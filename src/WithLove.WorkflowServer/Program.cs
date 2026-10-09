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
using WithLove.WorkflowServer.Services;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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
        .AsIEmbeddingGenerator());

builder.Services.AddChatClient(
    new OpenAI.Chat.ChatClient("gpt-5-nano", openaiKey).AsIChatClient())
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
        opts.ClientOptions.Interceptors = [new TracingInterceptor()];
        if (connectOptions.ApiKey is not null)
        {
            opts.ClientOptions.ApiKey = connectOptions.ApiKey;
            opts.ClientOptions.Tls = connectOptions.Tls; // TlsOptions; null is fine — SDK auto-enables TLS when ApiKey is set
        }
        opts.Interceptors = [new TracingInterceptor()];
    })
    .AddScopedActivities<DatabaseActivities>()
    .AddScopedActivities<CustomerOnboardingActivities>()
    .AddScopedActivities<StripeCheckoutOrderActivities>()
    .AddScopedActivities<LoyaltyActivities>()
    .AddWorkflow<DatabaseSetupWorkflow>()
    .AddWorkflow<CustomerOnboardingWorkflow>()
    .AddWorkflow<StripeCheckoutOrderWorkflow>()
    .AddWorkflow<LoyaltyAccountWorkflow>();

temporalWorker.ConfigureGiftShopChatWorker();

builder.Services.AddHostedService<DatabaseSetupHostedService>();

var app = builder.Build();

app.MapHealthCheckEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
await app.RunAsync();
