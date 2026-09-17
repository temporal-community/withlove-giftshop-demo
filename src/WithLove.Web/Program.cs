using Azure.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Trace;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;
using Temporalio.Converters;
using Temporalio.Extensions.OpenTelemetry;
using WithLove.Data;
using WithLove.Data.Models;
using WithLove.Web.Services;
using WithLove.Web.Middleware;
using WithLove.Web.Telemetry;
using Microsoft.AspNetCore.Components.Web;
using Stripe.Extensions.AspNetCore;
using WithLove.Web;
using WithLove.Web.Components;
using TemporalCommunity.Extensions.AI;
using WithLove.Workflows.Chat;
using WithLove.OpenInference;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOpenInferenceDefaults();

builder.ConfigureOpenTelemetry(
    aspNetCoreTracing =>
    {
        // Do not create specialized SignalR/Razor component spans. ServiceDefaults excludes the
        // generic Blazor transport requests while retaining normal application HTTP requests.
        aspNetCoreTracing.EnableAspNetCoreSignalRSupport = false;
        aspNetCoreTracing.EnableRazorComponentsSupport = false;
    },
    tracing =>
    {
        // GetHistory probes a lazily created workflow. Temporal reports the ordinary "not started"
        // case as an error whose exception message includes the raw workflow ID. Register this
        // before the exporter so only that low-value span is discarded at export time.
        tracing.AddProcessor(new ChatHydrationExportProcessor());
        tracing.AddSource(Instrumentation.ActivitySourceName);
        tracing.AddSource(TracingInterceptor.ClientSource.Name);
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Instrumentation.ActivitySourceName);
    });

builder.AddDefaultHealthChecks();

builder.Services.AddValidation();

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


builder.Services.AddIdentityCore<ShopUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;

        options.User.RequireUniqueEmail = true;
        
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

// Must run after AddIdentityCookies so it updates the application cookie scheme.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";

    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(2);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddSingleton<Instrumentation>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .RegisterPersistentService<AnonymousCartSession>(RenderMode.InteractiveServer)
    .RegisterPersistentService<AnonymousChatSession>(RenderMode.InteractiveServer);

builder.Services.AddMemoryCache();
var redisConnectionString = builder.Configuration.GetConnectionString("redisCache")
    ?? throw new InvalidOperationException("The redisCache connection string is required.");
var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

builder.Services.AddStackExchangeRedisCache(options =>
    options.ConfigurationOptions = redisOptions);

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
        ConfigurationOptions = redisOptions,
    }));

builder.Services.AddHttpClient<IProductService, ProductApiService>(client =>
{
    client.BaseAddress = new Uri("https+http://productsApi");

    client.DefaultRequestHeaders.Add("X-WITHLOVE-API-VERSION", DateTime.Today.ToString("yyyy-MM-dd"));
});

builder.Services.AddStripe();

builder.Services.AddScoped<AnonymousCartSession>();
builder.Services.AddScoped<AnonymousChatSession>();

builder.Services.AddScoped<ICartService, FusionCacheCartService>();

// Fully qualified to avoid adding Stripe using directives to Program.cs.
// SessionService is scoped so it takes a fresh StripeClient per request scope.
builder.Services.AddScoped(sp =>
    new Stripe.Checkout.SessionService(sp.GetRequiredService<Stripe.StripeClient>()));
builder.Services.AddScoped<IOrderService, StripeOrderService>();

builder.Services.AddScoped<ChatService>();
builder.Services.AddSingleton(OpenInferenceTraceConfig.Default);
builder.Services.AddSingleton(TelemetryIdentityFactory.Create());
builder.Services.AddScoped<IGiftShopChatWorkflowClient, GiftShopChatWorkflowClient>();
builder.Services.AddScoped<ChatIdentityRotator>();

builder.Services.AddScoped<ILoyaltyService, TemporalLoyaltyService>();

var connectOptions = ClientEnvConfig.LoadClientConnectOptions();
builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = connectOptions.TargetHost;
    opts.Namespace = connectOptions.Namespace;
    opts.Interceptors = [Extensions.CreateSafeTemporalTracingInterceptor()];
    if (connectOptions.ApiKey is not null)
    {
        opts.ApiKey = connectOptions.ApiKey;
        opts.Tls = connectOptions.Tls; // TlsOptions; null is fine — SDK auto-enables TLS when ApiKey is set
    }
});

// The Temporal client is shared: the durable chat workflow, TemporalLoyaltyService and the Stripe
// checkout workflow all use it, so one data converter has to serve all of them. Declare that
// dependency here instead of inheriting it from AddGiftShopChatWorkflowClient() below.
//
// AddGiftShopChatWorkflowClient() installs DurableAIDataConverterPlugin, which applies
// DurableAIDataConverter only while the converter is still DataConverter.Default; otherwise it
// logs and skips. The skip is silent, and its blast radius is asymmetric: arguments sent to the
// worker survive (DurableAI reads JSON case-insensitively) but results read back — LoyaltyProfile,
// ReservationResult, CheckoutSessionInfo — bind to default values with no exception. A converter
// mismatch must be a startup failure, not a field that quietly reads 0.
//
// PostConfigure so this runs after every Configure delegate, including any added by a plugin.
builder.Services.PostConfigure<TemporalClientConnectOptions>(opts =>
    opts.DataConverter = DurableAIConverterSetup.Resolve(opts.DataConverter));

builder.Services.AddGiftShopChatWorkflowClient();

var app = builder.Build();

// Must run first so all subsequent middleware (including UseHsts and UseHttpsRedirection)
// sees the correct scheme from ACA's X-Forwarded-Proto header.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
};
// Clear IP allowlists so the ACA ingress proxy is trusted regardless of its internal IP.
// This is safe because the container is not internet-reachable: ACA's managed ingress is the
// only entry point, and it strips/rewrites forwarded headers before they reach Kestrel.
// In a non-ACA environment (direct internet exposure), restore KnownIPNetworks with the
// specific proxy CIDR range instead of clearing it.
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseMiddleware<AnonymousCartMiddleware>();

// Immediately after the cart, and like it, before UseAuthentication — so it cannot see
// context.User, which is exactly why it only mints and never rotates. Rotation happens at the two
// places where the identity genuinely changes: Login.razor and the /logout endpoint below.
app.UseMiddleware<AnonymousChatMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapStripeWebhookHandler<StripeEventHandler>();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/logout", async (
    HttpContext context,
    SignInManager<ShopUser> signInManager,
    ChatIdentityRotator chatIdentity) =>
{
    await signInManager.SignOutAsync();

    // The shared-machine case, and the load-bearing half of the rotation invariant. Without this,
    // the next anonymous visitor on this browser inherits the pre-login wl-chat-id and resurrects a
    // running transcript. RotateAsync never throws: a logout must not fail because chat is down.
    await chatIdentity.RotateAsync(context);

    return Results.Redirect("/");
});

app.MapHealthCheckEndpoints();

app.Run();

/// <summary>
/// Reconciles the Temporal client's data converter with the MEAI-aware converter that the durable
/// chat workflow requires.
/// </summary>
/// <remarks>
/// The rule this encodes: adopting <c>DurableAIDataConverter</c> may replace the payload converter,
/// but it must never discard a <c>PayloadCodec</c> (encryption or compression) or overwrite a
/// converter the application chose deliberately. Assigning <c>DurableAIDataConverter.Instance</c>
/// unconditionally would drop a codec exactly as silently as the plugin drops the AI converter, so
/// an unreconcilable configuration fails loudly instead.
/// </remarks>
internal static class DurableAIConverterSetup
{
    /// <summary>
    /// Returns the converter the client should use, or throws when the configured converter cannot
    /// be reconciled with <c>DurableAIDataConverter</c>.
    /// </summary>
    /// <remarks>
    /// Comparisons here are deliberately <see cref="object.ReferenceEquals(object, object)"/> and
    /// not <c>GetType()</c>. <c>DurableAIDataConverter</c> reuses Temporal's
    /// <c>DefaultPayloadConverter</c> and only swaps its <c>JsonSerializerOptions</c>, so the stock
    /// converter and the AI converter are the same CLR type; a type comparison reports every stock
    /// client as "already AI-aware" and reintroduces the silent skip this method exists to prevent.
    /// <c>DataConverter.Default</c> and <c>DurableAIDataConverter.Instance</c> are both stable
    /// singletons, so identity is a sound test.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The configured converter uses a custom payload or failure converter, which would lose MEAI
    /// polymorphic content if kept and would lose the caller's intent if replaced.
    /// </exception>
    public static DataConverter Resolve(DataConverter configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        var durableAI = DurableAIDataConverter.Instance;

        // Already MEAI-aware, whether set here or upstream. Leave it — including its codec — alone.
        if (ReferenceEquals(configured.PayloadConverter, durableAI.PayloadConverter))
            return configured;

        var isStockConverter =
            ReferenceEquals(configured.PayloadConverter, DataConverter.Default.PayloadConverter)
            && ReferenceEquals(configured.FailureConverter, DataConverter.Default.FailureConverter);

        if (!isStockConverter)
        {
            throw new InvalidOperationException(
                "The Temporal client is configured with a custom payload or failure converter "
                + $"(payload: '{configured.PayloadConverter.GetType().FullName}', failure: "
                + $"'{configured.FailureConverter.GetType().FullName}') that cannot be reconciled "
                + "with DurableAIDataConverter. The chat workflow sends polymorphic "
                + "Microsoft.Extensions.AI content that the stock payload converter silently "
                + "degrades, and this client is shared with the loyalty and checkout workflows. "
                + "Build your converter on top of DurableAIDataConverter.Instance — for example "
                + "'DurableAIDataConverter.Instance with { PayloadCodec = yourCodec }' — rather "
                + "than replacing it.");
        }

        // Stock converter: adopt the MEAI-aware one, carrying over any codec so swapping the
        // payload converter cannot disable encryption or compression by accident.
        return configured.PayloadCodec is null
            ? durableAI
            : durableAI with { PayloadCodec = configured.PayloadCodec };
    }
}
