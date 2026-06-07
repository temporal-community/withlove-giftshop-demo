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
    // Single state object — use LoyaltyState.Empty, not new(...) — constructor has 7 params, Tier is derived
    private LoyaltyState _state = LoyaltyState.Empty;

    // _stateChanged: set by every mutating signal/update so the expiry loop recalculates
    // nextExpiry after a new reservation is added. Without this, a workflow with no pending
    // reservations would not recalculate its expiry deadline when a new reservation arrives.
    private bool _stateChanged;

    // No separate _processedSessionIds field — _state.ProcessedEarnSessionIds is the
    // authoritative idempotency set. Use it directly; no cache, no rebuild-on-restore needed.
    //
    // NOTE: LoyaltyState uses mutable HashSet<string> and Dictionary<string,PendingRedemption>
    // as record fields. `with` expressions perform shallow copies — unlisted fields share the same
    // collection reference. In-place .Add() on these collections is safe because the workflow is
    // single-threaded (Temporal guarantees one coroutine at a time). This is an intentional
    // trade-off: consistency and clarity over strict record immutability.

    [WorkflowRun]
    public async Task RunAsync(LoyaltyState? carriedState = null)
    {
        _state = carriedState ?? LoyaltyState.Empty;

        LogAccountStarted(Workflow.Logger, Workflow.Info.WorkflowId);

        // Loop: wake on signals/updates (via _stateChanged), OR when a pending reservation
        // deadline arrives, OR when ContinueAsNew is suggested.
        // _stateChanged ensures the loop recalculates nextExpiry after every mutation —
        // e.g., a new ReservePointsAsync must switch from idle waiting to a 24h expiry deadline.
        while (!Workflow.ContinueAsNewSuggested)
        {
            _stateChanged = false; // reset before each wait

            var nextExpiry = _state.PendingRedemptions.Values
                .Select(r => r.InitiatedAt.AddHours(24))
                .OrderBy(t => t)
                .Cast<DateTime?>()
                .FirstOrDefault();

            if (nextExpiry.HasValue)
            {
                // Timed wait: wake when the nearest reservation expires, or when state changes
                var timeout = nextExpiry.Value - Workflow.UtcNow;
                await Workflow.WaitConditionAsync(
                    () => Workflow.ContinueAsNewSuggested || HasExpiredReservations() || _stateChanged,
                    timeout: timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout);
            }
            else
            {
                // No pending reservations: wait indefinitely for a signal/update or ContinueAsNew.
                // Avoid scheduling/cancelling artificial long timers just to stay alive.
                await Workflow.WaitConditionAsync(
                    () => Workflow.ContinueAsNewSuggested || _stateChanged);
            }

            ExpireStaleReservations(); // no-op if nothing expired
            // loop continues: recalculates nextExpiry with fresh _state
        }

        LogContinuingAsNew(Workflow.Logger, Workflow.Info.WorkflowId);

        // Expire any remaining stale reservations before carrying state forward
        throw Workflow.CreateContinueAsNewException(
            (LoyaltyAccountWorkflow wf) => wf.RunAsync(
                _state.TrimAndExpire(Workflow.UtcNow.AddHours(-24))));
    }

    // ─── Signals ──────────────────────────────────────────────────────────────

    /// <summary>
    /// EARN — fire-and-forget, idempotent by StripeSessionId.
    /// Credits points after a confirmed purchase. ProcessedEarnSessionIds prevents double-crediting.
    /// </summary>
    [WorkflowSignal]
    public Task EarnPointsAsync(EarnPointsInput input)
    {
        // _state.ProcessedEarnSessionIds is authoritative — mutate in place (single-threaded)
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
        _stateChanged = true; // wake the expiry loop to recalculate
        return Task.CompletedTask;
    }

    /// <summary>
    /// COMMIT — payment confirmed, write final transaction.
    /// Idempotent: no-op if already committed or cancelled/expired.
    /// </summary>
    [WorkflowSignal]
    public Task CommitRedemptionAsync(string redemptionId)
    {
        // Idempotency: no-op if already committed or cancelled/expired
        if (_state.CommittedRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        if (_state.CancelledRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        // No-op if not pending — handles late/duplicate webhook commits without failing the activity
        if (!_state.PendingRedemptions.Remove(redemptionId, out var pending)) return Task.CompletedTask;

        // Use captured `pending` for points/discount details.
        // Move from PendingRedemptions → CommittedRedemptionIds, write PointTransaction.
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

    /// <summary>
    /// CANCEL — payment failed/abandoned, restore balance.
    /// Idempotent: no-op if already cancelled or committed.
    /// </summary>
    [WorkflowSignal]
    public Task CancelRedemptionAsync(string redemptionId)
    {
        // Idempotency: no-op if already cancelled or committed
        if (_state.CancelledRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        if (_state.CommittedRedemptionIds.Contains(redemptionId)) return Task.CompletedTask;
        // No-op if not pending — handles duplicate cancel signals gracefully
        if (!_state.PendingRedemptions.Remove(redemptionId, out var pending)) return Task.CompletedTask;

        // Use captured `pending` for restore amount.
        // Move from PendingRedemptions → CancelledRedemptionIds, restore balance.
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
    /// RESERVE — holds points, returns RedemptionId.
    /// Synchronous (Update) to prevent overdraft atomically.
    /// Does NOT write a PointTransaction yet — that happens at CommitRedemptionAsync.
    /// </summary>
    [WorkflowUpdate]
    public Task<ReservationResult> ReservePointsAsync(ReservePointsInput input)
    {
        // RedemptionId generated here — Workflow.NewGuid() is deterministic/replay-safe
        var redemptionId = Workflow.NewGuid().ToString();
        var discountAmount = input.PointsRequested / 100m; // 100 points = $1

        var pending = new PendingRedemption(
            redemptionId,
            input.PointsRequested,
            discountAmount,
            Workflow.UtcNow);

        // Deduct balance and add to PendingRedemptions
        _state = _state with
        {
            Balance = _state.Balance - input.PointsRequested
        };
        _state.PendingRedemptions[redemptionId] = pending;

        LogPointsReserved(Workflow.Logger, input.PointsRequested, redemptionId);
        _stateChanged = true; // wake the expiry loop — new reservation shortens next deadline

        return Task.FromResult(new ReservationResult(true, redemptionId, input.PointsRequested, discountAmount));
    }

    // ─── Queries ──────────────────────────────────────────────────────────────

    [WorkflowQuery]
    public int GetBalance() => _state.Balance;

    [WorkflowQuery]
    public LoyaltyProfile GetLoyaltyProfile()
    {
        // PointsToNextTier: derived from LifetimeEarned toward next threshold
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

    /// <summary>
    /// Returns true if any pending reservation has passed its 24-hour expiry deadline.
    /// Uses AddHours(24) &lt;= UtcNow to match ExpireStaleReservations cutoff logic exactly.
    /// </summary>
    private bool HasExpiredReservations() =>
        _state.PendingRedemptions.Values.Any(r => r.InitiatedAt.AddHours(24) <= Workflow.UtcNow);

    /// <summary>
    /// Expires all pending reservations older than 24 hours, restoring balance and moving
    /// their IDs to CancelledRedemptionIds so late commits are safe no-ops.
    /// </summary>
    private void ExpireStaleReservations()
    {
        var cutoff = Workflow.UtcNow.AddHours(-24);
        // Use <= to match HasExpiredReservations predicate — consistent at the exact 24h boundary
        var expired = _state.PendingRedemptions.Values.Where(r => r.InitiatedAt <= cutoff).ToList();
        if (expired.Count == 0) return;

        // Move expired redemptionIds into CancelledRedemptionIds so that a late
        // CommitRedemptionAsync is a safe no-op rather than silently writing a bad transaction.
        // CancelRedemptionAsync on an already-cancelled id is also a no-op via the same set.
        var expiredIds = expired.Select(r => r.RedemptionId).ToHashSet();

        _state = _state with
        {
            Balance = _state.Balance + expired.Sum(r => r.Points),
            PendingRedemptions = _state.PendingRedemptions
                .Where(kv => kv.Value.InitiatedAt > cutoff)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            // Collection expression target-types to HashSet<string> here, not List<string>.
            CancelledRedemptionIds = [.. _state.CancelledRedemptionIds, .. expiredIds]
        };
    }

    // ─── Replay-safe logger source gen (partial methods) ──────────────────────

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
