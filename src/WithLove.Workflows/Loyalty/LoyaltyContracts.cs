namespace WithLove.Workflows.Loyalty;

public enum LoyaltyTier { Bronze, Silver, Gold }

/// <summary>
/// Committed transaction — written only when CommitRedemptionAsync runs, never on reserve.
/// </summary>
public record PointTransaction(
    string StripeSessionId,  // cs_... for purchases; redemptionId for committed redemptions
    string? DisplayRef,      // Human-readable order confirmation number (e.g. "#WL-XXXXXX") for UI
    int Points,              // positive = earned, negative = redeemed (committed only)
    string Reason,           // "Purchase" | "Redemption"
    DateTime OccurredAt);

/// <summary>
/// In-flight reservation: balance is held but transaction not yet written.
/// StripeSessionId deliberately omitted — RedemptionId is the stable key stored in Stripe metadata.
/// Commit is always via redemptionId (from metadata), never via session lookup.
/// </summary>
public record PendingRedemption(
    string RedemptionId,     // Workflow.NewGuid() — returned to Blazor, stored in Stripe metadata
    int Points,
    decimal DiscountAmount,
    DateTime InitiatedAt);

public record LoyaltyState(
    int Balance,
    int LifetimeEarned,
    List<PointTransaction> Transactions,
    HashSet<string> ProcessedEarnSessionIds,                         // idempotency: prevent double-earn
    Dictionary<string, PendingRedemption> PendingRedemptions,       // key = RedemptionId
    HashSet<string> CommittedRedemptionIds,                          // idempotency: prevent double-commit
    HashSet<string> CancelledRedemptionIds)                          // idempotency: prevent double-cancel
{
    // Tier is always derived — never stored — eliminates drift risk
    // Tier computed from LifetimeEarned — redemptions never demote tier
    // Thresholds: Bronze 0–499 | Silver 500–1,999 | Gold 2,000+
    public LoyaltyTier Tier => LifetimeEarned switch
    {
        >= 2000 => LoyaltyTier.Gold,
        >= 500  => LoyaltyTier.Silver,
        _       => LoyaltyTier.Bronze
    };

    // Each call returns a fresh instance to avoid shared mutable state
    public static LoyaltyState Empty =>
        new(0, 0, [], new(), new(), new(), new());

    /// <summary>
    /// Called just before ContinueAsNew — expire stale pending reservations, trim visible history.
    /// Must mirror ExpireStaleReservations: expired RedemptionIds go into CancelledRedemptionIds
    /// so that a late CommitRedemptionAsync after ContinueAsNew is a safe no-op, not a bad write.
    /// Uses &lt;= for expired check, &gt; for surviving check (consistent with ExpireStaleReservations).
    /// </summary>
    public LoyaltyState TrimAndExpire(DateTime cutoff)
    {
        var expired = PendingRedemptions.Values.Where(r => r.InitiatedAt <= cutoff).ToList();
        var expiredIds = expired.Select(r => r.RedemptionId).ToHashSet();
        var restoredBalance = Balance + expired.Sum(r => r.Points);
        return this with
        {
            Balance = restoredBalance,
            Transactions = Transactions.TakeLast(500).ToList(),
            // ProcessedEarnSessionIds — NEVER pruned. Long-term idempotency guard:
            // an earn session ID from years ago still prevents double-crediting on retry.
            PendingRedemptions = PendingRedemptions
                .Where(kv => kv.Value.InitiatedAt > cutoff)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            // Expired reservations moved to CancelledRedemptionIds — same logic as
            // ExpireStaleReservations(). CommittedRedemptionIds kept in full.
            // Collection expression target-types to HashSet<string> here, not List<string>.
            CancelledRedemptionIds = [.. CancelledRedemptionIds, .. expiredIds]
        };
    }
}

public record LoyaltyProfile(int Balance, int LifetimeEarned, LoyaltyTier Tier, int PointsToNextTier);

/// <summary>
/// Input for earning points after a purchase.
/// StripeSessionId is the idempotency key: webhook → StripeCheckoutOrderWorkflow → LoyaltyActivities → LoyaltyAccountWorkflow.
/// </summary>
public record EarnPointsInput(string StripeSessionId, string ConfirmationNumber, int Points);

/// <summary>
/// Input for reserving points at checkout.
/// No StripeSessionId — session doesn't exist yet at reserve time.
/// RedemptionId is the stable key; Stripe session is created AFTER reservation succeeds.
/// </summary>
public record ReservePointsInput(int PointsRequested);

public record ReservationResult(bool Success, string RedemptionId, int PointsReserved, decimal DiscountAmount);

public record EnsureLoyaltyInput(string UserId, string StripeSessionId, string ConfirmationNumber, int Points);
