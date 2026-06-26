using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using WithLove.Workflows.Loyalty;

namespace WithLove.Workflows.Workflows;

/// <summary>
/// Long-lived Temporal workflow — one instance per customer loyalty account.
/// Identified by workflow ID "loyalty-{userId}". Uses Signal for earning/committing/cancelling,
/// Update for synchronous point reservation (prevents overdraft atomically), and Query for reads.
/// ContinueAsNew is used to bound event history growth.
/// </summary>
[Workflow]
public partial class LoyaltyAccountWorkflow
{
    private LoyaltyState _state = LoyaltyState.Empty;

    // Set by every mutating handler so the expiry loop recalculates nextExpiry on the next iteration.
    // Without this a newly added reservation would not shorten an active idle wait.
    private bool _stateChanged;

    [WorkflowRun]
    public async Task RunAsync(LoyaltyState? carriedState = null)
    {
        _state = carriedState ?? LoyaltyState.Empty;

        LogAccountStarted(Workflow.Logger, Workflow.Info.WorkflowId);

        while (!Workflow.ContinueAsNewSuggested)
        {
            _stateChanged = false;

            var nextExpiry = _state.PendingRedemptions.Values
                .Select(r => r.InitiatedAt.AddHours(24))
                .OrderBy(t => t)
                .Cast<DateTime?>()
                .FirstOrDefault();

            if (nextExpiry.HasValue)
            {
                var timeout = nextExpiry.Value - Workflow.UtcNow;
                await Workflow.WaitConditionAsync(
                    () => Workflow.ContinueAsNewSuggested || HasExpiredReservations() || _stateChanged,
                    timeout: timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout);
            }
            else
            {
                // No pending reservations — no timer needed; signals/updates wake via _stateChanged.
                await Workflow.WaitConditionAsync(
                    () => Workflow.ContinueAsNewSuggested || _stateChanged);
            }

            ExpireStaleReservations();
        }

        LogContinuingAsNew(Workflow.Logger, Workflow.Info.WorkflowId);

        throw Workflow.CreateContinueAsNewException(
            (LoyaltyAccountWorkflow wf) => wf.RunAsync(
                _state.TrimAndExpire(Workflow.UtcNow.AddHours(-24))));
    }

    // ─── Signals ──────────────────────────────────────────────────────────────

    /// <summary>Credits points after a confirmed purchase. Idempotent by StripeSessionId.</summary>
    [WorkflowSignal]
    public Task EarnPointsAsync(EarnPointsInput input)
    {
        if (!_state.ProcessedEarnSessionIds.Add(input.StripeSessionId))
        {
            LogDuplicateEarnIgnored(Workflow.Logger, input.StripeSessionId);
            return Task.CompletedTask;
        }

        _state = _state with
        {
            Balance = _state.Balance + input.Points,
            LifetimeEarned = _state.LifetimeEarned + input.Points,
            Transactions =
            [
                .._state.Transactions,
                new PointTransaction(
                    input.StripeSessionId,
                    input.ConfirmationNumber,
                    input.Points,
                    "Purchase",
                    Workflow.UtcNow)
            ]
        };

        LogPointsEarned(Workflow.Logger, input.Points, input.StripeSessionId, _state.Balance);
        _stateChanged = true;
        return Task.CompletedTask;
    }

    /// <summary>Writes the final redemption transaction once payment is confirmed. Idempotent.</summary>
    [WorkflowSignal]
    public Task CommitRedemptionAsync(string redemptionId)
    {
        if (_state.CommittedRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        if (_state.CancelledRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        // Remove from pending; safe no-op for late/duplicate webhook deliveries.
        if (!_state.PendingRedemptions.Remove(redemptionId, out var pending)) return Task.CompletedTask;

        _state.CommittedRedemptionIds.Add(redemptionId);
        _state = _state with
        {
            Transactions =
            [
                .._state.Transactions,
                new PointTransaction(
                    redemptionId,
                    null,
                    -pending.Points,    // negative = redeemed
                    "Redemption",
                    Workflow.UtcNow)
            ]
        };

        LogRedemptionCommitted(Workflow.Logger, redemptionId, _state.Balance);
        _stateChanged = true;
        return Task.CompletedTask;
    }

    /// <summary>Restores balance when a payment fails or is abandoned. Idempotent.</summary>
    [WorkflowSignal]
    public Task CancelRedemptionAsync(string redemptionId)
    {
        if (_state.CancelledRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        if (_state.CommittedRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        if (!_state.PendingRedemptions.Remove(redemptionId, out var pending)) return Task.CompletedTask;

        _state.CancelledRedemptionIds.Add(redemptionId);
        _state = _state with
        {
            Balance = _state.Balance + pending.Points
        };

        LogRedemptionCancelled(Workflow.Logger, redemptionId, _state.Balance);
        _stateChanged = true;
        return Task.CompletedTask;
    }

    // ─── Update (synchronous, validated) ──────────────────────────────────────

    [WorkflowUpdateValidator(nameof(ReservePointsAsync))]
    public void ValidateReservePoints(ReservePointsInput input)
    {
        // All loyalty invariants enforced server-side — UI validation is not sufficient
        if (input.PointsRequested <= 0)
            throw new ArgumentException("Points requested must be positive.");
        if (input.PointsRequested % 100 != 0)
            throw new ArgumentException("Points must be redeemed in multiples of 100.");
        if (input.PointsRequested > _state.Balance)
            throw new InvalidOperationException(
                $"Insufficient balance. Have {_state.Balance}, requested {input.PointsRequested}.");
    }

    /// <summary>
    /// Holds points against a pending Stripe checkout. Returns a RedemptionId the caller
    /// stores in Stripe session metadata for durable commit via <see cref="CommitRedemptionAsync"/>.
    /// No PointTransaction is written until commit — balance is deducted as a hold only.
    /// </summary>
    [WorkflowUpdate]
    public Task<ReservationResult> ReservePointsAsync(ReservePointsInput input)
    {
        // Workflow.NewGuid() is deterministic and replay-safe; never use Guid.NewGuid() in workflow code.
        var redemptionId = Workflow.NewGuid().ToString();
        var discountAmount = input.PointsRequested / 100m; // 100 pts = $1

        _state = _state with { Balance = _state.Balance - input.PointsRequested };
        _state.PendingRedemptions[redemptionId] = new PendingRedemption(
            redemptionId, input.PointsRequested, discountAmount, Workflow.UtcNow);

        LogPointsReserved(Workflow.Logger, input.PointsRequested, redemptionId);
        _stateChanged = true;

        return Task.FromResult(new ReservationResult(redemptionId, input.PointsRequested, discountAmount));
    }

    // ─── Queries ──────────────────────────────────────────────────────────────

    [WorkflowQuery]
    public int GetBalance() => _state.Balance;

    [WorkflowQuery]
    public LoyaltyProfile GetLoyaltyProfile()
    {
        var pointsToNextTier = _state.Tier switch
        {
            LoyaltyTier.Bronze => 500 - _state.LifetimeEarned,
            LoyaltyTier.Silver => 2000 - _state.LifetimeEarned,
            LoyaltyTier.Gold   => 0,
            _                  => 0
        };

        return new LoyaltyProfile(
            _state.Balance,
            _state.LifetimeEarned,
            _state.Tier,
            pointsToNextTier);
    }

    [WorkflowQuery]
    public IReadOnlyList<PointTransaction> GetTransactionHistory() => _state.Transactions;

    // ─── Private helpers ──────────────────────────────────────────────────────

    private bool HasExpiredReservations() =>
        _state.PendingRedemptions.Values.Any(r => r.InitiatedAt.AddHours(24) <= Workflow.UtcNow);

    // Restores balance for all reservations past their 24h deadline and moves their IDs into
    // CancelledRedemptionIds so any late CommitRedemptionAsync signal is a safe no-op.
    private void ExpireStaleReservations()
    {
        var cutoff = Workflow.UtcNow.AddHours(-24); // <= matches HasExpiredReservations exactly
        var expired = _state.PendingRedemptions.Values.Where(r => r.InitiatedAt <= cutoff).ToList();
        if (expired.Count == 0) return;

        var expiredIds = expired.Select(r => r.RedemptionId).ToHashSet();
        _state = _state with
        {
            Balance = _state.Balance + expired.Sum(r => r.Points),
            PendingRedemptions = _state.PendingRedemptions
                .Where(kv => kv.Value.InitiatedAt > cutoff)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            CancelledRedemptionIds = [.. _state.CancelledRedemptionIds, .. expiredIds]
        };
    }

    // ─── Structured log methods — Workflow.Logger suppresses duplicates on replay ─

    [LoggerMessage(Level = LogLevel.Information, Message = "Loyalty account started: {WorkflowId}")]
    private static partial void LogAccountStarted(ILogger logger, string workflowId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Points earned: {Points} for session {StripeSessionId} (balance: {Balance})")]
    private static partial void LogPointsEarned(ILogger logger, int points, string stripeSessionId, int balance);

    [LoggerMessage(Level = LogLevel.Information, Message = "Points reserved: {Points} pts, redemptionId {RedemptionId}")]
    private static partial void LogPointsReserved(ILogger logger, int points, string redemptionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Redemption committed: {RedemptionId} (balance: {Balance})")]
    private static partial void LogRedemptionCommitted(ILogger logger, string redemptionId, int balance);

    [LoggerMessage(Level = LogLevel.Information, Message = "Redemption cancelled: {RedemptionId} (balance restored: {Balance})")]
    private static partial void LogRedemptionCancelled(ILogger logger, string redemptionId, int balance);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate earn signal ignored: {StripeSessionId}")]
    private static partial void LogDuplicateEarnIgnored(ILogger logger, string stripeSessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loyalty account continuing as new: {WorkflowId}")]
    private static partial void LogContinuingAsNew(ILogger logger, string workflowId);
}
