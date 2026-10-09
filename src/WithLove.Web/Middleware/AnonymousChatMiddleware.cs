using WithLove.Web.Services;

namespace WithLove.Web.Middleware;

/// <summary>
/// Reads the wl-chat-id cookie to identify anonymous chat sessions, minting one when the cookie is
/// absent or malformed, and populates the scoped <see cref="AnonymousChatSession"/> so the Blazor
/// circuit can derive a stable chat workflow ID.
/// </summary>
/// <remarks>
/// This middleware only ever <em>mints</em>; it never rotates. Rotation belongs at the two events
/// where the identity genuinely changes — login and logout — and is done there, at the call site,
/// rather than by a middleware that tries to infer the transition. Note also that this runs before
/// <c>UseAuthentication</c>, so it cannot see <c>context.User</c> even if it wanted to.
/// </remarks>
public class AnonymousChatMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AnonymousChatSession session)
    {
        var chatId = ChatIdentityCookie.TryRead(context) ?? ChatIdentityCookie.Mint(context);
        session.Initialize(chatId);
        await next(context);
    }
}
