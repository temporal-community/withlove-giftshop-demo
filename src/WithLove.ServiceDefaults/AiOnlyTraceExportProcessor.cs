using System.Diagnostics;
using OpenTelemetry;
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Exports only application-classified AI activities without changing trace sampling decisions.
/// </summary>
internal sealed class AiOnlyTraceExportProcessor(BaseExporter<Activity> exporter)
    : BatchActivityExportProcessor(exporter)
{
    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        // Do not clear Recorded on rejected activities. Temporal propagates that sampling flag
        // across durable boundaries, and later model/tool activities would never be recorded.
        if (AiTrajectoryActivityClassifier.IsRetainedAiActivity(activity))
            base.OnEnd(activity);
    }

}
