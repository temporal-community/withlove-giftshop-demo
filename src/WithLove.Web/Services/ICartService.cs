using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using WithLove.Web.Models;

namespace WithLove.Web.Services;

public interface ICartService
{
    // Synchronous reads from in-memory snapshot
    IReadOnlyList<CartItem> Items { get; }
    int ItemCount { get; }
    decimal Subtotal { get; }
    decimal EnhancementsTotal { get; }
    decimal Total { get; }
    List<GiftEnhancement> Enhancements { get; }
    event Action? OnChange;

    // Initialization — call from OnInitializedAsync with userId.
    // Pass anonymousCartId when transitioning from anonymous to authenticated to merge carts.
    Task InitializeAsync(string userId, string? anonymousCartId = null);

    // Async mutations (update local snapshot immediately, then persist to cache)
    Task AddItemAsync(CartItem item);
    Task RemoveItemAsync(int productId);
    Task UpdateQuantityAsync(int productId, int quantity);
    Task ToggleEnhancementAsync(GiftEnhancement enhancement);
    Task ClearAsync();
}

public static class CartServiceExtensions
{
    public static async Task InitializeFromAuthAsync(
        this ICartService cartService,
        AuthenticationStateProvider authStateProvider,
        AnonymousCartSession anonSession)
    {
        var auth = await authStateProvider.GetAuthenticationStateAsync();
        var userId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var anonId = !string.IsNullOrEmpty(anonSession.CartId)
            ? anonSession.CartId
            : Guid.NewGuid().ToString("N");
        await cartService.InitializeAsync(
            userId ?? anonId,
            userId is not null ? anonId : null
        );
    }
}
