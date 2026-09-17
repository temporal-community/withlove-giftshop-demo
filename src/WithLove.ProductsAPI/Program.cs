using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Scalar.AspNetCore;
using StackExchange.Redis;
using WithLove.Data;
using WithLove.ProductsAPI.Endpoints;
using WithLove.ProductsAPI.Middleware;
using WithLove.ProductsAPI.Services;
using WithLove.OpenInference;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOpenInferenceDefaults();
builder.Services.AddSingleton(OpenInferenceTraceConfig.Default);

builder.ConfigureOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(Instrumentation.ActivitySourceName);
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Instrumentation.ActivitySourceName);
    });

builder.AddDefaultHealthChecks();

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
        settings.CommandTimeout = 20;
    });

builder.Services.AddValidation();

builder.Services.AddSingleton<Instrumentation>();

builder.Services.AddOpenApi();

builder.Services.AddMemoryCache();
var redisConnectionString = builder.Configuration.GetConnectionString("redisCache")
    ?? throw new InvalidOperationException("The redisCache connection string is required.");
var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

builder.Services.AddStackExchangeRedisCache(options =>
    options.ConfigurationOptions = redisOptions);

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
            { ConfigurationOptions = redisOptions })
    );

var openaiKey = builder.Configuration["OPENAI_API_KEY"] ?? "";
builder.Services.AddEmbeddingGenerator<string, Embedding<float>>(
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", openaiKey)
        .AsIEmbeddingGenerator()
        .WithOpenTelemetryInstrumentation(Instrumentation.ActivitySourceName));

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
