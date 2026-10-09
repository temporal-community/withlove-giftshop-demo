using TemporalCommunity.Extensions.AI;
using TemporalCommunity.Extensions.AI.Exceptions;
using TemporalCommunity.Extensions.AI.Session;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using WithLove.Workflows;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Services;

public interface IGiftShopChatWorkflowClient
{
    Task EnsureStartedAsync(string workflowId);

    /// <summary>
    /// Sends one durable turn to the session workflow.
    /// </summary>
    /// <param name="workflowId">The session workflow ID.</param>
    /// <param name="operationId">
    /// The caller's correlation key for this turn. It is <em>not</em> an idempotency key and must
    /// never be used as the Temporal Update ID — see the remarks on the implementation.
    /// </param>
    /// <param name="request">The durable turn request.</param>
    Task<DurableTurnResult<GiftShopChatTurnState>> SendMessageAsync(
        string workflowId,
        string operationId,
        DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request);

    /// <summary>
    /// Returns the durable session history, but only from a run that is still open.
    /// </summary>
    /// <exception cref="Temporalio.Exceptions.WorkflowQueryRejectedException">
    /// The latest run under <paramref name="workflowId"/> is closed.
    /// </exception>
    Task<IReadOnlyList<DurableSessionEntry>> GetHistoryAsync(string workflowId);

    /// <summary>
    /// Shuts the session down and waits for the run to actually close.
    /// </summary>
    /// <param name="workflowId">The session workflow ID.</param>
    /// <param name="cancellationToken">
    /// Bounds the wait for completion. The caller decides that bound, because the right answer
    /// differs by call site: End Chat can afford to wait, a logout cannot.
    /// </param>
    Task ShutdownAsync(string workflowId, CancellationToken cancellationToken = default);
}

internal sealed class GiftShopChatWorkflowClient(
    ITemporalClient temporalClient,
    IDurableChatWorkflowInputFactory workflowInputFactory) : IGiftShopChatWorkflowClient
{
    public async Task EnsureStartedAsync(string workflowId)
    {
        var input = workflowInputFactory.Create();
        await temporalClient.StartWorkflowAsync(
            (GiftShopChatWorkflow workflow) => workflow.RunAsync(input),
            new WorkflowOptions(workflowId, WorkflowConstants.DefaultTaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
            });
    }

    /// <remarks>
    /// The turn is dispatched with the server-assigned Update ID. <paramref name="operationId"/> is
    /// deliberately not passed as <c>WorkflowUpdateOptions.Id</c>: it is minted fresh per call and
    /// never reused, so it cannot deduplicate anything. Setting it as the Update ID would advertise
    /// an at-most-once guarantee that does not exist, and no GiftShop tool performs an external
    /// mutating side effect that would need one. It stays a correlation key only — carried on
    /// <c>CorrelationId</c>, <c>RequestData.OperationId</c>, and the caller's span tag.
    /// </remarks>
    public Task<DurableTurnResult<GiftShopChatTurnState>> SendMessageAsync(
        string workflowId,
        string operationId,
        DurableTurnRequest<GiftShopChatRequestData, GiftShopChatTurnState> request)
    {
        // Fail before dispatch, in the same spirit as the two guards below: the workflow's update
        // validator rejects a request whose CorrelationId and RequestData.OperationId disagree, so
        // catching it here turns a Temporal round-trip into a local throw.
        if (!string.Equals(operationId, request.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(operationId, request.RequestData?.OperationId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The operation ID must match both CorrelationId and RequestData.OperationId.",
                nameof(request));
        }

        if (request.Options is null)
        {
            throw new DurableConfigurationException(
                "DurableTurnRequest.Options cannot be null for a GiftShop durable turn.");
        }

        if (request.ChatOptions?.Tools is { Count: > 0 })
        {
            throw new DurableConfigurationException(
                "ChatOptions.Tools cannot be used for a GiftShop durable turn. " +
                "GiftShop tools are registered with AddDurableTool so Temporal can schedule " +
                "each invocation as an activity.");
        }

        var handle = temporalClient.GetWorkflowHandle<GiftShopChatWorkflow>(workflowId);
        return handle.ExecuteUpdateAsync(workflow => workflow.SendMessageAsync(request));
    }

    /// <remarks>
    /// The reject condition is set <em>per query</em> rather than on the client. This
    /// <c>ITemporalClient</c> is shared with <c>StripeEventHandler</c> and
    /// <c>TemporalLoyaltyService</c>, so a client-level condition would silently change their query
    /// behaviour too.
    /// </remarks>
    public Task<IReadOnlyList<DurableSessionEntry>> GetHistoryAsync(string workflowId)
    {
        var handle = temporalClient.GetWorkflowHandle<GiftShopChatWorkflow>(workflowId);
        return handle.QueryAsync(
            workflow => workflow.GetHistory(),
            new WorkflowQueryOptions
            {
                // Render only what the model can see. A closed run still answers queries with its
                // full transcript, but that transcript is unreachable to the model that will handle
                // the next turn — the next message starts a new run with empty history. Showing it
                // would put a conversation on screen that the assistant has no memory of, and the
                // customer only discovers that by leaning on earlier context ("as I said, my mum is
                // allergic to lavender") and being contradicted. An empty panel is honest.
                //
                // This is also what makes End Chat work for authenticated users, whose workflow ID
                // was already stable and whose "cleared" transcript therefore came straight back on
                // reopen. Verified against a real server: after a shutdown signal the run reports
                // Completed and an unguarded query still returns every entry.
                RejectCondition = QueryRejectCondition.NotOpen,
            });
    }

    /// <remarks>
    /// A signal is not a completion. <c>RequestShutdownAsync</c> only sets a flag the workflow
    /// notices when it next wakes, so returning as soon as the signal is accepted leaves a window in
    /// which the run is still open and still flagged. A message sent into that window attaches to
    /// the old run via <c>IdConflictPolicy.UseExisting</c> and is rejected by the update validator
    /// with "Session has been shut down." Waiting for the result closes the window.
    /// </remarks>
    public async Task ShutdownAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        var handle = temporalClient.GetWorkflowHandle<GiftShopChatWorkflow>(workflowId);
        await handle.SignalAsync(
            workflow => workflow.RequestShutdownAsync(),
            new WorkflowSignalOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });

        await handle.GetResultAsync(
            rpcOptions: new RpcOptions { CancellationToken = cancellationToken });
    }
}
