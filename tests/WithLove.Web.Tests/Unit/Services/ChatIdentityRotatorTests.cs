using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Temporalio.Exceptions;
using WithLove.Web.Middleware;
using WithLove.Web.Tests.Fakes;

namespace WithLove.Web.Tests.Unit.Services;

/// <summary>
/// Guards the two rotation points — login and logout — and the rule that neither of them may fail
/// because the chat backend is unavailable.
/// </summary>
/// <remarks>
/// <para>
/// End Chat deliberately has no test here because it deliberately does not rotate: the cookie is
/// <c>HttpOnly</c> and End Chat runs inside a SignalR circuit with no <c>HttpResponse</c> to write
/// to. Its contract is covered by <c>ChatServiceTests</c> instead.
/// </para>
/// <para>
/// Two things these tests deliberately stop short of. First, whether a browser withholds a
/// <c>SameSite=Strict</c> cookie on a cross-site top-level navigation: the attribute this
/// application emits is assertable, the user agent's send decision is not, so only the former is
/// asserted. Second, what a browser does when one response carries two <c>Set-Cookie</c> headers
/// for the same name: also a statement about user agents, and one this application no longer has to
/// ask, because <c>ChatIdentityCookie.Mint</c> is idempotent per request. What is reachable
/// in-process is that the response carries exactly one identity and that the session holds it,
/// which is what
/// <see cref="MiddlewareMintThenRotation_WritesOneIdentityAndTheSessionHoldsIt"/> asserts.
/// </para>
/// </remarks>
public class ChatIdentityRotatorTests
{
    private const string PreviousChatId = "0123456789abcdef0123456789abcdef";
    private const string PreviousWorkflowId = "giftshop-chat-anon-" + PreviousChatId;

    private readonly IGiftShopChatWorkflowClient _workflowClient =
        A.Fake<IGiftShopChatWorkflowClient>();

    private ChatIdentityRotator CreateRotator() =>
        new(_workflowClient, A.Fake<ILogger<ChatIdentityRotator>>());

    private static DefaultHttpContext CreateHttpContext(string? chatCookie = null)
    {
        var context = new DefaultHttpContext();
        if (chatCookie is not null)
            context.Request.Headers.Cookie = $"{ChatIdentityCookie.CookieName}={chatCookie}";

        return context;
    }

    private static string ReadRotatedChatId(HttpContext context)
    {
        var header = context.Response.Headers.SetCookie.ToString();
        var start = header.IndexOf($"{ChatIdentityCookie.CookieName}=", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the identity must be rotated");
        start += ChatIdentityCookie.CookieName.Length + 1;
        var end = header.IndexOf(';', start);
        return end < 0 ? header[start..] : header[start..end];
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_ShutsDownTheSessionTheRequestCarried_NotTheOneItMints()
    {
        // Rotation replaces one identity with another, and only one of the two has a run that
        // needs stopping. Shut down the freshly minted one and the visitor's actual session stays
        // open and queryable for the rest of its lifetime while a workflow that was never started
        // gets the signal.
        //
        // Note this is deliberately *not* a test of statement ordering, despite the read-then-mint
        // shape of the implementation. Verified by mutation: swapping those two statements does
        // not change the outcome, because HttpRequest.Cookies is parsed from the request header
        // and is untouched by HttpResponse.Cookies.Append — so TryRead still returns the incoming
        // value even after Mint has run. The ordering is defensive, not load-bearing, and a test
        // that claimed otherwise would be asserting a hazard that does not exist.
        var context = CreateHttpContext(PreviousChatId);

        await CreateRotator().RotateAsync(context);

        A.CallTo(() => _workflowClient.ShutdownAsync(PreviousWorkflowId, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        var rotated = ReadRotatedChatId(context);
        rotated.Should().NotBe(PreviousChatId);
        rotated.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_BoundsTheShutdownWithACancellableBudget()
    {
        var context = CreateHttpContext(PreviousChatId);
        CancellationToken budget = default;
        A.CallTo(() => _workflowClient.ShutdownAsync(A<string>._, A<CancellationToken>._))
            .Invokes((string _, CancellationToken token) => budget = token);

        await CreateRotator().RotateAsync(context);

        // CancellationToken.None here would mean an unreachable Temporal blocks the logout for as
        // long as the RPC takes to give up, which is the failure this budget exists to prevent.
        budget.CanBeCanceled.Should().BeTrue();
        budget.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_WithNoPreviousIdentity_MintsWithoutShuttingAnythingDown()
    {
        var context = CreateHttpContext();

        await CreateRotator().RotateAsync(context);

        ReadRotatedChatId(context).Should().MatchRegex("^[0-9a-f]{32}$");
        Fake.GetCalls(_workflowClient).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_WithAMalformedPreviousCookie_DerivesNoWorkflowIdFromIt()
    {
        // A workflow ID is never built from an unvalidated cookie value, not even on the path
        // whose only job is to throw that value away.
        var context = CreateHttpContext("../../etc/passwd");

        await CreateRotator().RotateAsync(context);

        ReadRotatedChatId(context).Should().MatchRegex("^[0-9a-f]{32}$");
        Fake.GetCalls(_workflowClient).Should().BeEmpty();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    [InlineData("unavailable")]
    [InlineData("not-found")]
    [InlineData("cancelled")]
    [InlineData("unexpected")]
    public async Task Rotate_StillRotatesWhenTheChatBackendFails(string failure)
    {
        // A logout must never fail because the AI chat backend is unavailable, and a login must
        // never fail for it either. An orphaned workflow that reaps itself is a far better outcome
        // than a customer who cannot sign out — so every one of these is logged and swallowed.
        Exception thrown = failure switch
        {
            "unavailable" => new RpcException(
                RpcException.StatusCode.Unavailable,
                "Temporal is down",
                null),
            "not-found" => new RpcException(
                RpcException.StatusCode.NotFound,
                "workflow not found",
                null),
            "cancelled" => new OperationCanceledException(),
            _ => new InvalidOperationException("something nobody predicted"),
        };
        var context = CreateHttpContext(PreviousChatId);
        A.CallTo(() => _workflowClient.ShutdownAsync(A<string>._, A<CancellationToken>._))
            .ThrowsAsync(thrown);

        var rotate = () => CreateRotator().RotateAsync(context);

        await rotate.Should().NotThrowAsync();
        ReadRotatedChatId(context).Should().NotBe(PreviousChatId);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_StillCompletesWhenTheShutdownHangsPastItsBudget()
    {
        // Swallowing the exception is only half of "a logout never fails on chat". The other half
        // is that it never *stalls* either, which only the real budget can demonstrate: this test
        // hangs the shutdown forever and relies on the two second CancellationTokenSource to end
        // it. A budget widened to minutes, or dropped, turns this red.
        var context = CreateHttpContext(PreviousChatId);
        A.CallTo(() => _workflowClient.ShutdownAsync(A<string>._, A<CancellationToken>._))
            .ReturnsLazily((string _, CancellationToken token) =>
                Task.Delay(Timeout.InfiniteTimeSpan, token));

        var rotate = Task.Run(() => CreateRotator().RotateAsync(context));

        // Raced against a deadline rather than simply awaited, so a widened or removed budget
        // fails this test in ten seconds instead of hanging the suite for as long as the mutation
        // says to wait.
        var finished = await Task.WhenAny(rotate, Task.Delay(TimeSpan.FromSeconds(10)));
        finished.Should().BeSameAs(rotate, "the shutdown budget must bound the logout");
        await rotate;
        ReadRotatedChatId(context).Should().NotBe(PreviousChatId);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_WhenTheResponseHasAlreadyStarted_ChangesNothing()
    {
        // A cookie can only be written while the response headers are still ours. Adding
        // [StreamRendering] to the login page would quietly stop satisfying that, and the correct
        // behaviour is to log loudly and leave both the cookie and the session alone rather than
        // shut down a run whose replacement identity was never issued.
        var context = CreateStartedResponseContext(PreviousChatId);

        await CreateRotator().RotateAsync(context);

        context.Response.Headers.SetCookie.Should().BeEmpty();
        Fake.GetCalls(_workflowClient).Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task Rotate_WritesTheCookieWithExactlyTheOptionsTheDesignSpecifies()
    {
        // The rotation path writes wl-chat-id just as AnonymousChatMiddleware does, and until now
        // nothing asserted its shape — only the value it carried. Both paths go through
        // ChatIdentityCookie.Mint today, so this is a guard against a future rotation that stops
        // doing so and quietly issues a cookie with different flags at the one moment the identity
        // changes.
        var cookies = new CapturingResponseCookies();
        var context = CreateHttpContext(PreviousChatId);
        context.Features.Set<IResponseCookiesFeature>(new CapturingResponseCookiesFeature(cookies));
        var expectedExpiry = DateTimeOffset.UtcNow.AddHours(2);

        await CreateRotator().RotateAsync(context);

        // One Append and no Delete. The capturing double throws on Delete, so a rotation
        // reimplemented as delete-then-append fails here rather than being silently accepted.
        var appended = cookies.Appended.Should().ContainSingle().Subject;
        appended.Name.Should().Be(ChatIdentityCookie.CookieName);
        appended.Value.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(PreviousChatId);

        // Lax and not Strict. What is assertable is the attribute this application emits; whether a
        // Strict cookie would then be withheld by a browser on a cross-site top-level navigation is
        // a statement about user agents and is not reachable from a test at this level. See the
        // class remarks.
        appended.Options.SameSite.Should().Be(SameSiteMode.Lax);
        appended.Options.HttpOnly.Should().BeTrue();
        appended.Options.Secure.Should().BeTrue();
        appended.Options.IsEssential.Should().BeFalse();
        appended.Options.MaxAge.Should().BeNull();
        appended.Options.Expires.Should().NotBeNull();
        appended.Options.Expires!.Value.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task MiddlewareMintThenRotation_WritesOneIdentityAndTheSessionHoldsIt()
    {
        // The real pipeline shape for a login or logout that arrives without a usable chat cookie —
        // one whose two hour lifetime has run out, say. AnonymousChatMiddleware runs first and mints
        // because TryRead saw nothing; the endpoint then rotates. This once produced two
        // Set-Cookie: wl-chat-id headers on one response, leaving the browser's choice between them
        // to decide whether AnonymousChatSession still described the visitor.
        //
        // Both halves of the fix are asserted, because either one alone is satisfiable by a broken
        // implementation: exactly one identity is written (a rotation that stopped writing at all
        // would also pass a count-free check), and it is the identity the session carries into the
        // circuit. Rotation is not weakened by the deduplication — the value is minted during this
        // request and so cannot predate the authentication boundary, which is the invariant
        // rotation exists for.
        var context = CreateHttpContext();
        var session = new AnonymousChatSession();
        await new AnonymousChatMiddleware(_ => Task.CompletedTask).InvokeAsync(context, session);

        await CreateRotator().RotateAsync(context);

        var issued = ReadAllChatIds(context);
        issued.Should().ContainSingle().Which.Should().MatchRegex("^[0-9a-f]{32}$");
        issued[0].Should().Be(session.ChatId);

        // Nothing to shut down: the middleware's mint was never in the request, so TryRead returned
        // null and no workflow ID was derived from an identity that never chatted. The run behind a
        // cookie the visitor lost is unreachable by design and reaps itself on the workflow's own
        // 24 hour lifetime.
        Fake.GetCalls(_workflowClient).Should().BeEmpty();
    }

    /// <summary>
    /// Every <c>wl-chat-id</c> value the response carries, in the order it was written.
    /// </summary>
    private static List<string> ReadAllChatIds(HttpContext context)
    {
        var prefix = $"{ChatIdentityCookie.CookieName}=";
        return context.Response.Headers.SetCookie
            .Select(header => header ?? string.Empty)
            .Where(header => header.StartsWith(prefix, StringComparison.Ordinal))
            .Select(header =>
            {
                var value = header[prefix.Length..];
                var end = value.IndexOf(';');
                return end < 0 ? value : value[..end];
            })
            .ToList();
    }

    private static DefaultHttpContext CreateStartedResponseContext(string chatCookie)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Headers = new HeaderDictionary
            {
                ["Cookie"] = $"{ChatIdentityCookie.CookieName}={chatCookie}",
            },
        });
        var response = A.Fake<IHttpResponseFeature>();
        A.CallTo(() => response.HasStarted).Returns(true);
        A.CallTo(() => response.Headers).Returns(new HeaderDictionary());
        features.Set(response);
        return new DefaultHttpContext(features);
    }
}
