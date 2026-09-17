using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Exporter;

namespace WithLove.Telemetry.Tests;

public class ServiceDefaultsRoutingTests
{
    [Fact]
    public void ConfigureAspNetCoreTracing_AppliesShopSiteOverridesAndPreservesSharedFilters()
    {
        var options = new AspNetCoreTraceInstrumentationOptions();

        Extensions.ConfigureAspNetCoreTracing(options, configured =>
        {
            configured.EnableAspNetCoreSignalRSupport = false;
            configured.EnableRazorComponentsSupport = false;
            configured.Filter = context => context.Request.Path != "/blocked";
        });

        options.EnableAspNetCoreSignalRSupport.Should().BeFalse();
        options.EnableRazorComponentsSupport.Should().BeFalse();
        IsAllowed(options, "/orders").Should().BeTrue();
        IsAllowed(options, "/health").Should().BeFalse();
        IsAllowed(options, "/alive").Should().BeFalse();
        IsAllowed(options, "/blocked").Should().BeFalse();
        IsAllowed(options, "/_blazor/initializers/").Should().BeFalse();
        IsAllowed(options, "/_blazor/negotiate", "POST").Should().BeFalse();
    }

    [Theory]
    [InlineData("GET", "/app.css", false)]
    [InlineData("HEAD", "/scripts/app.js", false)]
    [InlineData("GET", "/Components/Layout/ReconnectModal.HASH.razor.JS", false)]
    [InlineData("GET", "/images/product.webp", false)]
    [InlineData("GET", "/fonts/site.woff2", false)]
    [InlineData("GET", "/_framework/runtime.wasm", false)]
    [InlineData("GET", "/api/products/42", true)]
    [InlineData("GET", "/api/export.json", true)]
    [InlineData("POST", "/scripts/app.js", true)]
    public void ConfigureAspNetCoreTracing_FiltersKnownStaticReadRequests(
        string method,
        string path,
        bool expected)
    {
        var options = new AspNetCoreTraceInstrumentationOptions();
        Extensions.ConfigureAspNetCoreTracing(options, configure: null);

        IsAllowed(options, path, method).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, "None", false, false)]
    [InlineData("http://aspire:4317", null, "Aspire", true, true)]
    [InlineData(null, "http://phoenix:6006/v1/traces", "Phoenix", false, false)]
    [InlineData("http://aspire:4317", "http://phoenix:6006/v1/traces", "Phoenix", true, true)]
    public void Resolve_ImplementsExclusiveTraceAndAspireSignalMatrix(
        string? aspire,
        string? phoenix,
        string expectedTrace,
        bool expectedLogs,
        bool expectedMetrics)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Extensions.AspireOtlpEndpointConfigurationKey] = aspire,
            [Extensions.PhoenixOtlpTracesEndpointConfigurationKey] = phoenix,
        }).Build();

        var routing = TelemetryExportRouting.Resolve(configuration);

        routing.TraceDestination.ToString().Should().Be(expectedTrace);
        routing.ExportLogsToAspire.Should().Be(expectedLogs);
        routing.ExportMetricsToAspire.Should().Be(expectedMetrics);
    }

    [Fact]
    public void Resolve_WithAx_RoutesOnlyTracesToAxAndKeepsAspireSignalsEnabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Extensions.AspireOtlpEndpointConfigurationKey] = "http://aspire:4317",
            [Extensions.AxOtlpTracesEndpointConfigurationKey] = "https://example.test/v1",
            [Extensions.AxProtocolConfigurationKey] = "http/protobuf",
            [Extensions.AxApiKeyConfigurationKey] = "private-api-key",
            [Extensions.AxSpaceIdConfigurationKey] = "private-space-id",
        }).Build();

        var routing = TelemetryExportRouting.Resolve(configuration);

        routing.TraceDestination.Should().Be(TraceExportDestination.Ax);
        routing.ExportLogsToAspire.Should().BeTrue();
        routing.ExportMetricsToAspire.Should().BeTrue();
        routing.Ax.Should().NotBeNull();
        routing.Ax!.Endpoint.Should().Be(new Uri("https://example.test/v1/traces"));
        routing.Ax.Protocol.Should().Be(OtlpExportProtocol.HttpProtobuf);
        routing.Ax.Headers.Should().Be("arize-space-id=private-space-id,arize-api-key=private-api-key");
    }

    [Theory]
    [InlineData("https://otlp.arize.com/v1", "http/protobuf", "https://otlp.arize.com/v1/traces")]
    [InlineData("https://otlp.arize.com/v1/", "http/protobuf", "https://otlp.arize.com/v1/traces")]
    [InlineData("https://otlp.arize.com/v1/traces", "http/protobuf", "https://otlp.arize.com/v1/traces")]
    [InlineData("https://otlp.arize.com/v1", "grpc", "https://otlp.arize.com/v1")]
    public void Resolve_WithAx_NormalizesOnlyTheHttpBaseEndpoint(
        string endpoint,
        string protocol,
        string expectedEndpoint)
    {
        var configuration = AxConfiguration(endpoint, "test-api-key");
        configuration[Extensions.AxProtocolConfigurationKey] = protocol;

        var routing = TelemetryExportRouting.Resolve(configuration);

        routing.Ax!.Endpoint.Should().Be(new Uri(expectedEndpoint));
        routing.Ax.Protocol.Should().Be(
            protocol == "grpc" ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf);
    }

    [Fact]
    public void Resolve_WithPhoenixAndAx_RejectsDuplicateArizeTraceDestinationsWithoutLeakingSecrets()
    {
        const string privateApiKey = "must-not-leak-api-key";
        var configuration = AxConfiguration("https://example.test/v1", privateApiKey);
        configuration[Extensions.PhoenixOtlpTracesEndpointConfigurationKey] = "http://phoenix:6006/v1/traces";

        var action = () => TelemetryExportRouting.Resolve(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only one Arize trace destination*")
            .Which.Message.Should().NotContain(privateApiKey);
    }

    [Theory]
    [InlineData("Arize:Tracing:Ax:Endpoint")]
    [InlineData("Arize:Tracing:Ax:ApiKey")]
    [InlineData("Arize:Tracing:Ax:SpaceId")]
    public void Resolve_WithIncompleteAxConfiguration_NamesMissingSettingWithoutLeakingSecrets(string missingKey)
    {
        const string privateApiKey = "must-not-leak-api-key";
        const string privateSpaceId = "must-not-leak-space-id";
        var configuration = AxConfiguration("https://example.test/v1", privateApiKey, privateSpaceId);
        configuration[missingKey] = null;

        var action = () => TelemetryExportRouting.Resolve(configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(missingKey);
        exception.Message.Should().NotContain(privateApiKey);
        exception.Message.Should().NotContain(privateSpaceId);
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("ftp://example.test/v1")]
    public void Resolve_WithMalformedAxEndpoint_RejectsItWithoutLeakingCredentials(string endpoint)
    {
        const string privateApiKey = "must-not-leak-api-key";
        var configuration = AxConfiguration(endpoint, privateApiKey);

        var action = () => TelemetryExportRouting.Resolve(configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(Extensions.AxOtlpTracesEndpointConfigurationKey);
        exception.Message.Should().Contain("absolute HTTP or HTTPS URI");
        exception.Message.Should().NotContain(privateApiKey);
    }

    [Fact]
    public void Resolve_WithUnknownAxProtocol_RejectsItWithoutLeakingCredentials()
    {
        const string privateApiKey = "must-not-leak-api-key";
        var configuration = AxConfiguration("https://example.test/v1", privateApiKey);
        configuration[Extensions.AxProtocolConfigurationKey] = "http/json";

        var action = () => TelemetryExportRouting.Resolve(configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(Extensions.AxProtocolConfigurationKey);
        exception.Message.Should().Contain("http/protobuf");
        exception.Message.Should().Contain("grpc");
        exception.Message.Should().NotContain(privateApiKey);
    }

    [Theory]
    [InlineData("phoenix:6006")]
    [InlineData("ftp://phoenix/traces")]
    public void Resolve_RejectsMalformedPhoenixEndpoint(string endpoint)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Extensions.PhoenixOtlpTracesEndpointConfigurationKey] = endpoint,
        }).Build();

        var action = () => TelemetryExportRouting.Resolve(configuration);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Phoenix:OtlpTracesEndpoint*absolute HTTP or HTTPS URI*");
    }

    [Fact]
    public void CreateSafeTemporalTracingInterceptor_ReturnsFreshInstancesWithoutWorkflowIdTag()
    {
        var first = Extensions.CreateSafeTemporalTracingInterceptor();
        var second = Extensions.CreateSafeTemporalTracingInterceptor();

        first.Should().NotBeSameAs(second);
        first.Options.TagNameWorkflowId.Should().BeNull();
        second.Options.TagNameWorkflowId.Should().BeNull();
    }

    [Theory]
    [InlineData("explicit", "configured", "otel-service", "explicit")]
    [InlineData(null, "configured", "otel-service", "configured")]
    [InlineData(null, null, "otel-service", "otel-service")]
    [InlineData(null, null, null, "fallback-app")]
    public void ResolveOpenInferenceProjectName_UsesDocumentedPrecedence(
        string? explicitName,
        string? configuredName,
        string? otelServiceName,
        string expected)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "fallback-app",
        });
        builder.Configuration["OpenInference:ProjectName"] = configuredName;
        builder.Configuration["OTEL_SERVICE_NAME"] = otelServiceName;

        Extensions.ResolveOpenInferenceProjectName(builder, explicitName).Should().Be(expected);
    }

    private static bool IsAllowed(
        AspNetCoreTraceInstrumentationOptions options,
        string path,
        string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        return options.Filter!(context);
    }

    private static IConfigurationRoot AxConfiguration(
        string endpoint,
        string apiKey,
        string spaceId = "private-space-id") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Extensions.AxOtlpTracesEndpointConfigurationKey] = endpoint,
            [Extensions.AxProtocolConfigurationKey] = "http/protobuf",
            [Extensions.AxApiKeyConfigurationKey] = apiKey,
            [Extensions.AxSpaceIdConfigurationKey] = spaceId,
        }).Build();
}
