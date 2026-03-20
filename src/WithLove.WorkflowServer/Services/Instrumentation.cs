using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WithLove.WorkflowServer.Services;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "workflowServer";
    internal const string ActivitySourceVersion = "1.0.0";

    public ActivitySource ActivitySource { get; } = new(ActivitySourceName, ActivitySourceVersion);

    public Meter Meter { get; } = new(ActivitySourceName, ActivitySourceVersion);

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
