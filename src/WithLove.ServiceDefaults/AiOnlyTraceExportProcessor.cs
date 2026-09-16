using System.Diagnostics;
using OpenTelemetry;
using WithLove.OpenInference;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Removes non-AI Activities before the active Arize exporter runs while retaining the connected
/// chat trajectory needed by OpenInference viewers.
/// </summary>
internal sealed class AiOnlyTraceExportProcessor : BaseProcessor<Activity>
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

        if (!IsAiTrajectoryActivity(activity))
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
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
