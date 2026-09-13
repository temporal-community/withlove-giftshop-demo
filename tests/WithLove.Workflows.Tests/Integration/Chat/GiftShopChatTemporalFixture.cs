using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TemporalCommunity.Extensions.AI;
using Temporalio.Common;
using Temporalio.Extensions.Hosting;
using WithLove.OpenInference;
using WithLove.WorkflowServer.Telemetry;

namespace WithLove.Workflows.Tests.Integration.Chat;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GiftShopChatTemporalCollection : ICollectionFixture<GiftShopChatTemporalFixture>
{
    public const string Name = "GiftShop durable chat";
}

public sealed class GiftShopChatTemporalFixture : IAsyncLifetime
{
    public WorkflowEnvironment Environment { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Environment = await WorkflowEnvironment.StartLocalAsync(new()
        {
            DevServerOptions = new() { DownloadVersion = "v1.7.2" },
            // Mirrors the AppHost declaration. ConfigureDurableExecution sets
            // EnableSearchAttributes = true, so DurableChatWorkflowBase upserts these at start
            // and after every turn; without them registered the upsert fails and every
            // workflow-level test fails at the first turn rather than on an assertion.
            SearchAttributes =
            [
                SearchAttributeKey.CreateLong("TurnCount"),
                SearchAttributeKey.CreateDateTimeOffset("SessionCreatedAt"),
            ],
        });
        Environment.Client.Options.DataConverter = DurableAIDataConverter.Instance;
    }

    public async Task DisposeAsync() => await Environment.DisposeAsync();
}

internal sealed class GiftShopChatWorkerHarness : IAsyncDisposable
{
    private readonly IHost _host;

    private GiftShopChatWorkerHarness(
        IHost host,
        string taskQueue,
        DurableChatWorkflowInput input)
    {
        _host = host;
        TaskQueue = taskQueue;
        WorkflowInput = input;
    }

    public string TaskQueue { get; }
    public DurableChatWorkflowInput WorkflowInput { get; }

    /// <summary>Starts a worker hosting the gift shop chat workflow and its durable tools.</summary>
    /// <param name="environment">The Temporal test environment to connect the worker to.</param>
    /// <param name="chatClient">Scripted model responses for the turn under test.</param>
    /// <param name="transformInput">
    /// Adjusts the workflow input before the workflow is started — used to shorten retry intervals
    /// so a test that deliberately provokes retries does not spend the production backoff waiting.
    /// </param>
    /// <param name="productsHandler">
    /// Transport for the <c>productsApi</c> named client. Defaults to
    /// <see cref="GiftShopProductsHandler"/>, which always succeeds.
    /// <para>
    /// This parameter is the failure-injection seam. Without it, every durable tool test could only
    /// exercise the happy path plus the incidental 404 the default handler returns for unknown
    /// paths, so the classification in <c>GiftShopChatToolService.IsResourceMissing</c> — which
    /// decides whether Temporal retries at all — had no test that could reach it.
    /// </para>
    /// </param>
    public static async Task<GiftShopChatWorkerHarness> StartAsync(
        WorkflowEnvironment environment,
        ScriptedGiftShopChatClient chatClient,
        Func<DurableChatWorkflowInput, DurableChatWorkflowInput>? transformInput = null,
        HttpMessageHandler? productsHandler = null,
        OpenInferenceTraceConfig? traceConfig = null,
        string? taskQueue = null)
    {
        var targetHost = environment.Client.Connection.Options.TargetHost
            ?? throw new InvalidOperationException("Temporal target host is unavailable.");

        // Random by default so concurrent harnesses cannot steal each other's tasks. A caller may
        // pin it to WorkflowConstants.DefaultTaskQueue when the code under test is the production
        // client — GiftShopChatWorkflowClient.EnsureStartedAsync hardcodes that queue, and routing
        // around it would leave the very policies the test exists to exercise untested. Safe only
        // because this collection disables parallelization.
        taskQueue ??= $"giftshop-chat-test-{Guid.NewGuid():N}";
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddChatClient(
            new GenAiMessageContentChatClient(
                chatClient,
                traceConfig ?? OpenInferenceTraceConfig.Default)).Build();
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new NoopEmbeddingGenerator());
        builder.Services.AddHttpClient("productsApi", client =>
                client.BaseAddress = new Uri("http://products.test"))
            .ConfigurePrimaryHttpMessageHandler(
                () => productsHandler ?? new GiftShopProductsHandler());

        var worker = builder.Services
            .AddHostedTemporalWorker(
                targetHost,
                environment.Client.Options.Namespace,
                taskQueue)
            .ConfigureOptions(options =>
            {
                options.ClientOptions ??= new();
                options.ClientOptions.Interceptors =
                    [Microsoft.Extensions.Hosting.Extensions.CreateSafeTemporalTracingInterceptor()];
                options.Interceptors =
                [
                    Microsoft.Extensions.Hosting.Extensions.CreateSafeTemporalTracingInterceptor(),
                    new TemporalUpdateTraceContextInterceptor(),
                ];
            })
            .ConfigureGiftShopChatWorker(
                (function, metadata) => new OpenInferenceToolFunction(
                    function,
                    metadata.ToolCallId,
                    metadata.ConversationId,
                    metadata.CorrelationId,
                    traceConfig ?? OpenInferenceTraceConfig.Default))
            .AddWorkflow<GiftShopSharedWorkerStatusWorkflow>()
            .AddWorkflow<LoyaltyAccountWorkflow>();

        var host = builder.Build();
        await host.StartAsync();

        var clientServices = new ServiceCollection();
        clientServices.AddLogging();
        clientServices.AddDurableChatWorkflowInputFactory(
            taskQueue,
            GiftShopChatRegistrationExtensions.ConfigureDurableExecution);
        foreach (var declaration in GiftShopChatToolCatalog.CreateDeclarations())
            clientServices.AddDurableToolDeclaration(declaration);
        await using var provider = clientServices.BuildServiceProvider();
        var input = provider.GetRequiredService<IDurableChatWorkflowInputFactory>().Create();
        input = transformInput?.Invoke(input) ?? input;

        return new GiftShopChatWorkerHarness(host, taskQueue, input);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed class ScriptedGiftShopChatClient : IChatClient
{
    private readonly Func<int, IReadOnlyList<ChatMessage>, ChatOptions?, Task<ChatResponse>> _next;
    private int _callCount;

    public ScriptedGiftShopChatClient(
        Func<int, IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> next)
        : this((call, messages, options) => Task.FromResult(next(call, messages, options)))
    {
    }

    public ScriptedGiftShopChatClient(
        Func<int, IReadOnlyList<ChatMessage>, ChatOptions?, Task<ChatResponse>> next) =>
        _next = next;

    public int CallCount => Volatile.Read(ref _callCount);
    public ConcurrentQueue<IReadOnlyList<ChatMessage>> Requests { get; } = new();
    public ChatClientMetadata Metadata { get; } = new("giftshop-scripted");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToList();
        Requests.Enqueue(materialized);
        var call = Interlocked.Increment(ref _callCount);
        return await _next(call, materialized, options);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class NoopEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata { get; } = new("noop", null, null, 1);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(_ => new Embedding<float>(new[] { 0f })).ToList()));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// A ProductsAPI transport that returns whatever the test tells it to, and counts attempts.
/// </summary>
/// <remarks>
/// <para>
/// The attempt count is the point. Asserting that a durable tool <i>threw</i> only proves the tool
/// refused to answer; it does not prove Temporal treated the failure as retryable. Those are
/// different outcomes with the same observable exception, and a non-retryable classification is
/// exactly the regression that would go unnoticed. Counting the HTTP attempts the worker actually
/// made — alongside <c>ActivityTaskFailed</c> in workflow history — distinguishes them.
/// </para>
/// <para>
/// Deliberately not disposable-sensitive: <see cref="IHttpClientFactory"/> owns the primary handler
/// and may dispose it while the test still holds the reference to read
/// <see cref="AttemptCount"/>. <see cref="HttpMessageHandler.Dispose(bool)"/> is a no-op here, so
/// the counter outlives the pipeline.
/// </para>
/// </remarks>
internal sealed class ScriptedProductsHandler(
    Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _attemptCount;

    /// <summary>Number of requests that reached this transport across all retries.</summary>
    public int AttemptCount => Volatile.Read(ref _attemptCount);

    /// <summary>Paths requested, in order, so a test can confirm which tool was exercised.</summary>
    public ConcurrentQueue<string> Paths { get; } = new();

    /// <summary>Always answers with <paramref name="status"/> and an empty JSON body.</summary>
    public static ScriptedProductsHandler AlwaysReturns(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attemptCount);
        Paths.Enqueue(request.RequestUri?.PathAndQuery ?? string.Empty);
        return Task.FromResult(respond(request));
    }
}

internal sealed class GiftShopProductsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        var json = path switch
        {
            "/api/products/7" => ProductJson,
            var value when value.StartsWith("/api/products/search", StringComparison.Ordinal) =>
                $$"""{"value":[{{ProductJson}}]}""",
            "/api/categories" => """{"value":[{"id":3,"name":"Comfort","description":"Warm gifts"}]}""",
            // navigate_to_collection verifies the model-supplied ID against this route before it
            // records a navigation, so an unmapped ID here now means "no such collection".
            "/api/categories/3" => """{"id":3,"name":"Comfort","description":"Warm gifts"}""",
            "/api/products/category/3" => $$"""{"value":[{{ProductJson}}]}""",
            _ => string.Empty,
        };

        var response = new HttpResponseMessage(
            string.IsNullOrEmpty(json) ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }

    private const string ProductJson =
        """
        {"id":7,"name":"Keepsake Box","price":25.00,"categoryName":"Comfort","subCategory":"Keepsakes","description":"A handcrafted box.","imageUrl":"/images/7.jpg","stripePriceId":"price_7","materials":[{"name":"Wood"}],"storyTitle":"Made with care"}
        """;
}
