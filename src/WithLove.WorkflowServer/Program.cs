using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.DiagnosticSource;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Runtime;
using WithLove.Data;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Workflows;
using WithLove.WorkflowServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySourceName);
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

builder.Services.AddDbContext<ProductsDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("productsDatabase"),
        sqlOptions => { sqlOptions.EnableRetryOnFailure(); });
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
        .AsIEmbeddingGenerator());

builder.Services.AddChatClient(
    new OpenAI.Chat.ChatClient("gpt-5-nano", openaiKey).AsIChatClient())
    .UseFunctionInvocation();

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

builder.Services.AddHostedTemporalWorker(
        clientTargetHost: connectOptions.TargetHost ?? "localhost:7233",
        clientNamespace: connectOptions.Namespace,
        taskQueue: "with-love-tasks")
    .ConfigureOptions(opts =>
    {
        opts.ClientOptions ??= new();
        opts.ClientOptions.Runtime = temporalRuntime;
        opts.ClientOptions.Interceptors = [new TracingInterceptor()];
        opts.Interceptors = [new TracingInterceptor()];
    })
    .AddScopedActivities<DatabaseActivities>()
    .AddScopedActivities<CustomerOnboardingActivities>()
    .AddScopedActivities<StripeCheckoutOrderActivities>()
    .AddScopedActivities<ChatAgentActivities>()
    .AddScopedActivities<LoyaltyActivities>()
    .AddWorkflow<DatabaseSetupWorkflow>()
    .AddWorkflow<CustomerOnboardingWorkflow>()
    .AddWorkflow<StripeCheckoutOrderWorkflow>()
    .AddWorkflow<ChatAgentWorkflow>()
    .AddWorkflow<LoyaltyAccountWorkflow>();

builder.Services.AddHostedService<DatabaseSetupHostedService>();

var app = builder.Build();

app.MapHealthCheckEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
await app.RunAsync();
