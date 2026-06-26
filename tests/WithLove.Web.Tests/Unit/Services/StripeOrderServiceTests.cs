using Microsoft.AspNetCore.Identity;
using Stripe;
using Stripe.Checkout;
using WithLove.Data.Models;
using WithLove.Workflows.Activities;
using ZiggyCreatures.Caching.Fusion;

namespace WithLove.Web.Tests.Unit.Services;

public class StripeOrderServiceTests
{
    private const string UserId = "user-123";
    private const string StripeCustomerId = "cus_test123";

    private readonly SessionService _sessions = A.Fake<SessionService>();
    private readonly SessionLineItemService _lineItems = A.Fake<SessionLineItemService>();
    private readonly IFusionCache _cache = A.Fake<IFusionCache>();
    private readonly IProductService _products = A.Fake<IProductService>();
    private readonly UserManager<ShopUser> _userManager;

    public StripeOrderServiceTests()
    {
        _userManager = A.Fake<UserManager<ShopUser>>(
            o => o.WithArgumentsForConstructor(
                new object[] { A.Fake<IUserStore<ShopUser>>(), null!, null!, null!, null!, null!, null!, null!, null! }));

        // Wire LineItems virtual property on SessionService
        A.CallTo(() => _sessions.LineItems).Returns(_lineItems);

        // Default: cache miss
        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>._,
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(default(string)));

        // Default: SetAsync is a no-op
        A.CallTo(() => _cache.SetAsync(
                A<string>._,
                A<string>._,
                A<FusionCacheEntryOptions>._,
                A<IEnumerable<string>?>._,
                A<CancellationToken>._))
            .Returns(ValueTask.CompletedTask);

        // Default: no products in catalog
        A.CallTo(() => _products.GetProductsAsync())
            .Returns(new List<WithLove.Web.Models.Product>());
    }

    private StripeOrderService CreateSut() => new(_sessions, _userManager, _products, _cache);

    private static ShopUser MakeUser(string stripeCustomerId = StripeCustomerId) =>
        new() { Id = UserId, StripeCustomerId = stripeCustomerId };

    private static Session MakeSession(string id, string paymentStatus = "paid", long? amountTotal = 2500) =>
        new()
        {
            Id = id,
            PaymentStatus = paymentStatus,
            Created = DateTime.UtcNow,
            AmountTotal = amountTotal,
            CustomerId = StripeCustomerId
        };

    private static LineItem MakeLineItem(string id, long amountTotal, long quantity, string priceId = "price_ABC") =>
        new()
        {
            Id = id,
            AmountTotal = amountTotal,
            Quantity = quantity,
            Description = "Test Product",
            Price = new Price { Id = priceId }
        };

    private static StripeList<Session> MakeSessionPage(IList<Session> sessions, bool hasMore = false) =>
        new() { Data = new List<Session>(sessions), HasMore = hasMore };

    private static StripeList<LineItem> MakeLineItemPage(IList<LineItem> items, bool hasMore = false) =>
        new() { Data = new List<LineItem>(items), HasMore = hasMore };

    #region GetOrdersAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrdersAsync_ReturnsEmptyPage_WhenUserNotFound()
    {
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(default(ShopUser));
        var sut = CreateSut();

        var result = await sut.GetOrdersAsync(UserId);

        result.Orders.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrdersAsync_ReturnsEmptyPage_WhenStripeCustomerIdIsEmpty()
    {
        A.CallTo(() => _userManager.FindByIdAsync(UserId))
            .Returns(new ShopUser { Id = UserId, StripeCustomerId = "" });
        var sut = CreateSut();

        var result = await sut.GetOrdersAsync(UserId);

        result.Orders.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrdersAsync_FiltersOutUnpaidSessions()
    {
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var sessions = new List<Session>
        {
            MakeSession("cs_paid_001", "paid"),
            MakeSession("cs_paid_002", "paid"),
            MakeSession("cs_unpaid_001", "unpaid")
        };

        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(sessions));

        var sut = CreateSut();

        var result = await sut.GetOrdersAsync(UserId);

        result.Orders.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrdersAsync_PropagatesHasMoreAndNextCursor()
    {
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var sessions = new List<Session>
        {
            MakeSession("cs_first_paid", "paid"),
            MakeSession("cs_last", "paid")
        };

        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(sessions, hasMore: true));

        var sut = CreateSut();

        var result = await sut.GetOrdersAsync(UserId);

        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().Be("cs_last");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrdersAsync_WarmsCacheForEachPaidSession()
    {
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        const string sessionId1 = "cs_test0001234";
        const string sessionId2 = "cs_test0009999";
        var confirmNum1 = OrderInfo.GenerateConfirmationNumber(sessionId1);
        var confirmNum2 = OrderInfo.GenerateConfirmationNumber(sessionId2);
        var expectedKey1 = $"order:confirm:{UserId}:{confirmNum1}";
        var expectedKey2 = $"order:confirm:{UserId}:{confirmNum2}";

        var sessions = new List<Session>
        {
            MakeSession(sessionId1, "paid"),
            MakeSession(sessionId2, "paid")
        };

        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(sessions));

        var sut = CreateSut();

        await sut.GetOrdersAsync(UserId);

        A.CallTo(() => _cache.SetAsync(
                A<string>.That.Matches(k => k == expectedKey1),
                A<string>.That.Matches(v => v == sessionId1),
                A<FusionCacheEntryOptions>._,
                A<IEnumerable<string>?>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => _cache.SetAsync(
                A<string>.That.Matches(k => k == expectedKey2),
                A<string>.That.Matches(v => v == sessionId2),
                A<FusionCacheEntryOptions>._,
                A<IEnumerable<string>?>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    #endregion

    #region GetOrderAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_ReturnsOrder_OnCacheHit()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        // Cache hit — return the sessionId
        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var lineItem = MakeLineItem("li_test001", 500L, 2);
        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { lineItem }));

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        result!.ConfirmationNumber.Should().Be(confirmationNumber);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_FallsBackToStripeScan_OnCacheMiss()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        // Cache miss (default setup returns null)
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(new List<Session> { session }));

        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { MakeLineItem("li_001", 500L, 1) }));

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_BackfillsCacheAfterScan()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);
        var expectedKey = $"order:confirm:{UserId}:{confirmationNumber}";

        // Cache miss (default setup returns null)
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(new List<Session> { session }));

        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { MakeLineItem("li_001", 500L, 1) }));

        var sut = CreateSut();

        await sut.GetOrderAsync(UserId, confirmationNumber);

        A.CallTo(() => _cache.SetAsync(
                A<string>.That.Matches(k => k == expectedKey),
                A<string>.That.Matches(v => v == sessionId),
                A<FusionCacheEntryOptions>._,
                A<IEnumerable<string>?>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_ReturnsNull_OnOwnershipMismatch()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        // Cache hit
        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        // Session belongs to a different customer
        var session = new Session
        {
            Id = sessionId,
            PaymentStatus = "paid",
            Created = DateTime.UtcNow,
            AmountTotal = 2500L,
            CustomerId = "cus_other_customer"
        };

        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        // User's StripeCustomerId is cus_test123 — does not match cus_other_customer
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_ReturnsNull_WhenConfirmationNumberAbsent()
    {
        const string requestedConfirmNumber = "WL-UNKNOWN";

        // Cache miss (default returns null)
        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        // Scan returns a session whose confirmation number does NOT match
        var nonMatchingSession = MakeSession("cs_other_session_id");
        A.CallTo(() => _sessions.ListAsync(
                A<SessionListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeSessionPage(new List<Session> { nonMatchingSession }, hasMore: false));

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, requestedConfirmNumber);

        result.Should().BeNull();
        A.CallTo(() => _sessions.GetAsync(
                A<string>._,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task GetOrderAsync_PaginatesLineItemsCompletely()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        // Cache hit
        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        // Page 1: 2 items, HasMore = true, last item id = "li_page1last"
        var page1Items = new List<LineItem>
        {
            MakeLineItem("li_page1_item1", 500L, 1, "price_A"),
            MakeLineItem("li_page1last", 750L, 1, "price_B")
        };

        // Page 2: 1 item, HasMore = false
        var page2Items = new List<LineItem>
        {
            MakeLineItem("li_page2_item1", 1000L, 2, "price_C")
        };

        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>.That.Matches(o => o.StartingAfter == null),
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(page1Items, hasMore: true));

        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>.That.Matches(o => o.StartingAfter == "li_page1last"),
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(page2Items, hasMore: false));

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        result!.LineItems.Should().HaveCount(3);
    }

    #endregion

    #region Mapping tests

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task MapToDetail_UsesStripeAmountTotal_AsUnitPrice()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        // AmountTotal = 500 cents, Quantity = 2 => UnitPrice = 500 / 100 / 2 = 2.50
        var lineItem = MakeLineItem("li_001", amountTotal: 500L, quantity: 2, priceId: "price_ABC");
        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { lineItem }));

        // Catalog has a product with a different current price — must NOT be used
        A.CallTo(() => _products.GetProductsAsync())
            .Returns(new List<WithLove.Web.Models.Product>
            {
                new() { Id = 1, Name = "Rose Box", ImageUrl = "https://img.test/rose.jpg", StripePriceId = "price_ABC", Price = 29.99m }
            });

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        result!.LineItems.Should().HaveCount(1);
        result.LineItems[0].UnitPrice.Should().Be(2.50m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task MapToDetail_EnrichesProductNameAndImageUrl_WhenStripePriceMatchesProduct()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var lineItem = MakeLineItem("li_001", 2999L, 1, "price_ABC");
        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { lineItem }));

        A.CallTo(() => _products.GetProductsAsync())
            .Returns(new List<WithLove.Web.Models.Product>
            {
                new() { Id = 42, Name = "Rose Box", ImageUrl = "https://img.test/rose.jpg", StripePriceId = "price_ABC", Price = 29.99m }
            });

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        var item = result!.LineItems[0];
        item.ProductName.Should().Be("Rose Box");
        item.ImageUrl.Should().Be("https://img.test/rose.jpg");
        item.IsEnhancement.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task MapToDetail_EnrichesProductByName_WhenStripePriceDoesNotMatch()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        var lineItems = new StripeList<LineItem>
        {
            Data = [
                new LineItem
                {
                    Id = "li_123",
                    AmountTotal = 500,
                    Quantity = 2,
                    Description = "Rose Box", // This matches product name
                    Price = new Price { Id = "price_OLD" } // Old price not in catalog
                }
            ]
        };

        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(lineItems);

        A.CallTo(() => _products.GetProductsAsync())
            .Returns(new List<WithLove.Web.Models.Product>
            {
                new() { Id = 42, Name = "Rose Box", ImageUrl = "https://img.test/rose.jpg", StripePriceId = "price_NEW", Price = 29.99m }
            });

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        var item = result!.LineItems[0];
        item.ProductId.Should().Be(42);
        item.ProductName.Should().Be("Rose Box");
        item.ImageUrl.Should().Be("https://img.test/rose.jpg");
        item.IsEnhancement.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Orders)]
    public async Task MapToDetail_MarksLineItemAsEnhancement_WhenNoCatalogMatch()
    {
        const string sessionId = "cs_test_abc1234567";
        var confirmationNumber = OrderInfo.GenerateConfirmationNumber(sessionId);

        A.CallTo(() => _cache.GetOrDefaultAsync<string>(
                A<string>.That.Matches(k => k.Contains(confirmationNumber)),
                A<string>._,
                A<FusionCacheEntryOptions?>._,
                A<CancellationToken>._))
            .Returns(new ValueTask<string?>(sessionId));

        var session = MakeSession(sessionId);
        A.CallTo(() => _sessions.GetAsync(
                sessionId,
                A<SessionGetOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(session);

        A.CallTo(() => _userManager.FindByIdAsync(UserId)).Returns(MakeUser());

        // Line item references a price id that does NOT exist in the product catalog
        var lineItem = MakeLineItem("li_001", 500L, 1, "price_UNKNOWN");
        A.CallTo(() => _lineItems.ListAsync(
                sessionId,
                A<SessionLineItemListOptions>._,
                A<RequestOptions>._,
                A<CancellationToken>._))
            .Returns(MakeLineItemPage(new List<LineItem> { lineItem }));

        // Catalog has a product but with a different price id
        A.CallTo(() => _products.GetProductsAsync())
            .Returns(new List<WithLove.Web.Models.Product>
            {
                new() { Id = 1, Name = "Rose Box", ImageUrl = "https://img.test/rose.jpg", StripePriceId = "price_ABC", Price = 29.99m }
            });

        var sut = CreateSut();

        var result = await sut.GetOrderAsync(UserId, confirmationNumber);

        result.Should().NotBeNull();
        var item = result!.LineItems[0];
        item.IsEnhancement.Should().BeTrue();
        item.ProductId.Should().BeNull();
    }

    #endregion
}
