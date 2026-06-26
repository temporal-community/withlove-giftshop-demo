using Microsoft.AspNetCore.Identity;
using Stripe;
using Stripe.Checkout;
using WithLove.Data.Models;
using WithLove.Web.Models;
using WithLove.Workflows.Activities;
using ZiggyCreatures.Caching.Fusion;
using WebProduct = WithLove.Web.Models.Product;

namespace WithLove.Web.Services;

/// <summary>
/// Retrieves order history from Stripe Checkout Sessions with FusionCache-backed confirmation number resolution.
/// Cursor-based pagination mirrors Stripe's own list API.
/// </summary>
/// <remarks>
/// The constructor takes <see cref="SessionService"/> (not <see cref="StripeClient"/>) for testability:
/// <c>V1Services</c> and <c>CheckoutService</c> both have internal-only constructors that cannot be faked
/// by FakeItEasy, whereas <see cref="SessionService"/> has a public parameterless constructor and its
/// <see cref="SessionService.LineItems"/> property is virtual — both are fakeable.
/// </remarks>
public class StripeOrderService(
    SessionService sessionService,
    UserManager<ShopUser> userManager,
    IProductService productService,
    IFusionCache cache) : IOrderService
{
    private const int PageSize = 25;

    /// <inheritdoc />
    public async Task<OrdersPage> GetOrdersAsync(string userId, string? cursor = null, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || string.IsNullOrEmpty(user.StripeCustomerId))
            return new([], false, null);

        // Single Stripe API call per page — cursor-based pagination driven by the UI.
        // No Expand — line_items expansion unreliable on list endpoint. Thumbnails only on detail page.
        var page = await sessionService.ListAsync(new SessionListOptions
        {
            Customer = user.StripeCustomerId,
            Limit = PageSize,
            StartingAfter = cursor,
        }, cancellationToken: ct);

        var paid = page.Data.Where(s => s.PaymentStatus == "paid").ToList();

        // NextCursor = last session ID on this page, passed as StartingAfter on next request.
        var nextCursor = page.HasMore ? page.Data.LastOrDefault()?.Id : null;

        // Warm cache so detail page can resolve confirmationNumber → sessionId.
        // Key is user-scoped (userId prefix) — prevents cross-user collisions on 7-char suffix.
        foreach (var s in paid)
        {
            var key = $"order:confirm:{userId}:{OrderInfo.GenerateConfirmationNumber(s.Id)}";
            await cache.SetAsync(key, s.Id, new FusionCacheEntryOptions { Duration = TimeSpan.FromDays(365) }, ct);
        }

        return new(paid.Select(MapToSummary).ToList(), page.HasMore, nextCursor);
    }

    /// <inheritdoc />
    public async Task<OrderDetailView?> GetOrderAsync(string userId, string confirmationNumber, CancellationToken ct = default)
    {
        // 1. Fast path: cache holds confirmationNumber → sessionId (user-scoped key prevents cross-user collision)
        var sessionId = await cache.GetOrDefaultAsync<string>($"order:confirm:{userId}:{confirmationNumber}");

        // 2. Cold-cache fallback: scan customer sessions to find the match.
        //    Needed for orders placed before Redis was wired up, or after cache eviction.
        if (sessionId is null)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is null || string.IsNullOrEmpty(user.StripeCustomerId)) return null;

            string? scanCursor = null;
            do
            {
                var scanPage = await sessionService.ListAsync(
                    new SessionListOptions { Customer = user.StripeCustomerId, Limit = 100, StartingAfter = scanCursor },
                    cancellationToken: ct);

                foreach (var s in scanPage.Data.Where(s => s.PaymentStatus == "paid"))
                {
                    if (OrderInfo.GenerateConfirmationNumber(s.Id) == confirmationNumber)
                    {
                        sessionId = s.Id;
                        // Backfill so future loads are fast
                        await cache.SetAsync($"order:confirm:{userId}:{confirmationNumber}", sessionId,
                            new FusionCacheEntryOptions { Duration = TimeSpan.FromDays(365) }, ct);
                        break;
                    }
                }
                scanCursor = (sessionId is null && scanPage.HasMore) ? scanPage.Data.LastOrDefault()?.Id : null;
            }
            while (scanCursor is not null && sessionId is null);
        }

        if (sessionId is null) return null;

        // 3. Fetch session and validate ownership
        Session session;
        try
        {
            session = await sessionService.GetAsync(sessionId,
                new SessionGetOptions { Expand = ["customer"] }, cancellationToken: ct);
        }
        catch (StripeException) { return null; }

        if (session.PaymentStatus != "paid") return null;

        // Ownership check: session must belong to the requesting user's Stripe customer
        var owner = await userManager.FindByIdAsync(userId);
        if (owner is null || session.CustomerId != owner.StripeCustomerId) return null;

        // 4. Fetch ALL line items via cursor-based pagination.
        // sessionService.LineItems.ListAsync is the current SDK API;
        // SessionService.ListLineItemsAsync is marked [Obsolete] in stripe-dotnet.
        var allLineItems = new List<LineItem>();
        string? liCursor = null;
        do
        {
            var liPage = await sessionService.LineItems.ListAsync(
                sessionId,
                new SessionLineItemListOptions { Limit = 100, StartingAfter = liCursor },
                cancellationToken: ct);
            allLineItems.AddRange(liPage.Data);
            liCursor = liPage.HasMore ? liPage.Data.LastOrDefault()?.Id : null;
        }
        while (liCursor is not null);

        var allProducts = await BuildProductLookupAsync(ct);
        return MapToDetail(session, allLineItems, allProducts);
    }

    private async Task<List<WebProduct>> BuildProductLookupAsync(CancellationToken ct)
    {
        return await productService.GetProductsAsync();
    }

    private static OrderSummaryView MapToSummary(Session session) =>
        new(
            ConfirmationNumber: OrderInfo.GenerateConfirmationNumber(session.Id),
            Status: "Confirmed",
            PlacedAt: session.Created,
            AmountTotal: (session.AmountTotal ?? 0) / 100m,
            LineItems: []);   // line items not fetched on list; detail page only

    private static OrderDetailView MapToDetail(
        Session session,
        IList<LineItem> lineItems,
        List<WebProduct> allProducts)
    {
        // TryAdd avoids throwing on duplicate StripePriceId entries in the catalog.
        var productsByPriceId = new Dictionary<string, WebProduct>(StringComparer.Ordinal);
        foreach (var p in allProducts.Where(p => !string.IsNullOrEmpty(p.StripePriceId)))
            productsByPriceId.TryAdd(p.StripePriceId!, p);

        var productsByName = new Dictionary<string, WebProduct>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in allProducts)
            productsByName.TryAdd(p.Name, p);

        var views = lineItems.Select(li =>
        {
            var product = li.Price?.Id is { } priceId
                ? productsByPriceId.GetValueOrDefault(priceId)
                : null;
            
            if (product is null && li.Description is { } desc)
            {
                product = productsByName.GetValueOrDefault(desc);
            }

            return new OrderLineItemView(
                ProductId: product?.Id,
                StripePriceId: li.Price?.Id,
                ProductName: product?.Name ?? li.Description ?? "Gift Enhancement",
                ImageUrl: product?.ImageUrl,
                Quantity: (int)(li.Quantity ?? 1),
                // UnitPrice from Stripe (historical) — do NOT use catalog price.
                // Detail page shows what was actually charged; reorder uses current catalog independently.
                UnitPrice: li.AmountTotal / 100m / (li.Quantity ?? 1L),
                IsEnhancement: product is null);
        }).ToList();

        return new(
            ConfirmationNumber: OrderInfo.GenerateConfirmationNumber(session.Id),
            Status: "Confirmed",
            PlacedAt: session.Created,
            AmountTotal: (session.AmountTotal ?? 0) / 100m,
            LineItems: views);
    }
}
