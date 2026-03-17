using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;
using Temporalio.Testing;

namespace WithLove.AppHost.Resources;

public class TemporalContainerResource(string name) : ContainerResource(name), IResourceWithConnectionString, IResourceWithServiceDiscovery
{
    private EndpointReference? _primaryEndpoint;
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, TemporalResourceConstants.ServiceEndpointName);
    
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"{PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}");

    public TemporalContainerOptions Options { get; set; } = new();
}


public static class TemporalContainerBuilderExtensions
{
    public static IResourceBuilder<TemporalContainerResource> AddTemporalDevContainer(
        this IDistributedApplicationBuilder builder,
        string name = "temporal-container",
        Action<TemporalContainerOptions>? configure = null)
    {
        var resource = new TemporalContainerResource(name);
        configure?.Invoke(resource.Options);
        
        string? endpointAddress = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(resource, async (@event, _) =>
        {
            if (@event.Resource.TryGetEndpoints(out var endpoints))
            {
                var serviceEndpoint = endpoints.Single(e => e.Name == TemporalResourceConstants.ServiceEndpointName);
                endpointAddress = $"{serviceEndpoint.TargetHost}:{serviceEndpoint.Port}";
            }

            await Task.CompletedTask;
        });
        
        var healthCheckKey = $"{name}_check";
        builder.Services.AddHealthChecks()
            .AddTemporalHealthCheck(_ => new TemporalClientConnectOptions
                {
                    Namespace = resource.Options.Namespace,
                    TargetHost = endpointAddress 
                }, healthCheckKey );
        
        return builder.AddResource(resource)
            .WithImage(TemporalResourceConstants.TemporalImage,
                resource.Options.ImageTag ?? TemporalResourceConstants.DefaultTag)
            .WithImageRegistry("docker.io")
            .WithArgs(BuildContainerArgs(resource.Options))
            .ExcludeFromManifest()
            .WithEndpoint(
                targetPort: TemporalResourceConstants.DefaultServiceEndpointPort,
                port: resource.Options.Port,
                name: TemporalResourceConstants.ServiceEndpointName)
            .WithHttpEndpoint(
                targetPort: TemporalResourceConstants.DefaultUIEndpointPort,
                port: resource.Options.UIPort,
                name: TemporalResourceConstants.UIEndpointName)
            .WithHttpEndpoint(
                targetPort: resource.Options.MetricsPort,
                port: resource.Options.MetricsPort,
                name: TemporalResourceConstants.MetricsEndpointName)
            .WithHealthCheck(healthCheckKey)
            .WithUrlForEndpoint(TemporalResourceConstants.UIEndpointName, url =>
            {
                url.DisplayText = "Dashboard";
            });
    }

    private static string[] BuildContainerArgs(TemporalResourceOptions options)
    {
        var args = new List<string> { "server", "start-dev" };
        
        args.AddRange(["--ip", "0.0.0.0"]);
        args.AddRange(["--port", $"{TemporalResourceConstants.DefaultServiceEndpointPort}"]);

        if (options.IsHeadless)
            args.Add("--headless");

        args.AddRange(["--log-level", options.DevServerOptions.LogLevel]);

        args.AddRange(["--log-format", options.DevServerOptions.LogFormat]);
        
            args.AddRange(["--namespace", options.Namespace]);

        // Add search attributes from inherited property
        if (options.SearchAttributes != null)
        {
            foreach (var sa in options.SearchAttributes)
            {
                args.AddRange(["--search-attribute", $"{sa.Name}={sa.ValueType}"]);
            }
        }

        if (!string.IsNullOrEmpty(options.ApiKey))
            args.AddRange(["--api-key", options.ApiKey]);

        return args.ToArray();
    }
    
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<TemporalContainerResource> source)
        where TDestination : IResourceWithEnvironment
    {
        return builder
            .WithReference(source as IResourceBuilder<IResourceWithServiceDiscovery>)
            .WithEnvironment(ctx =>
            {
                ctx.EnvironmentVariables["TEMPORAL_ADDRESS"] =
                    source.Resource.PrimaryEndpoint.Property(EndpointProperty.HostAndPort);

                ctx.EnvironmentVariables["TEMPORAL_NAMESPACE"] = source.Resource.Options.Namespace;
                
                ctx.EnvironmentVariables["TEMPORAL_UI_ADDRESS"] =
                    source.GetEndpoint(TemporalResourceConstants.UIEndpointName);
            });
    }
}


public class TemporalContainerOptions: TemporalResourceOptions
{
    public string? ImageTag { get; set; } = TemporalResourceConstants.DefaultTag;
}


public class TemporalResourceOptions : WorkflowEnvironmentStartLocalOptions
{

    public TemporalResourceOptions()
    {
        // Set defaults that differ from base class
        UIPort = TemporalResourceConstants.DefaultUIEndpointPort;
        UI = true;
        TargetHost = $"0.0.0.0:{TemporalResourceConstants.DefaultServiceEndpointPort}";
    }
    
    /// <summary>
    /// Gets the main service port for Temporal, parsed from TargetHost.
    /// Maps to --port CLI argument and DevServerOptions.Port concept.
    /// </summary>
    public int Port
    {
        get
        {
            if (string.IsNullOrEmpty(TargetHost))
                throw new InvalidOperationException("TargetHost must be set before accessing Port.");
            
            var parts = TargetHost.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                return port;
            
            throw new FormatException($"TargetHost '{TargetHost}' is not in the expected 'ip:port' format.");
        }
    }
    
    /// <summary>
    /// Gets the IP address to bind to, parsed from TargetHost.
    /// Maps to --ip CLI argument and DevServerOptions.Ip concept.
    /// </summary>
    public string Ip
    {
        get
        {
            if (string.IsNullOrEmpty(TargetHost))
                throw new InvalidOperationException("TargetHost must be set before accessing Ip.");
            
            var parts = TargetHost.Split(':');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                return parts[0];

            return "0.0.0.0";
        }
    }
    
    /// <summary>
    /// Gets or sets the metrics endpoint port.
    /// Specific to Aspire hosting setup.
    /// </summary>
    public int MetricsPort { get; set; } = TemporalResourceConstants.DefaultMetricsEndpointPort;
    
    /// <summary>
    /// Gets or sets whether to run in headless mode (no UI).
    /// This is the inverse of the inherited UI property.
    /// </summary>
    public bool IsHeadless => !UI;
}