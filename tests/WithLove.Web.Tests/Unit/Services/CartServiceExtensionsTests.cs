using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace WithLove.Web.Tests.Unit.Services;

public class CartServiceExtensionsTests
{
    private readonly ICartService _cartService;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly AnonymousCartSession _anonSession;

    public CartServiceExtensionsTests()
    {
        _cartService = A.Fake<ICartService>();
        _authStateProvider = A.Fake<AuthenticationStateProvider>();
        _anonSession = new AnonymousCartSession();
    }

    private void SetupAuthenticatedUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(principal);
        A.CallTo(() => _authStateProvider.GetAuthenticationStateAsync())
            .Returns(Task.FromResult(authState));
    }

    private void SetupAnonymousUser()
    {
        var identity = new ClaimsIdentity(); // no auth type = unauthenticated
        var principal = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(principal);
        A.CallTo(() => _authStateProvider.GetAuthenticationStateAsync())
            .Returns(Task.FromResult(authState));
    }

    #region Authenticated User

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AuthenticatedUser_UsesUserIdAsPrimary()
    {
        SetupAuthenticatedUser("user-42");
        _anonSession.Initialize("anon-abc");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        A.CallTo(() => _cartService.InitializeAsync("user-42", "anon-abc"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AuthenticatedUser_PassesAnonIdForMerge()
    {
        SetupAuthenticatedUser("user-1");
        _anonSession.Initialize("anon-xyz");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // Second parameter should be the anonymous cart ID for merge
        A.CallTo(() => _cartService.InitializeAsync("user-1", "anon-xyz"))
            .MustHaveHappenedOnceExactly();
    }

    #endregion

    #region Anonymous User

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AnonymousUser_UsesAnonSessionCartId()
    {
        SetupAnonymousUser();
        _anonSession.Initialize("anon-session-id");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        A.CallTo(() => _cartService.InitializeAsync("anon-session-id", null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AnonymousUser_DoesNotPassMergeId()
    {
        SetupAnonymousUser();
        _anonSession.Initialize("anon-123");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // anonymousCartId parameter should be null (no merge for anon users)
        A.CallTo(() => _cartService.InitializeAsync(A<string>._, A<string>.That.IsNotNull()))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AnonymousUser_EmptyAnonSession_GeneratesFallbackId()
    {
        SetupAnonymousUser();
        // Don't call _anonSession.Initialize — simulates interactive circuit scope

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // Should still call InitializeAsync with a non-empty generated ID
        A.CallTo(() => _cartService.InitializeAsync(
                A<string>.That.Matches(s => !string.IsNullOrEmpty(s)), null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AuthenticatedUser_EmptyAnonSession_StillPassesFallbackForMerge()
    {
        SetupAuthenticatedUser("user-99");
        // Don't call _anonSession.Initialize — simulates interactive circuit scope

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // Primary ID should be the authenticated user, merge ID should be a generated fallback
        A.CallTo(() => _cartService.InitializeAsync(
                "user-99",
                A<string>.That.Matches(s => !string.IsNullOrEmpty(s))))
            .MustHaveHappenedOnceExactly();
    }

    #endregion

    #region Consistent Behavior

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_AlwaysCallsInitializeExactlyOnce()
    {
        SetupAnonymousUser();
        _anonSession.Initialize("anon-1");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        A.CallTo(() => _cartService.InitializeAsync(A<string>._, A<string?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_SameAnonId_UsedAcrossMultipleCalls()
    {
        SetupAnonymousUser();
        _anonSession.Initialize("stable-anon-id");

        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);
        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // Both calls should use the same stable anonymous ID
        A.CallTo(() => _cartService.InitializeAsync("stable-anon-id", null))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeFromAuthAsync_DifferentUsers_GetDifferentPrimaryIds()
    {
        _anonSession.Initialize("anon-shared");

        // First call: authenticated
        SetupAuthenticatedUser("user-A");
        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        // Second call: anonymous
        SetupAnonymousUser();
        await _cartService.InitializeFromAuthAsync(_authStateProvider, _anonSession);

        A.CallTo(() => _cartService.InitializeAsync("user-A", "anon-shared"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _cartService.InitializeAsync("anon-shared", null))
            .MustHaveHappenedOnceExactly();
    }

    #endregion
}
