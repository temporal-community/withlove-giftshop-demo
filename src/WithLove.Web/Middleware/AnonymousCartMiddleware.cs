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

                // Matches wl-chat-id, so the two anonymous-identity cookies have one shape rather
                // than two. Whether this cookie still round-trips depends on the scheme the browser
                // is on, which depends on how the app was started:
                //
                //   aspire run — the shopSite child inherits the AppHost's launch profile (https)
                //     and is given ASPNETCORE_URLS=https://...;http://... plus
                //     ASPNETCORE_HTTPS_PORT, so UseHttpsRedirection (registered immediately before
                //     this middleware) has a port to redirect to and the browser is on https.
                //     Observed 2026-09-12 against the running shopSite: a second request over
                //     https://localhost:7260 returned both cookies and the server minted nothing.
                //
                //   dotnet run --project src/WithLove.Web — picks the first profile, http, where
                //     UseHttpsRedirection cannot determine an https port and serves plaintext. The
                //     cookie survives there only because browsers treat localhost as a trustworthy
                //     origin; served over http from any non-loopback host it is dropped silently
                //     and every request mints a new, empty anonymous cart.
                Secure = true,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                IsEssential = true
            });
        }
        session.Initialize(cartId);
        await next(context);
    }
}
