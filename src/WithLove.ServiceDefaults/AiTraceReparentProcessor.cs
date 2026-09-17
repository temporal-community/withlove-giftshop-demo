using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Reparents retained AI activities to the distributed chat-turn root when their direct parent is
/// infrastructure excluded by the AI-only exporter.
/// </summary>
/// <remarks>
/// <see cref="Activity.ParentSpanId"/> has no public setter. The supported runtime currently stores
/// that value in <c>_parentSpanId</c>; this compatibility path is isolated here and fails closed when
/// the runtime no longer has that exact implementation detail.
/// </remarks>
internal sealed class AiTraceReparentProcessor : BaseProcessor<Activity>
{
    private static int baggagePropagationConfigured;

    private static readonly FieldInfo? ParentSpanIdField = typeof(Activity).GetField(
        "_parentSpanId",
        BindingFlags.Instance | BindingFlags.NonPublic);

    internal AiTraceReparentProcessor() => EnsureBaggagePropagation();

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (!AiTrajectoryActivityClassifier.IsRetainedAiActivity(activity)
            || !WithLove.ServiceDefaults.Telemetry.AiTraceAnchorScope.TryGetCurrent(out var anchor)
            || !string.Equals(activity.TraceId.ToHexString(), anchor.TraceId, StringComparison.Ordinal)
            || string.Equals(activity.SpanId.ToHexString(), anchor.SpanId, StringComparison.Ordinal)
            || string.Equals(activity.ParentSpanId.ToHexString(), anchor.SpanId, StringComparison.Ordinal)
            || HasRetainedAiParent(activity))
        {
            return;
        }

        TrySetParentSpanId(activity, anchor.SpanId);
    }

    private static bool HasRetainedAiParent(Activity activity) =>
        activity.Parent is { } parent && AiTrajectoryActivityClassifier.IsRetainedAiActivity(parent);

    private static void EnsureBaggagePropagation()
    {
        if (Interlocked.Exchange(ref baggagePropagationConfigured, 1) != 0)
            return;

        // HttpClient owns trace-context injection on modern .NET. Its OpenTelemetry listener adds
        // custom propagation only when the default is not TraceContextPropagator, so explicitly
        // include W3C baggage to carry the opaque chat-turn anchor across HTTP service boundaries.
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
        [
            new TraceContextPropagator(),
            new BaggagePropagator(),
        ]));
    }

    private static void TrySetParentSpanId(Activity activity, string parentSpanId)
    {
        if (ParentSpanIdField?.FieldType != typeof(string))
            return;

        try
        {
            ParentSpanIdField.SetValue(activity, parentSpanId);
        }
        catch (Exception)
        {
            // The AI-only exporter can safely retain the original topology if a future runtime
            // changes this private field or disallows the mutation.
        }
    }
}
