using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using WithLove.Data;
using WithLove.ProductsAPI.Endpoints;
using WithLove.ProductsAPI.Middleware;
using WithLove.ProductsAPI.Services;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (resilience)
builder.AddServiceDefaults();

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySourceName);
        tracing.AddFusionCacheInstrumentation();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Instrumentation.ActivitySourceName);
        metrics.AddFusionCacheInstrumentation();
    });

// Health Checks
builder.AddDefaultHealthChecks();

// Add Entity Framework Core with SQL Server and Aspire integration
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
        settings.CommandTimeout = 20;
    });

// Add validation support for Minimal APIs
builder.Services.AddValidation();

// Add custom instrumentation for manual tracing
builder.Services.AddSingleton<Instrumentation>();

// Add OpenAPI documentation
builder.Services.AddOpenApi();

// Add caching
builder.Services.AddMemoryCache();
builder.AddRedisDistributedCache(connectionName: "redisCache");

builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = TimeSpan.FromMinutes(5),
        FailSafeThrottleDuration = TimeSpan.FromSeconds(10),
        EagerRefreshThreshold = 0.95f,
        AllowBackgroundDistributedCacheOperations = true,
        Duration = TimeSpan.FromMinutes(4),

        JitterMaxDuration = TimeSpan.FromSeconds(2)
    })
    .WithSystemTextJsonSerializer()
    .WithRegisteredDistributedCache()
    .WithBackplane(
        new RedisBackplane(new RedisBackplaneOptions
            { Configuration = builder.Configuration.GetConnectionString("redisCache") })
    );

// Register embedding generator (OpenAI text-embedding-3-small)
var openaiKey = builder.Configuration["OPENAI_API_KEY"] ?? "";
builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", openaiKey)
        .AsIEmbeddingGenerator());

// Register embedding service (used for query-time embedding generation)
builder.Services.AddScoped<EmbeddingService>();

// Register caching services
builder.Services.AddScoped<IProductCacheService, ProductCacheService>();

var app = builder.Build();

// Global error handling middleware (must be early in pipeline)
app.UseErrorHandling();

// Add standard response headers (security and caching)
app.UseResponseHeaders();

// Map default Aspire endpoints (health checks)
app.MapHealthCheckEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Map Scalar API reference (default path: /scalar)
    app.MapScalarApiReference(options =>
    {
        options.AddDocument("v1", "WithLove Products API v1");
        options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
        options.ShowOperationId();
        options.WithTitle("WithLove Products API Documentation");
        options.WithTheme(ScalarTheme.Purple);
    });
}

app.UseHttpsRedirection();

// Map API endpoints with versioning filter
app.MapProductEndpoints();

app.MapCategoryEndpoints();

app.Run();