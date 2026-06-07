using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.EntityFrameworkCore;
using Temporalio.Common.EnvConfig;
using WithLove.Data;
using WithLove.Data.Models;
using WithLove.Web.Services;
using WithLove.Web.Middleware;
using Microsoft.AspNetCore.Components.Web;
using Stripe.Extensions.AspNetCore;
using WithLove.Web;
using WithLove.Web.Components;
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

// Add validation support for Minimal APIs
builder.Services.AddValidation();

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


builder.Services.AddIdentityCore<ShopUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;

        options.User.RequireUniqueEmail = true;
        
        // Relax default password settings.
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 4;
        options.Password.RequiredUniqueChars = 0;
    })
    .AddEntityFrameworkStores<ProductsDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<ShopUserClaimsPrincipalFactory>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Note: This needs to be called after AddAuthentication().AddIdentityCookies() so it can configure the correct cookie options
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied"; //TODO: Implement this page to show when users are denied access due to authorization policies

    // Security & Persistence
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(2); // How long they stay logged in
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Add custom instrumentation for manual tracing
builder.Services.AddSingleton<Instrumentation>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .RegisterPersistentService<AnonymousCartSession>(RenderMode.InteractiveServer);

// Register FusionCache with Redis backplane
builder.Services.AddMemoryCache();
builder.AddRedisDistributedCache(connectionName: "redisCache");

builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromDays(30),
        IsFailSafeEnabled = false,
        AllowBackgroundDistributedCacheOperations = true,
    })
    .WithSystemTextJsonSerializer()
    .WithRegisteredDistributedCache()
    .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
    {
        Configuration = builder.Configuration.GetConnectionString("redisCache")
    }));

// Register product service (HTTP-based with Aspire service discovery)
builder.Services.AddHttpClient<IProductService, ProductApiService>(client =>
{
    // Use https+http:// scheme for development (try HTTPS first, fall back to HTTP)
    // In production, this would be https:// only
    client.BaseAddress = new Uri("https+http://productsApi");

    // Set required API version header (YYYY-MM-DD format)
    client.DefaultRequestHeaders.Add("X-WITHLOVE-API-VERSION", DateTime.Today.ToString("yyyy-MM-dd"));
});

builder.Services.AddStripe();

// Register anonymous cart session (scoped: one per SignalR circuit, shared between middleware and components)
builder.Services.AddScoped<AnonymousCartSession>();

// Register cart service (with FusionCache persistence)
builder.Services.AddScoped<ICartService, FusionCacheCartService>();

// Register chat service (Temporal-backed conversational assistant)
builder.Services.AddScoped<ChatService>();

// Register loyalty service (Temporal-backed Love Tokens loyalty points)
builder.Services.AddScoped<ILoyaltyService, TemporalLoyaltyService>();

var connectOptions = ClientEnvConfig.LoadClientConnectOptions();
builder.Services.AddTemporalClient(connectOptions.TargetHost, clientNamespace: connectOptions.Namespace);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseMiddleware<AnonymousCartMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapStripeWebhookHandler<StripeEventHandler>();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/logout", async (SignInManager<ShopUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
});

app.MapHealthCheckEndpoints();

app.Run();