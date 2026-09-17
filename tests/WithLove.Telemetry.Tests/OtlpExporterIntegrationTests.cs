using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using WithLove.OpenInference;
using WithLove.OpenInference.Spans;
using WithLove.Web.Telemetry;

namespace WithLove.Telemetry.Tests;

public class OtlpExporterIntegrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportedTrace_UsesExclusiveDestinationAndContainsNoRawIdentity(bool usePhoenix)
    {
        await using var aspire = await OtlpTestServer.StartAsync();
        await using var phoenix = await OtlpTestServer.StartAsync();
        var sourceName = $"WithLove.ExportPrivacy.{Guid.NewGuid():N}";
        const string rawUser = "raw-user-export-sentinel";
        const string rawWorkflow = "giftshop-chat-raw-user-export-sentinel";
        var identity = TelemetryIdentity.Create(Convert.ToBase64String(new byte[32]), "test-v1");
        var safeSession = identity.ForSession(rawWorkflow);
        var safeUser = identity.ForUser(rawUser);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = sourceName });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = aspire.BaseUri.AbsoluteUri,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["Phoenix:OtlpTracesEndpoint"] = usePhoenix
                ? new Uri(phoenix.BaseUri, "v1/traces").AbsoluteUri
                : null,
            ["OpenInference:ProjectName"] = "withlove-giftshop",
        });
        builder.ConfigureOpenTelemetry();
        builder.AddOpenInferenceDefaults();
        using var host = builder.Build();
        await host.StartAsync();

        using (var source = new ActivitySource(sourceName))
        using (OpenInferenceContextScope.Push(new() { SessionId = safeSession, UserId = safeUser }))
        using (var chain = source.StartChain("chat.turn"))
        {
            chain.Activity!.SetTag("chat.operation_id", "operation-123");
        }

        host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000).Should().BeTrue();
        var destination = usePhoenix ? phoenix : aspire;
        var request = await destination.WaitForAsync("/v1/traces");
        var wireText = Encoding.UTF8.GetString(request.Body);
        wireText.Should().Contain("withlove-giftshop");
        wireText.Should().Contain(safeSession);
        wireText.Should().Contain(safeUser);
        wireText.Should().NotContain(rawUser);
        wireText.Should().NotContain(rawWorkflow);
        wireText.Should().NotContain("temporalWorkflowID");

        if (usePhoenix)
            aspire.Requests.Should().NotContain(request => request.Path == "/v1/traces");
    }

    [Fact]
    public async Task ExportedTrace_WithAx_UsesTracePathAndAuthenticationWithoutCredentialLeakageToAspire()
    {
        await using var aspire = await OtlpTestServer.StartAsync();
        await using var ax = await OtlpTestServer.StartAsync();
        var sourceName = $"WithLove.AxExport.{Guid.NewGuid():N}";
        const string apiKey = "ax-api-key-sentinel";
        const string spaceId = "ax-space-id-sentinel";
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ApplicationName = sourceName });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = aspire.BaseUri.AbsoluteUri,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["Arize:Tracing:Ax:Endpoint"] = new Uri(ax.BaseUri, "v1").AbsoluteUri,
            ["Arize:Tracing:Ax:Protocol"] = "http/protobuf",
            ["Arize:Tracing:Ax:ApiKey"] = apiKey,
            ["Arize:Tracing:Ax:SpaceId"] = spaceId,
            ["OpenInference:ProjectName"] = "withlove-giftshop",
        });
        builder.ConfigureOpenTelemetry();
        builder.AddOpenInferenceDefaults();
        using var host = builder.Build();
        await host.StartAsync();

        using (var source = new ActivitySource(sourceName))
        using (source.StartChain("chat.turn"))
        {
        }

        host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000).Should().BeTrue();
        var request = await ax.WaitForAsync("/v1/traces");

        request.Headers["arize-api-key"].Should().Be(apiKey);
        request.Headers["arize-space-id"].Should().Be(spaceId);
        aspire.Requests.Should().NotContain(captured => captured.Path == "/v1/traces");

        host.Services.GetRequiredService<MeterProvider>().ForceFlush(5_000).Should().BeTrue();
        host.Services.GetRequiredService<ILogger<OtlpExporterIntegrationTests>>()
            .LogInformation("OTLP log routing sentinel");
        await host.StopAsync();
        await aspire.WaitForAsync("/v1/metrics");
        var logRequest = await aspire.WaitForAsync("/v1/logs");
        Encoding.UTF8.GetString(logRequest.Body).Should().Contain("OTLP log routing sentinel");

        ax.Requests.Should().NotContain(captured =>
            captured.Path == "/v1/metrics" || captured.Path == "/v1/logs");
        aspire.Requests.Should().OnlyContain(captured =>
            !captured.Headers.ContainsKey("arize-api-key")
            && !captured.Headers.ContainsKey("arize-space-id"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportedTrace_ContainsAllCollectedSpansForEachArizeDestination(bool usePhoenix)
    {
        await using var aspire = await OtlpTestServer.StartAsync();
        await using var arize = await OtlpTestServer.StartAsync();
        var applicationName = $"WithLove.FullTraceService.{Guid.NewGuid():N}";
        var sourceName = $"WithLove.FullTraceSource.{Guid.NewGuid():N}";
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = applicationName,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = aspire.BaseUri.AbsoluteUri,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["Phoenix:OtlpTracesEndpoint"] = usePhoenix
                ? new Uri(arize.BaseUri, "v1/traces").AbsoluteUri
                : null,
            ["Arize:Tracing:Ax:Endpoint"] = usePhoenix
                ? null
                : new Uri(arize.BaseUri, "v1").AbsoluteUri,
            ["Arize:Tracing:Ax:Protocol"] = usePhoenix ? null : "http/protobuf",
            ["Arize:Tracing:Ax:ApiKey"] = usePhoenix ? null : "ax-api-key-sentinel",
            ["Arize:Tracing:Ax:SpaceId"] = usePhoenix ? null : "ax-space-id-sentinel",
            ["OpenInference:ProjectName"] = "withlove-giftshop",
        });
        builder.ConfigureOpenTelemetry(configureTracing: tracing => tracing.AddSource(sourceName));
        builder.AddOpenInferenceDefaults();
        using var host = builder.Build();
        await host.StartAsync();

        using var source = new ActivitySource(sourceName);
        using (var chain = source.StartActivity("chat.turn"))
        {
            chain!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "CHAIN");
            chain.SetTag("chat.operation_id", "operation-123");
            using (source.StartActivity("durable.turn"))
            {
                using (var model = source.StartActivity("openai.chat"))
                    model!.SetTag("gen_ai.operation.name", "chat");
                using (var tool = source.StartActivity("execute_tool search_products"))
                {
                    tool!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "TOOL");
                    using (var retriever = source.StartActivity("product.search"))
                        retriever!.SetTag(OpenInferenceAttributes.OpenInferenceSpanKind, "RETRIEVER");
                }
            }
        }
        using (source.StartActivity("HTTP GET /collections")) { }
        using (var embeddings = source.StartActivity("openai.embeddings"))
            embeddings!.SetTag("gen_ai.operation.name", "embeddings");

        host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000).Should().BeTrue();
        var request = await arize.WaitForAsync("/v1/traces");
        var wireText = Encoding.UTF8.GetString(request.Body);
        wireText.Should().Contain(applicationName);
        wireText.Should().Contain("withlove-giftshop");
        wireText.Should().Contain("chat.turn");
        wireText.Should().Contain("durable.turn");
        wireText.Should().Contain("openai.chat");
        wireText.Should().Contain("execute_tool search_products");
        wireText.Should().Contain("product.search");
        wireText.Should().Contain("HTTP GET /collections");
        wireText.Should().Contain("openai.embeddings");
        aspire.Requests.Should().NotContain(captured => captured.Path == "/v1/traces");
    }
}
