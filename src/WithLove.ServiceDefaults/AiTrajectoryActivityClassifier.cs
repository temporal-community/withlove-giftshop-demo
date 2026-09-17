using System.Diagnostics;
using WithLove.OpenInference;

namespace Microsoft.Extensions.Hosting;

/// <summary>Identifies activities retained by the AI-only trace exporter.</summary>
internal static class AiTrajectoryActivityClassifier
{
    private const string GenAiOperationName = "gen_ai.operation.name";
    private const string ChatOperation = "chat";
    private const string ChatOperationId = "chat.operation_id";
    private const string ChainSpanKind = "CHAIN";
    private const string ToolSpanKind = "TOOL";
    private const string RetrieverSpanKind = "RETRIEVER";
    private const string DurableTurnOperation = "durable.turn";

    internal static bool IsRetainedAiActivity(Activity activity)
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
