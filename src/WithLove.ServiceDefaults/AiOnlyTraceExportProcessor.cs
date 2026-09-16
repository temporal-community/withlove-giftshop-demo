using System.Diagnostics;
using OpenTelemetry;
using WithLove.OpenInference;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Exports only application-classified AI activities without changing trace sampling decisions.
/// </summary>
internal sealed class AiOnlyTraceExportProcessor(BaseExporter<Activity> exporter)
    : BatchActivityExportProcessor(exporter)
{
    private const string GenAiOperationName = "gen_ai.operation.name";
    private const string ChatOperation = "chat";
    private const string ChatOperationId = "chat.operation_id";
    private const string ChainSpanKind = "CHAIN";
    private const string ToolSpanKind = "TOOL";
    private const string RetrieverSpanKind = "RETRIEVER";
    private const string DurableTurnOperation = "durable.turn";

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        // Do not clear Recorded on rejected activities. Temporal propagates that sampling flag
        // across durable boundaries, and later model/tool activities would never be recorded.
        if (IsAiTrajectoryActivity(activity))
            base.OnEnd(activity);
    }

    internal static bool IsAiTrajectoryActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var spanKind = activity.GetTagItem(OpenInferenceAttributes.OpenInferenceSpanKind) as string;
        if (spanKind == ToolSpanKind || spanKind == RetrieverSpanKind)
            return true;

        if (spanKind == ChainSpanKind
            && activity.GetTagItem(ChatOperationId) is string { Length: > 0 })
        {
            return true;
        }

        return activity.OperationName == DurableTurnOperation
            || activity.GetTagItem(GenAiOperationName) is ChatOperation;
    }
}
