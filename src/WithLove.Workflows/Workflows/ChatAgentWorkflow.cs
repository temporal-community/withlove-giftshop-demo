using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using WithLove.Workflows.Activities;
using WithLove.Workflows.Chat;

namespace WithLove.Workflows.Workflows;

/// <summary>
/// Long-lived Temporal workflow — one instance per chat session.
/// Uses Update for sync request/response, Query for history reads, Signal for session end.
/// </summary>
[Workflow]
public partial class ChatAgentWorkflow
{
    private readonly List<ChatHistoryEntry> _history = [];
    private bool _isProcessing;
    private bool _shutdownRequested;

    [WorkflowRun]
    public async Task RunAsync(List<ChatHistoryEntry>? carriedHistory = null)
    {
        // Restore history from continue-as-new
        if (carriedHistory is { Count: > 0 })
            _history.AddRange(carriedHistory);

        LogSessionStarted(Workflow.Logger, Workflow.Info.WorkflowId);

        // Stay alive until shutdown or 24h idle timeout
        var conditionMet = await Workflow.WaitConditionAsync(
            () => _shutdownRequested || (!_isProcessing && Workflow.ContinueAsNewSuggested),
            timeout: TimeSpan.FromHours(24));

        if (!conditionMet)
        {
            LogSessionTimedOut(Workflow.Logger, Workflow.Info.WorkflowId);
        }
        else if (Workflow.ContinueAsNewSuggested && !_shutdownRequested)
        {
            LogSessionContinuedAsNew(Workflow.Logger, Workflow.Info.WorkflowId);
            var carried = _history.ToList();
            throw Workflow.CreateContinueAsNewException(
                (ChatAgentWorkflow wf) => wf.RunAsync(carried));
        }
    }

    [WorkflowUpdateValidator(nameof(SendMessageAsync))]
    public void ValidateSendMessage(ChatRequest request)
    {
        if (_shutdownRequested)
            throw new InvalidOperationException("Session has been shut down.");
        if (string.IsNullOrWhiteSpace(request.UserMessage))
            throw new ArgumentException("Message cannot be empty.");
    }

    [WorkflowUpdate]
    public async Task<ChatResponse> SendMessageAsync(ChatRequest request)
    {
        // Serialize: wait for any in-progress turn to finish
        await Workflow.WaitConditionAsync(() => !_isProcessing);
        _isProcessing = true;

        try
        {
            // Add user message to history
            _history.Add(new ChatHistoryEntry(true, request.UserMessage, Workflow.UtcNow));

            // Call AI inference activity (pass cart snapshot for view_cart tool)
            var input = new ChatInferenceInput([.. _history], request.UserMessage, request.Cart, request.User);

            var result = await Workflow.ExecuteActivityAsync(
                (ChatAgentActivities act) => act.InferAsync(input),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(2),
                    RetryPolicy = new()
                    {
                        InitialInterval = TimeSpan.FromSeconds(2),
                        BackoffCoefficient = 2.0f,
                        MaximumInterval = TimeSpan.FromSeconds(30),
                        MaximumAttempts = 3
                    }
                });

            // Add assistant response to history
            _history.Add(new ChatHistoryEntry(false, result.AssistantMessage, Workflow.UtcNow));

            return new ChatResponse(result.AssistantMessage, result.CartActions, result.NavigationActions);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    [WorkflowQuery]
    public IReadOnlyList<ChatHistoryEntry> GetHistory() => _history;

    [WorkflowSignal]
    public Task EndSessionAsync()
    {
        LogSessionEnded(Workflow.Logger, Workflow.Info.WorkflowId);
        _shutdownRequested = true;
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat session started: {WorkflowId}")]
    private static partial void LogSessionStarted(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat session timed out: {WorkflowId}")]
    private static partial void LogSessionTimedOut(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat session continue-as-new: {WorkflowId}")]
    private static partial void LogSessionContinuedAsNew(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat session end requested: {WorkflowId}")]
    private static partial void LogSessionEnded(ILogger logger, string workflowId);
}
