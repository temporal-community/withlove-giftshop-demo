using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using WithLove.AppHost.Extensions;

namespace WithLove.Telemetry.Tests;

public class AppHostTelemetryModelTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("Phoenix", true, false)]
    [InlineData("Ax", false, true)]
    [InlineData("ax", false, true)]
    public void TraceDestination_DefaultsByExecutionModeAndHonorsExplicitSelection(
        string? configuredDestination,
        bool isPublishMode,
        bool expectedUseAx) =>
        WithLoveApplicationExtensions.ResolveUseAxTraceDestination(configuredDestination, isPublishMode)
            .Should().Be(expectedUseAx);

    [Fact]
    public void TraceDestination_RejectsUnknownSelection()
    {
        var action = () => WithLoveApplicationExtensions.ResolveUseAxTraceDestination("both", false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Arize:TraceDestination*Ax*Phoenix*");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void AiContentCapture_DefaultsOffAndAcceptsOnlyBooleanValues(
        string? configuredValue,
        bool expected) =>
        WithLoveApplicationExtensions.ResolveCaptureAiContent(configuredValue)
            .Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData(" true ")]
    public void AiContentCapture_RejectsMalformedValues(string configuredValue)
    {
        var action = () => WithLoveApplicationExtensions.ResolveCaptureAiContent(configuredValue);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Telemetry:CaptureAiContent*true*false*");
    }

    [Fact]
    public async Task LocalFullApp_WiresPhoenixWithoutRequiringAnIdentityKeyParameter()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.WithLove_AppHost>();
        await using var app = await builder.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var arize = model.Resources.OfType<PhoenixResource>().Should().ContainSingle(resource => resource.Name == "arize").Subject;
        arize.Annotations.OfType<ContainerMountAnnotation>().Should().BeEmpty();

        foreach (var serviceName in new[] { "productsApi", "workflowServer", "shopSite" })
        {
            var service = model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == serviceName);
            var environment = await ResolveEnvironmentAsync(service, builder.ExecutionContext);
            environment["Phoenix__OtlpTracesEndpoint"].Should().BeOfType<ReferenceExpression>()
                .Which.ValueExpression.Should().Be("{arize.bindings.http.url}/v1/traces");
            environment["OpenInference__ProjectName"].Should().Be("withlove-giftshop");
            service.Annotations.OfType<ResourceRelationshipAnnotation>().Should().Contain(relationship =>
                ReferenceEquals(relationship.Resource, arize)
                && relationship.Type == "Reference");
            service.Annotations.OfType<ResourceRelationshipAnnotation>().Should().Contain(relationship =>
                ReferenceEquals(relationship.Resource, arize)
                && relationship.Type.Contains("Wait", StringComparison.OrdinalIgnoreCase));
        }

        var products = await ResolveEnvironmentAsync(
            model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "productsApi"),
            builder.ExecutionContext);
        var worker = await ResolveEnvironmentAsync(
            model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "workflowServer"),
            builder.ExecutionContext);
        var web = await ResolveEnvironmentAsync(
            model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "shopSite"),
            builder.ExecutionContext);

        products.Should().NotContainKey("TelemetryIdentity__Key");
        worker.Should().NotContainKey("TelemetryIdentity__Key");
        web.Should().NotContainKey("TelemetryIdentity__Key");
        web.Should().NotContainKey("TelemetryIdentity__KeyVersion");
        model.Resources.OfType<ParameterResource>().Should().NotContain(resource =>
            resource.Name == "telemetry-identity-key");
        AssertCaptureEnvironment(products, expectedCapture: false);
        AssertCaptureEnvironment(worker, expectedCapture: false);
        AssertCaptureEnvironment(web, expectedCapture: false);
    }

    [Fact]
    public async Task LocalFullApp_WithAxSelection_WiresEveryServiceToAxWithoutPhoenixOrReadinessWait()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.WithLove_AppHost>(
            args: ["--Arize:TraceDestination=Ax"]);
        await using var app = await builder.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var ax = model.Resources.OfType<ArizeAxResource>().Should().ContainSingle(resource => resource.Name == "arize-ax").Subject;

        model.Resources.Should().NotContain(resource => resource is PhoenixResource);
        foreach (var serviceName in new[] { "productsApi", "workflowServer", "shopSite" })
        {
            var service = model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == serviceName);
            var environment = await ResolveEnvironmentAsync(service, builder.ExecutionContext);
            AssertParameterExpression(environment, "Arize__Tracing__Ax__Endpoint", "arize-ax-otlp-endpoint");
            AssertParameterExpression(environment, "Arize__Tracing__Ax__ApiKey", "arize-ax-api-key");
            AssertParameterExpression(environment, "Arize__Tracing__Ax__SpaceId", "arize-ax-space-id");
            environment["Arize__Tracing__Ax__Protocol"].Should().Be("http/protobuf");
            environment.Should().NotContainKey("Phoenix__OtlpTracesEndpoint");
            service.Annotations.OfType<ResourceRelationshipAnnotation>().Should().ContainSingle(relationship =>
                ReferenceEquals(relationship.Resource, ax)
                && relationship.Type == "Reference");
            service.Annotations.OfType<ResourceRelationshipAnnotation>().Should().NotContain(relationship =>
                ReferenceEquals(relationship.Resource, ax)
                && relationship.Type.Contains("Wait", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task PublishAppHost_DefaultsEveryServiceToAxWithoutPhoenixOrIdentityKeyParameter()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.WithLove_AppHost>(
            args: ["--publisher", "manifest"]);
        await using var app = await builder.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        model.Resources.OfType<ArizeAxResource>().Should().ContainSingle(resource => resource.Name == "arize-ax");
        model.Resources.Should().NotContain(resource => resource is PhoenixResource);
        model.Resources.OfType<ParameterResource>().Should().NotContain(resource =>
            resource.Name == "telemetry-identity-key");
        foreach (var serviceName in new[] { "productsApi", "workflowServer", "shopSite" })
        {
            var service = model.Resources.OfType<ProjectResource>().Single(resource => resource.Name == serviceName);
            var environment = await ResolveEnvironmentAsync(service, builder.ExecutionContext);
            AssertParameterExpression(environment, "Arize__Tracing__Ax__Endpoint", "arize-ax-otlp-endpoint");
            environment.Should().NotContainKey("Phoenix__OtlpTracesEndpoint");
            environment.Should().NotContainKey("TelemetryIdentity__Key");
            environment.Should().NotContainKey("TelemetryIdentity__KeyVersion");
            AssertCaptureEnvironment(
                environment,
                expectedCapture: false);
        }
    }

    [Fact]
    public async Task ProductsOnlyTestTopology_ContainsNeitherPhoenixNorUnusedIdentitySecret()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.WithLove_AppHost>(
            args: ["TESTING=true"]);
        await using var app = await builder.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        model.Resources.Should().NotContain(resource => resource is PhoenixResource);
        model.Resources.OfType<ParameterResource>().Should().NotContain(resource =>
            resource.Name == "telemetry-identity-key");
    }

    [Fact]
    public async Task AiContentCaptureOptIn_EnablesOnlyWebAndWorkflowServer()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.WithLove_AppHost>(
            args: ["Telemetry:CaptureAiContent=true"]);
        await using var app = await builder.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        foreach (var service in model.Resources.OfType<ProjectResource>())
        {
            var environment = await ResolveEnvironmentAsync(service, builder.ExecutionContext);
            AssertCaptureEnvironment(environment, service.Name != "productsApi");
        }
    }

    private static async Task<Dictionary<string, object>> ResolveEnvironmentAsync(
        IResource resource,
        DistributedApplicationExecutionContext executionContext)
    {
        resource.TryGetEnvironmentVariables(out var callbacks).Should().BeTrue();
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(executionContext, resource, environment);
        foreach (var callback in callbacks!) await callback.Callback(context);
        return environment;
    }

    private static void AssertParameterExpression(
        IReadOnlyDictionary<string, object> environment,
        string key,
        string parameterName) =>
        environment[key].Should().BeAssignableTo<IManifestExpressionProvider>()
            .Which.ValueExpression.Should().Be($"{{{parameterName}.value}}");

    private static void AssertCaptureEnvironment(
        IReadOnlyDictionary<string, object> environment,
        bool? expectedCapture)
    {
        const string applicationSetting = "Telemetry__CaptureAiContent";
        const string standardSetting = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
        if (expectedCapture is null)
        {
            environment.Should().NotContainKey(applicationSetting);
            environment.Should().NotContainKey(standardSetting);
            return;
        }

        var expected = expectedCapture.Value ? "true" : "false";
        environment[applicationSetting].Should().Be(expected);
        environment.Should().NotContainKey(standardSetting);
    }
}
