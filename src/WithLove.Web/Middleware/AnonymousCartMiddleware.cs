using WithLove.Web.Services;

namespace WithLove.Web.Middleware;

/// <summary>
/// Reads the wl-cart-id cookie to identify anonymous carts.
/// If no cookie exists, generates a new GUID and sets it in the response.
/// Populates the scoped AnonymousCartSession so Blazor components can access the cart ID.
/// Must run before authentication middleware so all requests get a cart ID.
/// </summary>
public class AnonymousCartMiddleware(RequestDelegate next)
{
    private const string CookieName = "wl-cart-id";

    public async Task InvokeAsync(HttpContext context, AnonymousCartSession session)
    {
        var cartId = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(cartId))
        {
            cartId = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(CookieName, cartId, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                IsEssential = true
            });
        }
        session.Initialize(cartId);
        await next(context);
    }
}
