using System.Diagnostics.Metrics;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.DiagnosticSource;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Runtime;
using WithLove.Data;
using WithLove.OpenInference;
using WithLove.WorkflowServer.Services;
using WithLove.WorkflowServer.Telemetry;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOpenInferenceDefaults();

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddWorkflowServerTracingSources();
        tracing.AddSource(
            TracingInterceptor.ClientSource.Name,
            TracingInterceptor.WorkflowsSource.Name,
            TracingInterceptor.ActivitiesSource.Name);
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Instrumentation.ActivitySourceName);
        metrics.AddMeter("temporal");
    });

builder.AddDefaultHealthChecks();

builder.Services.AddStripe();

builder.Services.AddSingleton<AzureSqlTokenInterceptor>(
    _ => new AzureSqlTokenInterceptor(new DefaultAzureCredential()));

builder.Services.AddDbContext<ProductsDbContext>((sp, options) =>
{
    var raw = builder.Configuration.GetConnectionString("productsDatabase") ?? string.Empty;
    var (connStr, useTokenAuth) = AzureSqlTokenInterceptor.StripAuthenticationKeyword(raw);

    var sqlOptions = new Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>(
        o => o.EnableRetryOnFailure());

    if (useTokenAuth)
    {
        var interceptor = sp.GetRequiredService<AzureSqlTokenInterceptor>();
        options.UseSqlServer(connStr, sqlOptions).AddInterceptors(interceptor);
    }
    else
    {
        options.UseSqlServer(connStr, sqlOptions);
    }
});

builder.EnrichSqlServerDbContext<ProductsDbContext>(
    configureSettings: settings =>
    {
        settings.DisableHealthChecks = false;
        settings.DisableTracing = false;
        settings.DisableRetry = false;
        settings.CommandTimeout = 60;
    });

var openaiKey = builder.Configuration.GetValue<string>("OPENAI_API_KEY", string.Empty);
builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", openaiKey)
        .AsIEmbeddingGenerator()
        .WithOpenTelemetryInstrumentation(Instrumentation.ActivitySourceName));

builder.Services.AddChatClient(
    new GenAiMessageContentChatClient(
        // "gpt-5-nano" is a floating alias, not a dated pin such as "gpt-5-nano-2025-08-07", and
        // that is deliberate. WithLove is a reference sample: a dated snapshot eventually retires,
        // and someone cloning this months from now would hit a hard failure with an unhelpful
        // error. Slight model drift beats a sample that is reproducible and dead. The usual reason
        // to pin — keeping prompt A/B results attributable to one model version — does not apply
        // here, because this repo has no eval harness by choice. Pin the date if that changes.
        new OpenAI.Chat.ChatClient("gpt-5-nano", openaiKey).AsIChatClient(),
        OpenInferenceTraceConfig.Default))
    .Build();

builder.Services.AddHttpClient("productsApi", client =>
{
    client.BaseAddress = new Uri("https+http://productsApi");
    client.DefaultRequestHeaders.Add("X-WITHLOVE-API-VERSION", DateTime.Today.ToString("yyyy-MM-dd"));
});

builder.Services.AddSingleton<Instrumentation>();

// Meter must outlive the runtime — register as singleton for proper disposal.
var temporalMeter = new Meter("temporal", "1.0.0");
builder.Services.AddSingleton(temporalMeter);

var temporalRuntime = new TemporalRuntime(new TemporalRuntimeOptions
{
    Telemetry = new TelemetryOptions
    {
        Metrics = new MetricsOptions
        {
            CustomMetricMeter = new CustomMetricMeter(temporalMeter),
        }
    }
});
builder.Services.AddSingleton(temporalRuntime);

var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

var temporalWorker = builder.Services.AddHostedTemporalWorker(
        clientTargetHost: connectOptions.TargetHost ?? "localhost:7233",
        clientNamespace: connectOptions.Namespace,
        taskQueue: "with-love-tasks")
    .ConfigureOptions(opts =>
    {
        opts.ClientOptions ??= new();
        opts.ClientOptions.Runtime = temporalRuntime;
        opts.ClientOptions.Interceptors = [Extensions.CreateSafeTemporalTracingInterceptor()];
        if (connectOptions.ApiKey is not null)
        {
            opts.ClientOptions.ApiKey = connectOptions.ApiKey;
            opts.ClientOptions.Tls = connectOptions.Tls; // TlsOptions; null is fine — SDK auto-enables TLS when ApiKey is set
        }
        opts.Interceptors =
        [
            Extensions.CreateSafeTemporalTracingInterceptor(),
            new TemporalUpdateTraceContextInterceptor(),
        ];
    })
    .AddScopedActivities<DatabaseActivities>()
    .AddScopedActivities<CustomerOnboardingActivities>()
    .AddScopedActivities<StripeCheckoutOrderActivities>()
    .AddScopedActivities<LoyaltyActivities>()
    .AddWorkflow<DatabaseSetupWorkflow>()
    .AddWorkflow<CustomerOnboardingWorkflow>()
    .AddWorkflow<StripeCheckoutOrderWorkflow>()
    .AddWorkflow<LoyaltyAccountWorkflow>();

temporalWorker.ConfigureGiftShopChatWorker(
    (function, metadata) => new OpenInferenceToolFunction(
        function,
        metadata.ToolCallId,
        metadata.ConversationId,
        metadata.CorrelationId,
        OpenInferenceTraceConfig.Default));

builder.Services.AddHostedService<DatabaseSetupHostedService>();

var app = builder.Build();

app.MapHealthCheckEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
await app.RunAsync();
