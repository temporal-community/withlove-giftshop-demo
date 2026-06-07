using Microsoft.Extensions.Logging;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using WithLove.Workflows;
using WithLove.Workflows.Loyalty;
using WithLove.Workflows.Workflows;

namespace WithLove.Web.Services;

/// <summary>Temporal-backed Love Tokens client. Workflow ID: loyalty-{userId}.</summary>
public class TemporalLoyaltyService(ITemporalClient temporalClient, ILogger<TemporalLoyaltyService> logger)
    : ILoyaltyService
{
    private string WorkflowId(string userId) => $"loyalty-{userId}";

    /// <summary>Starts the loyalty workflow if needed before sending an Update.</summary>
    private async Task EnsureWorkflowAsync(string userId, CancellationToken ct)
    {
        try
        {
            await temporalClient.StartWorkflowAsync(
                (LoyaltyAccountWorkflow wf) => wf.RunAsync(null),
                new WorkflowOptions(WorkflowId(userId), WorkflowConstants.DefaultTaskQueue)
                {
                    IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting
                });
        }
        catch (Exception ex)
        {
            logger.FailedToEnsureLoyaltyWorkflow(ex, userId);
        }
    }

    /// <inheritdoc />
    public async Task<LoyaltyProfile?> GetLoyaltyProfileAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var handle = temporalClient.GetWorkflowHandle<LoyaltyAccountWorkflow>(WorkflowId(userId));
            return await handle.QueryAsync(wf => wf.GetLoyaltyProfile());
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetBalanceAsync(string userId, CancellationToken ct = default)
    {
        var profile = await GetLoyaltyProfileAsync(userId, ct);
        return profile?.Balance ?? 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PointTransaction>> GetTransactionHistoryAsync(
        string userId, int limit = 10, CancellationToken ct = default)
    {
        try
        {
            var handle = temporalClient.GetWorkflowHandle<LoyaltyAccountWorkflow>(WorkflowId(userId));
            var history = await handle.QueryAsync(wf => wf.GetTransactionHistory());
            return history.TakeLast(limit).ToList();
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<ReservationResult> ReservePointsAsync(
        string userId, int pointsRequested, CancellationToken ct = default)
    {
        await EnsureWorkflowAsync(userId, ct);

        var handle = temporalClient.GetWorkflowHandle<LoyaltyAccountWorkflow>(WorkflowId(userId));
        return await handle.ExecuteUpdateAsync(
            wf => wf.ReservePointsAsync(new ReservePointsInput(pointsRequested)));
    }

    /// <inheritdoc />
    public async Task CommitRedemptionAsync(string userId, string redemptionId, CancellationToken ct = default)
    {
        var handle = temporalClient.GetWorkflowHandle<LoyaltyAccountWorkflow>(WorkflowId(userId));
        await handle.SignalAsync(wf => wf.CommitRedemptionAsync(redemptionId));
    }

    /// <inheritdoc />
    public async Task CancelRedemptionAsync(string userId, string redemptionId, CancellationToken ct = default)
    {
        var handle = temporalClient.GetWorkflowHandle<LoyaltyAccountWorkflow>(WorkflowId(userId));
        await handle.SignalAsync(wf => wf.CancelRedemptionAsync(redemptionId));
    }
}
