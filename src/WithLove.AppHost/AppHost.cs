using WithLove.AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(args);

var isTestMode = builder.Configuration["TESTING"] == "true";

// Centralized parameters — set once via user secrets or AppHost appsettings,
// injected into projects as environment variables by Aspire.
var openaiKey = builder.AddParameter("openai-api-key", secret: true);
var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);
var stripeWebhookSecret = builder.AddParameter("stripe-webhook-secret", secret: true);
var stripePublicKey = builder.AddParameter("stripe-public-key");

// Infrastructure
var redisCache = builder.AddRedis("redisCache")
    .WithDataVolume()
    //.WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight();

var sqlserver = builder.AddSqlServer("sqlServer")
    .WithDockerfile("Resources/mssql-fts")
    .WithDataVolume()
    //.WithLifetime(ContainerLifetime.Persistent)
    .WithDbGate();

var productsDb = sqlserver.AddDatabase("productsDatabase");

// Products API — needs OpenAI for embeddings
var productsApi = builder.AddProject<Projects.WithLove_ProductsAPI>("productsApi")
    .WithEndpoint("scalar", callback: endpoint =>
    {
        endpoint.Port = 7001;
        endpoint.UriScheme = "https";
        endpoint.Transport = "http";
    }).WithUrlForEndpoint("scalar", url =>
    {
        url.DisplayText = "Scalar";
        url.Url = "/scalar";
    })
    .WithEnvironment("OPENAI_API_KEY", openaiKey)
    .WaitFor(redisCache)
    .WithReference(redisCache)
    .WaitFor(productsDb)
    .WithReference(productsDb);

if (!isTestMode)
{
    var temporalServer = builder.AddTemporalDevContainer("temporal-server", opts =>
    {
        opts.Namespace = "default";
    });

    // Workflow Server — needs OpenAI for chat inference, Stripe for order processing
    builder.AddProject<Projects.WithLove_WorkflowServer>("workflowServer")
        .WithEnvironment("OPENAI_API_KEY", openaiKey)
        .WithEnvironment("Stripe__Default__ApiKey", stripeApiKey)
        .WithEnvironment("Stripe__Default__WebhookSecret", stripeWebhookSecret)
        .WaitFor(temporalServer)
        .WithReference(temporalServer)
        .WaitFor(productsDb)
        .WithReference(productsDb)
        .WithReference(productsApi);

    // Web frontend — needs OpenAI for search, Stripe for checkout
    builder.AddProject<Projects.WithLove_Web>("shopSite")
        .WithEnvironment("OPENAI_API_KEY", openaiKey)
        .WithEnvironment("Stripe__Default__ApiKey", stripeApiKey)
        .WithEnvironment("Stripe__Default__WebhookSecret", stripeWebhookSecret)
        .WithEnvironment("Stripe__Default__PublicKey", stripePublicKey)
        .WaitFor(redisCache)
        .WithReference(redisCache)
        .WaitFor(productsDb)
        .WithReference(productsDb)
        .WaitFor(productsApi)
        .WaitFor(temporalServer)
        .WithReference(temporalServer)
        .WithReference(productsApi);
}

builder.Build().Run();
