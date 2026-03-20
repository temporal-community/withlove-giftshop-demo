using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WithLove.Web.Services;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "shopSite";
    internal const string ActivitySourceVersion = "1.0.0";

    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public Counter<long> CartOperations { get; }

    public Instrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, ActivitySourceVersion);
        Meter = new Meter(ActivitySourceName, ActivitySourceVersion);

        CartOperations = Meter.CreateCounter<long>(
            "cart.operations",
            description: "Number of cart operations, tagged by operation type");
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
