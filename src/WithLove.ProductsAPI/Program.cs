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

builder.AddServiceDefaults();

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySourceName);
        tracing.AddFusionCacheInstrumentation(opts =>
        {
            opts.IncludeMemoryLevel = true;
            opts.IncludeDistributedLevel = true;
            opts.IncludeBackplane = true;
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Instrumentation.ActivitySourceName);
        metrics.AddFusionCacheInstrumentation(opts =>
        {
            opts.IncludeMemoryLevel = true;
            opts.IncludeDistributedLevel = true;
            opts.IncludeBackplane = true;
        });
    });

builder.AddDefaultHealthChecks();

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

builder.Services.AddValidation();

builder.Services.AddSingleton<Instrumentation>();

builder.Services.AddOpenApi();

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

var openaiKey = builder.Configuration["OPENAI_API_KEY"] ?? "";
builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", openaiKey)
        .AsIEmbeddingGenerator());

builder.Services.AddScoped<EmbeddingService>();

builder.Services.AddScoped<IProductCacheService, ProductCacheService>();

var app = builder.Build();

// Keep this first so downstream middleware returns Problem Details on failure.
app.UseErrorHandling();

app.UseResponseHeaders();

app.MapHealthCheckEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

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

app.MapProductEndpoints();

app.MapCategoryEndpoints();

app.Run();
