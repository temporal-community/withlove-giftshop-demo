using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WithLove.Workflows.Telemetry;

internal static class ChatTelemetry
{
    // Must match the Aspire resource name — runs inside "workflowServer"
    private const string SourceName = "workflowServer";
    private const string Version = "1.0.0";

    // Static readonly singletons — creation is expensive, must be reused
    internal static readonly ActivitySource ActivitySource = new(SourceName, Version);
    internal static readonly Meter Meter = new(SourceName, Version);

    internal static readonly Histogram<double> InferenceDuration =
        Meter.CreateHistogram<double>("chat.inference.duration_ms", "ms",
            "End-to-end AI inference latency including all tool calls");

    internal static readonly Counter<long> CartMutations =
        Meter.CreateCounter<long>("chat.cart_mutations", "mutations",
            "Cart mutations triggered by the chat agent");
}
