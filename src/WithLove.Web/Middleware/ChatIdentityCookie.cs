namespace WithLove.Web.Middleware;

/// <summary>
/// The single definition of the <c>wl-chat-id</c> cookie: its name, its shape, and the only two
/// operations anything is allowed to perform on it.
/// </summary>
/// <remarks>
/// <para>
/// Three call sites write this cookie — <see cref="AnonymousChatMiddleware"/> mints it when absent,
/// and login and logout rotate it. Centralising the <see cref="CookieOptions"/> here means those
/// three sites cannot drift into three subtly different cookies.
/// </para>
/// <para>
/// The value is deliberately the raw identity, not a hash of one. It is concatenated into
/// <c>giftshop-chat-anon-{chatId}</c>, and keeping that derivation reversible by eye is what lets a
/// developer follow one browser session straight to its execution in the Temporal UI. Hashing would
/// add no guessing resistance — the value is already 122 bits from the platform CSPRNG — and knowing
/// the workflow ID is in any case sufficient to read the transcript via a Temporal query, so a hash
/// would protect the cookie and not the thing the cookie is for.
/// </para>
/// </remarks>
public static class ChatIdentityCookie
{
    /// <summary>Name of the cookie carrying the anonymous chat identity.</summary>
    public const string CookieName = "wl-chat-id";

    /// <summary>
    /// How long a returning visitor keeps the same anonymous chat identity.
    /// </summary>
    /// <remarks>
    /// Two hours, chosen over the workflow's own 24-hour run lifetime to keep the shared-machine
    /// window tight: on a borrowed or public browser, the next person inherits a chat identity for
    /// at most one sitting. Every realistic resume — F5, a second tab, a dropped SignalR circuit,
    /// reopening a tab — happens within minutes, so the shorter bound costs the feature nothing.
    /// <para>
    /// This is a hygiene bound on identifier lifetime and <em>not</em> a correctness mechanism. A
    /// cookie that outlives its workflow is the normal case, not an error: <c>EnsureStartedAsync</c>
    /// uses <c>IdConflictPolicy.UseExisting</c> with <c>IdReusePolicy.AllowDuplicate</c>, so a stale
    /// pointer simply starts a new run under the same ID. What keeps that honest is the
    /// <c>QueryRejectCondition.NotOpen</c> on the history query, which stops a closed run's
    /// transcript from being rendered — not this expiry.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    /// <summary>Length of a <c>Guid.ToString("N")</c> value.</summary>
    private const int ChatIdLength = 32;

    /// <summary>
    /// <see cref="HttpContext.Items"/> key under which the identity minted during this request is
    /// recorded, so a second <see cref="Mint"/> call on the same response returns it instead of
    /// appending a second <c>Set-Cookie</c>.
    /// </summary>
    private const string MintedIdentityKey = "WithLove.ChatIdentity.Minted";

    /// <summary>
    /// Returns the chat identity carried by the request, or <see langword="null"/> when the cookie
    /// is absent or does not have the exact shape this application mints.
    /// </summary>
    /// <remarks>
    /// A cookie is attacker-controlled input and this value is concatenated into a workflow ID, so
    /// anything that is not precisely 32 lowercase hex characters is treated as absent. That turns
    /// "attacker-controlled string spliced into a workflow ID" into "attacker-controlled opaque
    /// 128-bit token". It does <em>not</em> defend against an attacker who has learned another
    /// visitor's cookie value and replays it — that is ordinary session fixation, and entropy is the
    /// only defence against it.
    /// <para>
    /// This validation is a deliberate divergence from <see cref="AnonymousCartMiddleware"/>, which
    /// performs none. That is a defect in the cart path rather than a pattern worth mirroring.
    /// </para>
    /// </remarks>
    public static string? TryRead(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var chatId = context.Request.Cookies[CookieName];
        return IsWellFormed(chatId) ? chatId : null;
    }

    /// <summary>
    /// Returns the chat identity minted during this request, minting and writing one first if this
    /// is the first call on this <see cref="HttpContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent per request, and that is the whole point. <c>Response.Cookies.Append</c> adds a
    /// header rather than replacing one, so before this guard existed a request where
    /// <see cref="AnonymousChatMiddleware"/> minted and <c>ChatIdentityRotator</c> then rotated
    /// emitted two <c>Set-Cookie: wl-chat-id</c> headers — observed on <c>POST /logout</c> arriving
    /// without a well-formed cookie. Which of the two a user agent keeps is a statement about
    /// browsers that nothing in this codebase verifies, and the middleware had already handed the
    /// first value to <c>AnonymousChatSession</c>, so the circuit could be left deriving a
    /// workflow ID from an identity the browser no longer held. Recording the minted value in
    /// <see cref="HttpContext.Items"/> and returning it on any later call gives one writer per
    /// response and one value shared by session and browser.
    /// </para>
    /// <para>
    /// Deduplicating cannot weaken rotation, because the two cases cannot overlap. The rotator
    /// mints a second time only when the middleware minted first, and the middleware mints only
    /// when <see cref="TryRead"/> found nothing — the same inbound header the rotator reads. So
    /// whenever there is a previous identity to rotate away from, the rotator's <c>Mint</c> is the
    /// first of the request and issues a genuinely new value; when there is not, the value it
    /// returns is one minted moments earlier in the same request and is equally unconnected to any
    /// pre-authentication identity.
    /// </para>
    /// <para>
    /// Still a single <c>Append</c> and never a <c>Delete</c> followed by an <c>Append</c>; that
    /// much is pinned by tests.
    /// </para>
    /// </remarks>
    public static string Mint(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(MintedIdentityKey, out var alreadyMinted)
            && alreadyMinted is string existing)
        {
            return existing;
        }

        var chatId = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(CookieName, chatId, new CookieOptions
        {
            // Nothing client-side reads this. The workflow ID is derived server-side only, so
            // HttpOnly is free XSS reduction — at the cost that the cookie can be rotated only at
            // an HTTP boundary, which is why End Chat does not rotate it and login/logout do.
            HttpOnly = true,

            // Lax is the attribute emitted, and tests pin it. The reason it must not be Strict is a
            // claim about user agents that nothing in this build can check: a browser is expected to
            // withhold a Strict cookie on a top-level navigation from an email link or a search
            // result, leaving the middleware to see "absent" and mint a fresh one — silently
            // destroying the session on every inbound link.
            SameSite = SameSiteMode.Lax,

            // This cookie is a bearer capability for an anonymous transcript, so it must never be
            // sent over plaintext HTTP. Whether it is sent at all therefore depends on the scheme
            // the browser is on. Under `aspire run` the shopSite child inherits the AppHost's https
            // launch profile and is given an ASPNETCORE_HTTPS_PORT, so UseHttpsRedirection — which
            // runs ahead of AnonymousChatMiddleware — puts the browser on https and this costs
            // nothing; observed 2026-09-12 on the running shopSite, where a second request over
            // https://localhost:7260 returned this cookie rather than provoking a fresh mint. A
            // standalone `dotnet run --project src/WithLove.Web` picks the http profile instead,
            // where UseHttpsRedirection cannot determine a port and serves plaintext: there the
            // cookie survives only because localhost is a trustworthy origin, and over http from
            // any non-loopback host it is dropped and every request mints a new identity.
            Secure = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),

            // Honestly false: remove chat and the shop still works, so this cookie is not essential
            // to a functioning storefront the way wl-cart-id is. UseCookiePolicy is not registered,
            // so the flag is inert today; if a consent gate is ever added, this cookie stops being
            // written and ChatService.InitializeAsync throws immediately rather than silently
            // degrading back to a per-circuit identity.
            IsEssential = false,

        });

        // Recorded after the Append, so a failure to write cannot leave a value behind that a
        // later call would hand out as though the browser had received it.
        context.Items[MintedIdentityKey] = chatId;

        return chatId;
    }

    private static bool IsWellFormed(string? chatId)
    {
        if (chatId is not { Length: ChatIdLength })
            return false;

        foreach (var character in chatId)
        {
            var isLowerHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isLowerHex)
                return false;
        }

        return true;
    }
}
