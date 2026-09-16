using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Temporalio.Api.Enums.V1;
using Temporalio.Common;
using TemporalCommunity.Extensions.AI;

namespace WithLove.Workflows.Tests.Integration.Chat;

/// <summary>
/// Locks in how a durable tool distinguishes "this does not exist" from "this call failed".
/// </summary>
/// <remarks>
/// <para>
/// The bug these guard against is quiet by construction. A tool that catches an infrastructure
/// failure and returns apologetic prose <i>completes successfully</i>: Temporal records a healthy
/// activity, the retry policy never fires, and the model relays advice that can never work. Nothing
/// goes red. So a test that only asserts "the tool threw" is not enough either — throwing
/// non-retryably and throwing retryably produce the same exception at the call site but completely
/// different durable behaviour.
/// </para>
/// <para>
/// Each test therefore asserts three independent signals that cannot all be satisfied by accident:
/// the number of HTTP attempts the transport actually saw, the
/// <see cref="ActivityFailure.RetryState"/> Temporal recorded when it gave up, and the
/// <c>nonRetryable</c> flag on the application failure itself.
/// </para>
/// </remarks>
[Collection(GiftShopChatTemporalCollection.Name)]
public class GiftShopChatToolFailureTests(GiftShopChatTemporalFixture fixture)
{
    private const string InvokeFunctionActivity = "TemporalCommunity.Extensions.AI.InvokeFunction";
    private const string ProductsApiErrorType = "ProductsApiRequestFailed";

    /// <summary>
    /// Production retry attempts, mirrored from <c>ConfigureDurableExecution</c>. The interval is
    /// shortened for the test but the attempt count is not, because the attempt count is asserted.
    /// </summary>
    private const int ProductionMaximumAttempts = 3;

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task TransientProductsApiFailure_FailsTheActivityAndTemporalRetriesIt(
        HttpStatusCode status)
    {
        var handler = ScriptedProductsHandler.AlwaysReturns(status);
        var chatClient = new ScriptedGiftShopChatClient((call, _, _) => call == 1
            ? ToolCalls(Call("fail-1", "get_product_details", ("productId", 42)))
            : Final("Recovered."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            WithFastRetries,
            handler);
        var handle = await StartWorkflowAsync(harness, $"giftshop-chat-5xx-{Guid.NewGuid():N}");

        await RunTurnAsync(handle, "transient", "Tell me about product 42");

        // The transport is the ground truth for "did Temporal retry". Intermediate activity
        // attempts are not written to workflow history, so counting ActivityTaskFailed events
        // alone would report 1 whether the failure was retried three times or not retried at all.
        handler.AttemptCount.Should().Be(
            ProductionMaximumAttempts,
            "a transient failure must be retried up to the policy's attempt limit");

        var failures = await GetActivityFailuresAsync(handle);
        var toolFailures = failures.Where(f => f.ActivityType == InvokeFunctionActivity).ToList();
        toolFailures.Should().NotBeEmpty("swallowing the failure would complete the activity");
        toolFailures.Should().OnlyContain(f => f.ErrorType == ProductsApiErrorType);
        toolFailures.Should().OnlyContain(f => !f.NonRetryable);
        toolFailures.Should().OnlyContain(
            f => f.RetryState == RetryState.MaximumAttemptsReached,
            "Temporal records why it stopped; exhausting attempts proves the retries ran");
        toolFailures.Should().OnlyContain(f => f.Message.Contains("get_product_details"));

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task MissingProduct_IsAnsweredAsAStringWithoutFailingTheActivity()
    {
        var handler = ScriptedProductsHandler.AlwaysReturns(HttpStatusCode.NotFound);
        string? toolResult = null;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, _) => call == 1
            ? ToolCalls(Call("missing-1", "get_product_details", ("productId", 42)))
            : Final(CaptureFirstToolResult(messages, ref toolResult)));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            WithFastRetries,
            handler);
        var handle = await StartWorkflowAsync(harness, $"giftshop-chat-404-{Guid.NewGuid():N}");

        var result = await RunTurnAsync(handle, "missing", "Tell me about product 42");

        // A 404 is a real answer, not a fault: the product does not exist and a retry returns the
        // same 404 forever. It must reach the model as text so it can correct itself in-turn.
        result.Should().NotBeNull();
        result!.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        toolResult.Should().NotBeNull();
        toolResult.Should().Contain("There is no product with ID 42");
        handler.AttemptCount.Should().Be(1, "a 404 is an answer, so it must not be retried");

        var failures = await GetActivityFailuresAsync(handle);
        failures.Should().NotContain(f => f.ActivityType == InvokeFunctionActivity);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task MalformedProductsApiRequest_FailsWithoutBurningTheRetryBudget(
        HttpStatusCode status)
    {
        var handler = ScriptedProductsHandler.AlwaysReturns(status);
        var chatClient = new ScriptedGiftShopChatClient((call, _, _) => call == 1
            ? ToolCalls(Call("bad-1", "search_products", ("query", "keepsake")))
            : Final("Recovered."));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            WithFastRetries,
            handler);
        var handle = await StartWorkflowAsync(harness, $"giftshop-chat-4xx-{Guid.NewGuid():N}");

        await RunTurnAsync(handle, "malformed", "Find me a keepsake");

        // The request itself is wrong. Three identical attempts would fail three identical ways,
        // so the budget is reserved for failures a retry could actually clear.
        handler.AttemptCount.Should().Be(1, "a non-retryable failure must not be re-attempted");

        var failures = await GetActivityFailuresAsync(handle);
        var toolFailures = failures.Where(f => f.ActivityType == InvokeFunctionActivity).ToList();
        toolFailures.Should().NotBeEmpty();
        toolFailures.Should().OnlyContain(f => f.ErrorType == ProductsApiErrorType);
        toolFailures.Should().OnlyContain(f => f.NonRetryable);
        toolFailures.Should().OnlyContain(f => f.RetryState == RetryState.NonRetryableFailure);
        toolFailures.Should().OnlyContain(f => f.Message.Contains("search_products"));

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoyaltyLookupFailure_FailsTheActivityInsteadOfApologizing()
    {
        string? toolResult = null;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, _) => call == 1
            ? ToolCalls(Call("loyalty-1", "view_loyalty_points"))
            : Final(CaptureFirstToolResult(messages, ref toolResult)));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            WithFastRetries);
        var userId = $"user-{Guid.NewGuid():N}";

        // Occupy the loyalty workflow ID with a workflow that has no GetLoyaltyProfile query
        // handler. The query then fails with something that is not RpcException(NotFound) — the
        // "infrastructure is broken" shape — while remaining completely deterministic.
        await fixture.Environment.Client.StartWorkflowAsync(
            (GiftShopSharedWorkerStatusWorkflow workflow) => workflow.RunAsync(),
            new WorkflowOptions($"loyalty-{userId}", harness.TaskQueue));

        var handle = await StartWorkflowAsync(
            harness,
            GiftShopChatWorkflow.WorkflowIdFor(userId));

        await RunTurnAsync(
            handle,
            "loyalty-broken",
            "How many Love Tokens do I have?",
            new UserContext("Riley", userId));

        var failures = await GetActivityFailuresAsync(handle);
        var toolFailures = failures.Where(f => f.ActivityType == InvokeFunctionActivity).ToList();
        toolFailures.Should().NotBeEmpty(
            "a broken loyalty lookup must fail the activity, not complete it with prose");
        toolFailures.Should().OnlyContain(f => f.RetryState == RetryState.MaximumAttemptsReached);
        toolResult.Should().NotContain("Please try again in a moment");

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task LoyaltyAccountThatNeverExisted_IsAnsweredWithoutFailingTheActivity()
    {
        string? toolResult = null;
        var chatClient = new ScriptedGiftShopChatClient((call, messages, _) => call == 1
            ? ToolCalls(Call("loyalty-2", "view_loyalty_points"))
            : Final(CaptureFirstToolResult(messages, ref toolResult)));
        await using var harness = await GiftShopChatWorkerHarness.StartAsync(
            fixture.Environment,
            chatClient,
            WithFastRetries);
        var userId = $"user-{Guid.NewGuid():N}";
        var handle = await StartWorkflowAsync(
            harness,
            GiftShopChatWorkflow.WorkflowIdFor(userId));

        // The contrast case for the test above, and the reason the loyalty catch cannot simply be
        // deleted: a customer who has never earned a token has no loyalty workflow, and that is an
        // answer. Retrying it would return NotFound forever.
        var result = await RunTurnAsync(
            handle,
            "loyalty-new",
            "How many Love Tokens do I have?",
            new UserContext("Riley", userId));

        result.Should().NotBeNull();
        result!.CompletionReason.Should().Be(DurableTurnCompletionReason.FinalResponse);
        toolResult.Should().NotBeNull();
        toolResult.Should().Contain("don't have any Love Tokens yet");
        (await GetActivityFailuresAsync(handle))
            .Should().NotContain(f => f.ActivityType == InvokeFunctionActivity);

        await handle.SignalAsync(workflow => workflow.RequestShutdownAsync());
    }

    /// <summary>
    /// Keeps the production attempt count while collapsing the production backoff, so a test that
    /// deliberately exhausts the retry budget costs milliseconds rather than six seconds.
    /// </summary>
    private static DurableChatWorkflowInput WithFastRetries(DurableChatWorkflowInput input) =>
        input with
        {
            RetryPolicy = new RetryPolicy
            {
                InitialInterval = TimeSpan.FromMilliseconds(50),
                BackoffCoefficient = 1.0f,
                MaximumInterval = TimeSpan.FromMilliseconds(50),
                MaximumAttempts = ProductionMaximumAttempts,
            },
        };

    private Task<WorkflowHandle<GiftShopChatWorkflow>> StartWorkflowAsync(
        GiftShopChatWorkerHarness harness,
        string workflowId) =>
        fixture.Environment.Client.StartWorkflowAsync(
            (GiftShopChatWorkflow workflow) => workflow.RunAsync(harness.WorkflowInput),
            new WorkflowOptions(workflowId, harness.TaskQueue));

    /// <summary>
    /// Runs one turn, returning <see langword="null"/> when the update itself failed.
    /// </summary>
    /// <remarks>
    /// Whether a tool failure propagates out of the update or is reported inside the turn result is
    /// the package's choice, and deliberately not what these tests pin. Every assertion here reads
    /// Temporal history instead, which records what actually happened either way.
    /// </remarks>
    private static async Task<DurableTurnResult<GiftShopChatTurnState>?> RunTurnAsync(
        WorkflowHandle<GiftShopChatWorkflow> handle,
        string operationId,
        string message,
        UserContext? user = null)
    {
        try
        {
            return await handle.ExecuteUpdateAsync(
                workflow => workflow.SendMessageAsync(CreateRequest(operationId, message, user)),
                new WorkflowUpdateOptions { Id = operationId });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> CreateRequest(
        string operationId,
        string message,
        UserContext? user = null) =>
        new()
        {
            Messages = [new ChatMessage(ChatRole.User, message)],
            RequestData = new GiftShopChatRequestData(operationId, user),
            InitialTurnState = GiftShopChatTurnState.Create([]),
            CorrelationId = operationId,
            ConversationId = "tool-failure-test",
            ChatOptions = new ChatOptions
            {
                Instructions = GiftShopChatPrompt.BuildInstructions(user),
            },
            Options = new DurableTurnOptions
            {
                DispatchMode = DurableToolDispatchMode.Sequential,
            },
        };

    private static ChatResponse ToolCalls(params FunctionCallContent[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, calls));

    private static ChatResponse Final(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    private static FunctionCallContent Call(
        string callId,
        string name,
        params (string Name, object? Value)[] arguments) =>
        new(callId, name, arguments.ToDictionary(a => a.Name, a => a.Value));

    private static string CaptureFirstToolResult(
        IReadOnlyList<ChatMessage> messages,
        ref string? captured)
    {
        var result = messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .FirstOrDefault();

        // Unwrap rather than serialize. JsonSerializer escapes apostrophes to ' by default,
        // so serializing here would make every assertion about LA's prose — which is full of
        // contractions — silently fail to match for a reason that has nothing to do with the tool.
        captured = result?.Result switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            var value => JsonSerializer.Serialize(value),
        };
        return "Understood.";
    }

    /// <summary>
    /// Reads every activity failure from workflow history, joined back to the activity type that
    /// produced it and to the retry state Temporal assigned when it stopped trying.
    /// </summary>
    private static async Task<List<ActivityFailure>> GetActivityFailuresAsync(WorkflowHandle handle)
    {
        var scheduledActivityTypes = new Dictionary<long, string>();
        var failures = new List<ActivityFailure>();

        await foreach (var historyEvent in handle.FetchHistoryEventsAsync())
        {
            if (historyEvent.ActivityTaskScheduledEventAttributes is { } scheduled)
            {
                scheduledActivityTypes[historyEvent.EventId] = scheduled.ActivityType.Name;
            }
            else if (historyEvent.ActivityTaskFailedEventAttributes is { } failed)
            {
                var application = failed.Failure?.ApplicationFailureInfo;
                failures.Add(new ActivityFailure(
                    scheduledActivityTypes.GetValueOrDefault(failed.ScheduledEventId, "<unknown>"),
                    application?.Type,
                    application?.NonRetryable ?? false,
                    failed.RetryState,
                    failed.Failure?.Message ?? string.Empty));
            }
        }

        return failures;
    }

    private sealed record ActivityFailure(
        string ActivityType,
        string? ErrorType,
        bool NonRetryable,
        RetryState RetryState,
        string Message);
}
