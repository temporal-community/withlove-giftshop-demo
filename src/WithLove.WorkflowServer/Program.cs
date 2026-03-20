using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Temporalio.Common.EnvConfig;
using Temporalio.Extensions.Hosting;
using WithLove.Data;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Workflows;
using WithLove.WorkflowServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (resilience)
builder.AddServiceDefaults();

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySourceName);
    });

// Health Checks
builder.AddDefaultHealthChecks();

// Add Stripe services
builder.Services.AddStripe();

// Register EF Core with the Aspire-injected connection string
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

// Register embedding generator (OpenAI text-embedding-3-small)
var openaiKey = builder.Configuration.GetValue<string>("OPENAI_API_KEY", string.Empty);
builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", openaiKey)
        .AsIEmbeddingGenerator());

// Register IChatClient for chat assistant (gpt-5-nano with function invocation)
builder.Services.AddChatClient(
    new OpenAI.Chat.ChatClient("gpt-5-nano", openaiKey).AsIChatClient())
    .UseFunctionInvocation();

// Register HttpClient for ProductsAPI access (Aspire service discovery)
builder.Services.AddHttpClient("productsApi", client =>
{
    client.BaseAddress = new Uri("https+http://productsApi");
    client.DefaultRequestHeaders.Add("X-WITHLOVE-API-VERSION", DateTime.Today.ToString("yyyy-MM-dd"));
});

// Add custom instrumentation for manual tracing
builder.Services.AddSingleton<Instrumentation>();

// Configure Temporal worker with activities and workflow
var connectOptions = ClientEnvConfig.LoadClientConnectOptions();

builder.Services.AddHostedTemporalWorker(
        clientTargetHost: connectOptions.TargetHost ?? "localhost:7233",
        clientNamespace: connectOptions.Namespace,
        taskQueue: "with-love-tasks")
    .AddScopedActivities<DatabaseActivities>()
    .AddScopedActivities<CustomerOnboardingActivities>()
    .AddScopedActivities<StripeCheckoutOrderActivities>()
    .AddScopedActivities<ChatAgentActivities>()
    .AddWorkflow<DatabaseSetupWorkflow>()
    .AddWorkflow<CustomerOnboardingWorkflow>()
    .AddWorkflow<StripeCheckoutOrderWorkflow>()
    .AddWorkflow<ChatAgentWorkflow>();

// Trigger the database setup workflow on startup
builder.Services.AddHostedService<DatabaseSetupHostedService>();

var app = builder.Build();

app.MapHealthCheckEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
await app.RunAsync();
