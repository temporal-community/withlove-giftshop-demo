using Temporalio.Common;
using WithLove.AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions()
{
    Args = args,
    DashboardApplicationName = "WithLove Resource Dashboard",    
});

var isTestMode = builder.Configuration["TESTING"] == "true";

// Secret Keys
var openaiKey = builder.AddParameter("openai-api-key", secret: true);
var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);
var stripePublicKey = builder.AddParameter("stripe-public-key", secret: true);

// Infrastructure
var stripe = builder.AddStripeCliContainer("stripe", apiKey: stripeApiKey, publishableKey: stripePublicKey);

var redisCache = builder.AddRedis("redisCache")
    //.WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight();

if (!isTestMode)
    redisCache.WithDataVolume();

var sqlserver = builder.AddSqlServer("sqlServer")
    .WithDockerfile("Resources/mssql-fts")
    //.WithLifetime(ContainerLifetime.Persistent)
    .WithDbGate();

if (!isTestMode)
    sqlserver.WithDataVolume();

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
        opts.SearchAttributes =
        [
            SearchAttributeKey.CreateKeyword("StripeSessionId"),
            SearchAttributeKey.CreateKeyword("CustomerId"),
        ];
    });

    // Workflow Server — needs OpenAI for chat inference, Stripe for order processing
    builder.AddProject<Projects.WithLove_WorkflowServer>("workflowServer")
        .WithEnvironment("OPENAI_API_KEY", openaiKey)
        .WithReference(stripe)
        .WaitFor(temporalServer)
        .WithReference(temporalServer)
        .WaitFor(productsDb)
        .WithReference(productsDb)
        .WithReference(productsApi);

    // Web frontend — needs OpenAI for search, Stripe for checkout
    var shopSite = builder.AddProject<Projects.WithLove_Web>("shopSite")
        .WithEnvironment("OPENAI_API_KEY", openaiKey)
        .WithReference(stripe)
        .WaitFor(redisCache)
        .WithReference(redisCache)
        .WaitFor(productsDb)
        .WithReference(productsDb)
        .WaitFor(productsApi)
        .WaitFor(temporalServer)
        .WithReference(temporalServer)
        .WithReference(productsApi);

    stripe.WithWebhookForwardTo(shopSite, "/stripe/webhook");
}

builder.Build().Run();
