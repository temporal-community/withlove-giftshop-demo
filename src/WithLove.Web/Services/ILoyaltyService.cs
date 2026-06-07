using WithLove.Workflows.Loyalty;

namespace WithLove.Web.Services;

public interface ILoyaltyService
{
    Task<LoyaltyProfile?> GetLoyaltyProfileAsync(string userId, CancellationToken ct = default);
    Task<int> GetBalanceAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<PointTransaction>> GetTransactionHistoryAsync(string userId, int limit = 10, CancellationToken ct = default);

    // Returns RedemptionId — caller stores it and passes to Stripe session metadata.
    // No StripeSessionId parameter — session does not exist yet at reserve time.
    Task<ReservationResult> ReservePointsAsync(string userId, int pointsRequested, CancellationToken ct = default);

    // Called by StripeCheckoutOrderWorkflow (via LoyaltyActivities) after payment confirmed.
    Task CommitRedemptionAsync(string userId, string redemptionId, CancellationToken ct = default);

    // Called by Blazor if session creation fails before metadata is durably stored.
    Task CancelRedemptionAsync(string userId, string redemptionId, CancellationToken ct = default);
}
