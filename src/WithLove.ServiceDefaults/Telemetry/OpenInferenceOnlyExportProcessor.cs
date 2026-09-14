using System.Diagnostics;
using OpenTelemetry;
using WithLove.OpenInference;

namespace WithLove.ServiceDefaults.Telemetry;

/// <summary>
/// Restricts the Arize AX trace export to OpenInference-classified spans.
/// </summary>
/// <remarks>
/// Arize's LLM-observability views categorize spans by <c>openinference.span.kind</c> (CHAIN, LLM,
/// TOOL, RETRIEVER, EMBEDDING). Auto-instrumented infrastructure spans — ASP.NET Core, HttpClient,
/// EF Core and the Temporal SDK — carry no such kind and surface as "UNKNOWN", burying the handful
/// of agent spans. This processor drops any span without an <c>openinference.span.kind</c> from
/// export by clearing its Recorded flag, exactly as <see cref="ChatHydrationExportProcessor"/> does
/// in Web. It must be registered before the export processor.
///
/// It is wired only on the Arize AX destination (see <c>ConfigureOpenTelemetry</c>), so the Aspire
/// dashboard and Phoenix trace paths keep full-fidelity infrastructure traces for local debugging.
/// </remarks>
internal sealed class OpenInferenceOnlyExportProcessor : BaseProcessor<Activity>
{
    private const string GenAiAttributePrefix = "gen_ai.";

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!ShouldExport(activity))
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
    }

    private static bool ShouldExport(Activity activity)
    {
        // App-owned spans carry an explicit OpenInference kind (CHAIN, TOOL, RETRIEVER, …).
        if (activity.GetTagItem(OpenInferenceAttributes.OpenInferenceSpanKind) is not null)
            return true;

        // Model and embedding calls are emitted with OTel GenAI semantic conventions (gen_ai.*)
        // rather than an OpenInference kind; Arize classifies them as LLM/EMBEDDING at ingest.
        // Keep any span carrying GenAI attributes so those are not dropped as infrastructure.
        foreach (var tag in activity.EnumerateTagObjects())
        {
            if (tag.Key.StartsWith(GenAiAttributePrefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
