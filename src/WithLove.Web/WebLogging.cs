using Microsoft.Extensions.Logging;

namespace WithLove.Web;

internal static partial class WebLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting order processing workflow for checkout session {SessionId}")]
    internal static partial void StartingOrderProcessingWorkflow(this ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order processing workflow started for session {SessionId} with WorkflowId {WorkflowId}")]
    internal static partial void OrderProcessingWorkflowStarted(this ILogger logger, string sessionId, string workflowId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order processing workflow already started for session {SessionId}. RunId: {RunId}")]
    internal static partial void OrderProcessingWorkflowAlreadyStarted(this ILogger logger, string sessionId, string runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start order processing workflow for session {SessionId}")]
    internal static partial void FailedToStartOrderProcessingWorkflow(this ILogger logger, Exception exception, string sessionId);
}
