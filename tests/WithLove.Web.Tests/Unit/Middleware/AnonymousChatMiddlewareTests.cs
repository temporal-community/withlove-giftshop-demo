using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using WithLove.Web.Middleware;
using WithLove.Web.Tests.Fakes;

namespace WithLove.Web.Tests.Unit.Middleware;

/// <summary>
/// Guards the <c>wl-chat-id</c> cookie contract.
/// </summary>
/// <remarks>
/// The four <see cref="CookieOptions"/> values asserted here are decisions, not incidentals: a two
/// hour lifetime, <c>HttpOnly</c>, <c>SameSite=Lax</c>, and <c>Secure</c>. Each one is invisible at
/// runtime until it is wrong, and each one fails silently when it is — a <c>Strict</c> cookie
/// destroys the session on every inbound link, a longer lifetime quietly turns the cookie into a
/// visitor identifier, and omitting <c>Secure</c> exposes the transcript capability on plaintext
/// requests. These tests keep that contract from drifting.
/// </remarks>
public class AnonymousChatMiddlewareTests
{
    /// <summary>A value with the exact shape <c>Guid.ToString("N")</c> produces.</summary>
    private const string WellFormedChatId = "0123456789abcdef0123456789abcdef";

    private readonly AnonymousChatSession _session = new();
    private bool _nextCalled;

    private AnonymousChatMiddleware CreateMiddleware() =>
        new(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });

    private static DefaultHttpContext CreateHttpContext(string? cookieValue = null)
    {
        var context = new DefaultHttpContext();
        if (cookieValue is not null)
            context.Request.Headers.Cookie = $"{ChatIdentityCookie.CookieName}={cookieValue}";

        return context;
    }

    #region Cookie options — the design decisions, asserted as written

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_AppendsExactlyTheCookieOptionsTheDesignSpecifies()
    {
        var cookies = new CapturingResponseCookies();
        var context = CreateHttpContext();
        context.Features.Set<IResponseCookiesFeature>(new CapturingResponseCookiesFeature(cookies));
        var expectedExpiry = DateTimeOffset.UtcNow.AddHours(2);

        await CreateMiddleware().InvokeAsync(context, _session);

        var appended = cookies.Appended.Should().ContainSingle().Subject;
        appended.Name.Should().Be("wl-chat-id");
        appended.Value.Should().Be(_session.ChatId);

        // Nothing client-side reads this, so HttpOnly costs nothing — and it is precisely why End
        // Chat cannot rotate the cookie from inside a SignalR circuit.
        appended.Options.HttpOnly.Should().BeTrue();

        // Load-bearing. Under Strict, a visitor arriving from an email link or a search result
        // sends no cookie on the top-level navigation, the middleware sees "absent" and mints a
        // fresh identity — silently destroying the session on every inbound link.
        appended.Options.SameSite.Should().Be(SameSiteMode.Lax);

        // Two hours, not the 24 the design first proposed: the user weighted the shared-machine
        // window above the pedagogical symmetry with the workflow's own run lifetime.
        appended.Options.Expires.Should().NotBeNull();
        appended.Options.Expires!.Value.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));

        // Honest: remove chat and the shop still works, so this is not essential the way the cart
        // cookie is. Inert today because UseCookiePolicy is not registered — the safety net for a
        // future consent gate is ChatService's throw-on-empty, not a false claim here.
        appended.Options.IsEssential.Should().BeFalse();

        // The cookie is a bearer capability for the anonymous transcript and must not travel over
        // plaintext HTTP. The app redirects to HTTPS before the middleware runs.
        appended.Options.Secure.Should().BeTrue();

        // Expires alone, never Max-Age as well: one absolute lifetime, one place to change it.
        appended.Options.MaxAge.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_WritesOneSecureSetCookieHeader()
    {
        var context = CreateHttpContext();

        await CreateMiddleware().InvokeAsync(context, _session);

        // Asserted on the wire as well as on the options object, because this is what the browser
        // actually receives.
        var setCookie = context.Response.Headers.SetCookie;
        setCookie.Should().ContainSingle();
        var header = setCookie.ToString();
        header.Should().Contain($"wl-chat-id={_session.ChatId}");
        header.Should().Contain("expires=", Exactly.Once());
        header.Should().Contain("httponly", Exactly.Once());
        header.Should().Contain("samesite=lax", Exactly.Once());
        header.Should().Contain("secure", Exactly.Once());
        header.Should().NotContain("max-age");
    }

    #endregion

    #region Minting when the cookie is absent

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_PopulatesTheSessionWithAFreshOpaqueIdentity()
    {
        var context = CreateHttpContext();

        await CreateMiddleware().InvokeAsync(context, _session);

        _session.ChatId.Should().MatchRegex("^[0-9a-f]{32}$");
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task TwoVisitorsWithoutCookies_GetDifferentIdentities()
    {
        var middleware = CreateMiddleware();
        var first = new AnonymousChatSession();
        var second = new AnonymousChatSession();

        await middleware.InvokeAsync(CreateHttpContext(), first);
        await middleware.InvokeAsync(CreateHttpContext(), second);

        first.ChatId.Should().NotBe(second.ChatId);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task AMintedIdentity_IsAcceptedOnTheNextRequest()
    {
        // Minting and validating are two halves of one contract and they live in two methods. If
        // they ever disagree — a stricter validator, a different Guid format — every visitor gets
        // re-minted on every request and stable identity is silently gone with no other symptom.
        var middleware = CreateMiddleware();
        var firstRequest = CreateHttpContext();
        await middleware.InvokeAsync(firstRequest, _session);
        var minted = _session.ChatId;

        var secondRequest = CreateHttpContext(minted);
        var resumed = new AnonymousChatSession();
        await middleware.InvokeAsync(secondRequest, resumed);

        resumed.ChatId.Should().Be(minted);
        secondRequest.Response.Headers.SetCookie.Should().BeEmpty();
    }

    #endregion

    #region Reusing a well-formed cookie

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task WellFormedCookie_IsReusedAndNoSetCookieIsWritten()
    {
        var context = CreateHttpContext(WellFormedChatId);

        await CreateMiddleware().InvokeAsync(context, _session);

        _session.ChatId.Should().Be(WellFormedChatId);
        context.Response.Headers.SetCookie.Should().BeEmpty();
        _nextCalled.Should().BeTrue();
    }

    #endregion

    #region Rejecting attacker-controlled shapes

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]     // 31 characters
    [InlineData("0123456789abcdef0123456789abcdef0")]   // 33 characters
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]    // uppercase hex
    [InlineData("0123456789abcdef0123456789abcdeg")]    // one non-hex character
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]    // not hex at all
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")] // dashed Guid
    [InlineData("giftshop-chat-anon-injected-value")]   // an attempt to steer the workflow ID
    public async Task MalformedCookie_IsTreatedAsAbsentAndReminted(string malformed)
    {
        // This value is concatenated into a workflow ID. Accepting only the exact shape this app
        // mints turns "attacker-controlled string spliced into a workflow ID" into
        // "attacker-controlled opaque 128-bit token". It does not defend against replaying a
        // cookie value learned from someone else; only entropy does that.
        var context = CreateHttpContext(malformed);

        await CreateMiddleware().InvokeAsync(context, _session);

        _session.ChatId.Should().NotBe(malformed);
        _session.ChatId.Should().MatchRegex("^[0-9a-f]{32}$");
        context.Response.Headers.SetCookie.ToString()
            .Should().Contain($"wl-chat-id={_session.ChatId}");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task OversizedCookie_IsTreatedAsAbsentAndReminted()
    {
        var oversized = new string('a', 4096);
        var context = CreateHttpContext(oversized);

        await CreateMiddleware().InvokeAsync(context, _session);

        _session.ChatId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    #endregion

}
