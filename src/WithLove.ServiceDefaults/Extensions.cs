using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Temporalio.Extensions.OpenTelemetry;
using WithLove.OpenInference;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    internal const string PhoenixOtlpTracesEndpointConfigurationKey = "Phoenix:OtlpTracesEndpoint";
    internal const string AxOtlpTracesEndpointConfigurationKey = "Arize:Tracing:Ax:Endpoint";
    internal const string AxApiKeyConfigurationKey = "Arize:Tracing:Ax:ApiKey";
    internal const string AxSpaceIdConfigurationKey = "Arize:Tracing:Ax:SpaceId";
    internal const string AxProtocolConfigurationKey = "Arize:Tracing:Ax:Protocol";
    internal const string AiOnlyTraceConfigurationKey = "Trace:AiOnly";
    internal const string AspireOtlpEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    internal const string OpenInferenceProjectNameConfigurationKey = "OpenInference:ProjectName";
    private const string OtlpServiceNameConfigurationKey = "OTEL_SERVICE_NAME";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler(options =>
            {
                // These timeouts govern HTTP service-to-service calls (HttpClient), not
                // SQL or Redis connections — those have their own timeout/retry settings.
                //
                // The default AttemptTimeout of 10s is too tight when a downstream service
                // experiences first-request latency (e.g. Web → productsApi while productsApi
                // is waiting on Azure SQL cold-start). The caller would cancel the HTTP
                // request before the downstream could respond, which propagates as a
                // TaskCanceledException in productsApi's logs. Raising to 30s gives each
                // HTTP hop enough runway to absorb typical cold-start delays.
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                // SamplingDuration must be >= 2x AttemptTimeout per Polly's validation rules.
                // Default is 30s; raise to 60s to satisfy the constraint.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>Configures shared OpenTelemetry signals and destination routing.</summary>
    /// <param name="builder">The application builder to configure.</param>
    /// <param name="configureAspNetCoreTracing">
    /// Optional application-specific ASP.NET Core tracing configuration. Its request filter is
    /// combined with the shared health endpoint exclusions.
    /// </param>
    /// <param name="configureTracing">
    /// Optional application-specific trace-pipeline configuration. The callback runs before the
    /// destination exporter is registered so filtering processors can precede export processors.
    /// </param>
    public static OpenTelemetryBuilder ConfigureOpenTelemetry<TBuilder>(
        this TBuilder builder,
        Action<AspNetCoreTraceInstrumentationOptions>? configureAspNetCoreTracing = null,
        Action<TracerProviderBuilder>? configureTracing = null)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var routing = TelemetryExportRouting.Resolve(builder.Configuration);
        var serviceName = ResolveServiceName(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (routing.ExportLogsToAspire)
                logging.AddOtlpExporter();
        });

        var openTelemetryBuilder = builder.Services.AddOpenTelemetry();
        openTelemetryBuilder.ConfigureResource(resource => resource.AddService(serviceName));
        openTelemetryBuilder
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (routing.ExportMetricsToAspire)
                    metrics.AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        ConfigureAspNetCoreTracing(options, configureAspNetCoreTracing))
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                configureTracing?.Invoke(tracing);

                if (routing.TraceDestination == TraceExportDestination.Aspire)
                {
                    tracing.AddOtlpExporter();
                }
                else if (routing.TraceDestination == TraceExportDestination.Phoenix)
                {
                    AddArizeTraceExporter(tracing, "phoenix", routing.ExportAiOnly, options =>
                    {
                        options.Endpoint = routing.PhoenixEndpoint!;
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = null;
                    });
                }
                else if (routing.TraceDestination == TraceExportDestination.Ax)
                {
                    AddArizeTraceExporter(tracing, "arize-ax", routing.ExportAiOnly, options =>
                    {
                        options.Endpoint = routing.Ax!.Endpoint;
                        options.Protocol = routing.Ax.Protocol;
                        options.Headers = routing.Ax.Headers;
                    });
                }
            });

        if (routing.TraceDestination == TraceExportDestination.None
            && !builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddSingleton<IHostedService>(services =>
                new TraceExportStartupDiagnostics(
                    services.GetRequiredService<ILogger<TraceExportStartupDiagnostics>>(),
                    builder.Environment.ApplicationName));
        }

        return openTelemetryBuilder;
    }

    private static void AddArizeTraceExporter(
        TracerProviderBuilder tracing,
        string name,
        bool exportAiOnly,
        Action<OtlpExporterOptions> configure)
    {
        if (!exportAiOnly)
        {
            tracing.AddOtlpExporter(name, configure);
            return;
        }

        var options = new OtlpExporterOptions();
        configure(options);
        tracing.AddProcessor(new AiOnlyTraceExportProcessor(new OtlpTraceExporter(options)));
    }

    internal static void ConfigureAspNetCoreTracing(
        AspNetCoreTraceInstrumentationOptions options,
        Action<AspNetCoreTraceInstrumentationOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        configure?.Invoke(options);

        var applicationFilter = options.Filter;
        options.Filter = context =>
            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
            && !IsStaticAssetRequest(context)
            && (applicationFilter?.Invoke(context) ?? true);
    }

    private static bool IsStaticAssetRequest(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            return false;

        var extension = Path.GetExtension(context.Request.Path.Value.AsSpan());
        return extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".map", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".eot", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wasm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stamps one OpenInference project name while retaining each process's service.name.</summary>
    public static TBuilder AddOpenInferenceDefaults<TBuilder>(this TBuilder builder, string? projectName = null)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var resolved = ResolveOpenInferenceProjectName(builder, projectName);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddOpenInferenceProjectName(resolved));
        return builder;
    }

    internal static string ResolveOpenInferenceProjectName(
        IHostApplicationBuilder builder,
        string? explicitProjectName = null) => FirstNonEmpty(
            explicitProjectName,
            builder.Configuration[OpenInferenceProjectNameConfigurationKey],
            builder.Configuration[OtlpServiceNameConfigurationKey],
            builder.Environment.ApplicationName)!;

    /// <summary>Creates a fresh Temporal interceptor that never exports the raw workflow ID.</summary>
    public static TracingInterceptor CreateSafeTemporalTracingInterceptor() =>
        new(new TracingInterceptorOptions { TagNameWorkflowId = null });

    private static string ResolveServiceName<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder =>
        FirstNonEmpty(
            builder.Configuration[OtlpServiceNameConfigurationKey],
            builder.Environment.ApplicationName)!;

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    public static IHealthChecksBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var healthChecksBuilder = builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return healthChecksBuilder;
    }

    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    private sealed class TraceExportStartupDiagnostics(
        ILogger<TraceExportStartupDiagnostics> logger,
        string activitySourceName) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogWarning(
                "No OTLP trace destination is configured for ActivitySource {ActivitySourceName}. Configure {PhoenixEndpointKey}, {AxEndpointKey}, or {AspireEndpointKey}.",
                activitySourceName,
                PhoenixOtlpTracesEndpointConfigurationKey,
                AxOtlpTracesEndpointConfigurationKey,
                AspireOtlpEndpointConfigurationKey);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

internal enum TraceExportDestination
{
    None,
    Aspire,
    Phoenix,
    Ax,
}

internal sealed record TelemetryExportRouting(
    TraceExportDestination TraceDestination,
    bool ExportLogsToAspire,
    bool ExportMetricsToAspire,
    bool ExportAiOnly,
    Uri? PhoenixEndpoint,
    AxExporterConfiguration? Ax)
{
    internal static TelemetryExportRouting Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var aspire = GetOptionalHttpEndpoint(configuration, Extensions.AspireOtlpEndpointConfigurationKey);
        var phoenix = GetOptionalHttpEndpoint(configuration, Extensions.PhoenixOtlpTracesEndpointConfigurationKey);
        var ax = GetOptionalAxExporterConfiguration(configuration);
        var aiOnly = GetAiOnlyTraceConfiguration(configuration);

        if (phoenix is not null && ax is not null)
        {
            throw new InvalidOperationException(
                $"Only one Arize trace destination can be configured. Remove either "
                + $"'{Extensions.PhoenixOtlpTracesEndpointConfigurationKey}' or "
                + $"'{Extensions.AxOtlpTracesEndpointConfigurationKey}'.");
        }

        return new(
            phoenix is not null
                ? TraceExportDestination.Phoenix
                : ax is not null
                    ? TraceExportDestination.Ax
                : aspire is not null ? TraceExportDestination.Aspire : TraceExportDestination.None,
            ExportLogsToAspire: aspire is not null,
            ExportMetricsToAspire: aspire is not null,
            ExportAiOnly: aiOnly,
            PhoenixEndpoint: phoenix,
            Ax: ax);
    }

    private static bool GetAiOnlyTraceConfiguration(IConfiguration configuration)
    {
        var value = configuration[Extensions.AiOnlyTraceConfigurationKey];
        if (value is null || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException(
            $"Configuration '{Extensions.AiOnlyTraceConfigurationKey}' must be 'true' or 'false'.");
    }

    private static Uri? GetOptionalHttpEndpoint(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be an absolute HTTP or HTTPS URI, but was '{value}'.");
        }
        return uri;
    }

    private static AxExporterConfiguration? GetOptionalAxExporterConfiguration(IConfiguration configuration)
    {
        var endpointValue = configuration[Extensions.AxOtlpTracesEndpointConfigurationKey];
        var apiKey = configuration[Extensions.AxApiKeyConfigurationKey];
        var spaceId = configuration[Extensions.AxSpaceIdConfigurationKey];
        var protocolValue = configuration[Extensions.AxProtocolConfigurationKey];

        if (string.IsNullOrWhiteSpace(endpointValue)
            && string.IsNullOrWhiteSpace(apiKey)
            && string.IsNullOrWhiteSpace(spaceId)
            && string.IsNullOrWhiteSpace(protocolValue))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(endpointValue))
            throw MissingAxSetting(Extensions.AxOtlpTracesEndpointConfigurationKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw MissingAxSetting(Extensions.AxApiKeyConfigurationKey);
        if (string.IsNullOrWhiteSpace(spaceId))
            throw MissingAxSetting(Extensions.AxSpaceIdConfigurationKey);

        var endpoint = GetOptionalHttpEndpoint(configuration, Extensions.AxOtlpTracesEndpointConfigurationKey)
            ?? throw MissingAxSetting(Extensions.AxOtlpTracesEndpointConfigurationKey);
        var protocol = ParseAxProtocol(protocolValue);
        endpoint = NormalizeAxTraceEndpoint(endpoint, protocol);

        return new AxExporterConfiguration(
            endpoint,
            protocol,
            $"arize-space-id={spaceId},arize-api-key={apiKey}");
    }

    private static Uri NormalizeAxTraceEndpoint(Uri endpoint, OtlpExportProtocol protocol)
    {
        if (protocol != OtlpExportProtocol.HttpProtobuf
            || !endpoint.AbsolutePath.TrimEnd('/').Equals("/v1", StringComparison.Ordinal))
        {
            return endpoint;
        }

        return new UriBuilder(endpoint) { Path = "/v1/traces" }.Uri;
    }

    private static OtlpExportProtocol ParseAxProtocol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        if (value.Equals("grpc", StringComparison.OrdinalIgnoreCase))
            return OtlpExportProtocol.Grpc;

        throw new InvalidOperationException(
            $"Configuration '{Extensions.AxProtocolConfigurationKey}' must be 'http/protobuf' or 'grpc'.");
    }

    private static InvalidOperationException MissingAxSetting(string key) =>
        new($"Arize AX trace export requires configuration '{key}'.");
}

internal sealed record AxExporterConfiguration(
    Uri Endpoint,
    OtlpExportProtocol Protocol,
    string Headers);
