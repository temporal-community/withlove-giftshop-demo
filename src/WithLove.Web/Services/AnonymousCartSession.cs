using Microsoft.AspNetCore.Components;

namespace WithLove.Web.Services;

/// <summary>
/// Scoped service that bridges the HTTP phase (where the wl-cart-id cookie is read/set)
/// to the Blazor SignalR circuit (where the cart is initialized).
/// Populated by AnonymousCartMiddleware during the initial HTTP request.
/// [PersistentState] ensures CartId survives the prerender→circuit transition.
/// </summary>
public sealed class AnonymousCartSession
{
    [PersistentState]
    public string CartId { get; set; } = string.Empty;

    public void Initialize(string cartId) => CartId = cartId;
}
