using Aspire.Hosting.Dotnet;
using Azure.Provisioning.AppContainers;

namespace WithLove.AppHost.Extensions;

internal static partial class WithLoveApplicationExtensions
{
#pragma warning disable AZPROVISION001
    private static void ConfigureWorkflowServer(IResourceBuilder<DotnetProjectResource> workflowServer)
    {
        workflowServer.PublishAsAzureContainerApp((_, app) =>
        {
            // TCP-only probes — /health is dev-only in ServiceDefaults
            // (MapHealthCheckEndpoints is guarded by IsDevelopment()).
            var container = app.Template.Containers[0].Value!;
            container.Probes =
            [
                new ContainerAppProbe
                {
                    ProbeType = ContainerAppProbeType.Liveness,
                    TcpSocket = new ContainerAppTcpSocketRequestInfo { Port = 8080 },
                    InitialDelaySeconds = 5,
                    PeriodSeconds = 10,
                    FailureThreshold = 3,
                },
                new ContainerAppProbe
                {
                    ProbeType = ContainerAppProbeType.Readiness,
                    TcpSocket = new ContainerAppTcpSocketRequestInfo { Port = 8080 },
                    PeriodSeconds = 5,
                    FailureThreshold = 3,
                },
            ];

            // CPU-based KEDA scaling — MinReplicas=1 (never scale to zero; worker must poll Temporal).
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 5;
            app.Template.Scale.Rules.Add(new ContainerAppScaleRule
            {
                Name = "cpu-rule",
                Custom = new ContainerAppCustomScaleRule
                {
                    CustomScaleRuleType = "cpu",
                    Metadata =
                    {
                        ["type"] = "Utilization",
                        ["value"] = "70",
                    },
                },
            });
        });
    }

    private static void ConfigureShopSite(IResourceBuilder<DotnetProjectResource> shopSite)
    {
        shopSite.PublishAsAzureContainerApp((_, app) =>
        {
            // Sticky sessions for Blazor InteractiveServer / SignalR.
            app.Configuration.Ingress.StickySessionsAffinity = StickySessionAffinity.Sticky;

            // TCP readiness probe — MapHealthCheckEndpoints is gated by IsDevelopment() so
            // HTTP health endpoints don't exist in production; TCP is the correct fallback.
            var container = app.Template.Containers[0].Value!;
            container.Probes =
            [
                new ContainerAppProbe
                {
                    ProbeType = ContainerAppProbeType.Readiness,
                    TcpSocket = new ContainerAppTcpSocketRequestInfo { Port = 8080 },
                    InitialDelaySeconds = 10,
                    PeriodSeconds = 5,
                    FailureThreshold = 3,
                },
            ];

            // HTTP-based KEDA scaling — MinReplicas=1, MaxReplicas=10, 100 concurrent requests per replica.
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 10;
            app.Template.Scale.Rules.Add(new ContainerAppScaleRule
            {
                Name = "http-rule",
                Http = new ContainerAppHttpScaleRule
                {
                    Metadata =
                    {
                        ["concurrentRequests"] = "100",
                    },
                },
            });
        });
    }
#pragma warning restore AZPROVISION001
}
