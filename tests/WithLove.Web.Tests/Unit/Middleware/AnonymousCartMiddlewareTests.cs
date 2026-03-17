using Microsoft.AspNetCore.Http;
using WithLove.Web.Middleware;

namespace WithLove.Web.Tests.Unit.Middleware;

public class AnonymousCartMiddlewareTests
{
    private readonly AnonymousCartSession _session = new();
    private bool _nextCalled;

    private AnonymousCartMiddleware CreateMiddleware()
    {
        return new AnonymousCartMiddleware(_ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        });
    }

    private static DefaultHttpContext CreateHttpContext(string? cookieValue = null)
    {
        var context = new DefaultHttpContext();
        if (cookieValue is not null)
        {
            context.Request.Headers.Cookie = $"wl-cart-id={cookieValue}";
        }
        return context;
    }

    #region Existing Cookie

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task ExistingCookie_UsesItsValue()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("existing-cart-id");

        await middleware.InvokeAsync(context, _session);

        _session.CartId.Should().Be("existing-cart-id");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task ExistingCookie_DoesNotSetResponseCookie()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("existing-cart-id");

        await middleware.InvokeAsync(context, _session);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task ExistingCookie_CallsNext()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("existing-cart-id");

        await middleware.InvokeAsync(context, _session);

        _nextCalled.Should().BeTrue();
    }

    #endregion

    #region No Cookie

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_GeneratesNewCartId()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        _session.CartId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_CartIdIsValidGuidFormat()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        // ToString("N") produces 32 hex chars with no dashes
        _session.CartId.Should().HaveLength(32);
        _session.CartId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_SetsResponseCookie()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("wl-cart-id=");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_CookieIsHttpOnly()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("httponly", Exactly.Once());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_CookieIsSameSiteLax()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("samesite=lax", Exactly.Once());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_CookieHasExpiry()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("expires=");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_CallsNext()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        _nextCalled.Should().BeTrue();
    }

    #endregion

    #region Session Initialization

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task NoCookie_SessionCartIdMatchesResponseCookie()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context, _session);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain($"wl-cart-id={_session.CartId}");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Middleware)]
    public async Task MultipleCalls_GenerateDifferentIds()
    {
        var middleware = CreateMiddleware();
        var session1 = new AnonymousCartSession();
        var session2 = new AnonymousCartSession();

        await middleware.InvokeAsync(CreateHttpContext(), session1);
        await middleware.InvokeAsync(CreateHttpContext(), session2);

        session1.CartId.Should().NotBe(session2.CartId);
    }

    #endregion
}
