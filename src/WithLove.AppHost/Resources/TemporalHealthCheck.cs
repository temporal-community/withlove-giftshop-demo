using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;

namespace WithLove.AppHost.Resources;

public class TemporalHealthCheck(TemporalClientConnectOptions clientConnectOptions): IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        try
        {
            await TemporalClient.ConnectAsync(clientConnectOptions);
            return HealthCheckResult.Healthy(); 
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Unable to connect to Temporal server", e);
        }
    }
}


public static class TemporalHealthCheckBuilderExtensions
{
    public static IHealthChecksBuilder AddTemporalHealthCheck(this IHealthChecksBuilder builder,
        Func<IServiceProvider, TemporalClientConnectOptions> clientConnectOptionsFactory, string name = "temporal", 
        IEnumerable<string>? tags = default, TimeSpan? timeout = default)
    {

        return builder.Add(new HealthCheckRegistration(
            name, sp => new TemporalHealthCheck(clientConnectOptionsFactory(sp)),
            HealthStatus.Unhealthy,
            tags, timeout));
    }
}