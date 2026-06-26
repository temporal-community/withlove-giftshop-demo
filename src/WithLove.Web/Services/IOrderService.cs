using WithLove.Web.Models;

namespace WithLove.Web.Services;

/// <summary>Provides order history data sourced from Stripe Checkout Sessions.</summary>
public interface IOrderService
{
    /// <summary>Returns one page of paid sessions for the given user.</summary>
    /// <param name="userId">The authenticated user's identity ID.</param>
    /// <param name="cursor">Pass <c>null</c> for the first page; pass the previous <see cref="OrdersPage.NextCursor"/> for "Load More".</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OrdersPage> GetOrdersAsync(string userId, string? cursor = null, CancellationToken ct = default);

    /// <summary>Returns the full order detail for a confirmation number, or <c>null</c> if not found or unauthorized.</summary>
    /// <param name="userId">The authenticated user's identity ID.</param>
    /// <param name="confirmationNumber">The "WL-XXXXXXX" confirmation number used as the URL route parameter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OrderDetailView?> GetOrderAsync(string userId, string confirmationNumber, CancellationToken ct = default);
}
