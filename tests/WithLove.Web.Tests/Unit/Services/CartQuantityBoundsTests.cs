using WithLove.Workflows.Chat;
using ZiggyCreatures.Caching.Fusion;

namespace WithLove.Web.Tests.Unit.Services;

/// <summary>
/// Bounds and saturating arithmetic for cart line quantities, at both the helper and production
/// service level.
/// </summary>
/// <remarks>
/// <para>
/// Cart quantity is not fully trusted input. Two of its three writers are outside the UI's control:
/// the chat assistant supplies a quantity chosen by a language model, and
/// <see cref="FusionCacheCartService"/> rehydrates carts from a cache entry with a 30-day TTL that
/// may have been written by an older build.
/// </para>
/// <para>
/// The failure was not a wrong number on a page. Unbounded <c>+=</c> on an <see cref="int"/> wraps
/// silently, and LINQ's <c>Sum</c> over <c>int</c> is <c>checked</c> — so a wrapped quantity turned
/// <c>ItemCount</c>, a property read during render and on the checkout path, into a thrown
/// <see cref="OverflowException"/> outside any exception handler.
/// </para>
/// </remarks>
public class CartQuantityBoundsTests
{
    #region CartQuantity helper

    [Theory]
    [InlineData(-5, CartQuantity.Min)]
    [InlineData(-1, CartQuantity.Min)]
    [InlineData(0, CartQuantity.Min)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(99, CartQuantity.Max)]
    [InlineData(100, CartQuantity.Max)]
    [InlineData(int.MaxValue, CartQuantity.Max)]
    [InlineData(int.MinValue, CartQuantity.Min)]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Clamp_ConstrainsToTheAllowedRange(int quantity, int expected) =>
        CartQuantity.Clamp(quantity).Should().Be(expected);

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(98, 1, CartQuantity.Max)]
    [InlineData(99, 1, CartQuantity.Max)]
    [InlineData(int.MaxValue, int.MaxValue, CartQuantity.Max)]
    [InlineData(int.MaxValue, 1, CartQuantity.Max)]
    [InlineData(1, int.MinValue, CartQuantity.Min)]
    [InlineData(5, -10, CartQuantity.Min)]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void ClampedSum_SaturatesInsteadOfWrapping(int current, int delta, int expected) =>
        // The addition widens to long before it is clamped. Clamping an already-wrapped int would
        // produce a valid-looking small number from an overflow, which is worse than throwing.
        CartQuantity.ClampedSum(current, delta).Should().Be(expected);

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void TotalItems_DoesNotThrowOnAPoisonedCart()
    {
        // The regression in one line. `items.Sum(i => i.Quantity)` over these two lines throws
        // OverflowException; TotalItems accumulates in long and clamps.
        var poisoned = new List<CartItem>
        {
            new() { ProductId = 1, Quantity = int.MaxValue },
            new() { ProductId = 2, Quantity = int.MaxValue },
        };

        var total = CartQuantity.TotalItems(poisoned);

        total.Should().Be(int.MaxValue);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void TotalItems_ClampsNegativeAccumulationToZero()
    {
        var poisoned = new List<CartItem> { new() { ProductId = 1, Quantity = -40 } };

        CartQuantity.TotalItems(poisoned).Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Sanitize_ClampsQuantitiesAndMergesDuplicateLines()
    {
        var items = new List<CartItem>
        {
            new() { ProductId = 1, Quantity = int.MaxValue },
            new() { ProductId = 1, Quantity = 5 },
            new() { ProductId = 2, Quantity = -3 },
        };

        var sanitized = CartQuantity.Sanitize(items);

        sanitized.Should().HaveCount(2);
        sanitized.Single(i => i.ProductId == 1).Quantity.Should().Be(CartQuantity.Max);
        sanitized.Single(i => i.ProductId == 2).Quantity.Should().Be(CartQuantity.Min);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Sanitize_WithNullInput_ReturnsEmptyList() =>
        CartQuantity.Sanitize(null).Should().BeEmpty();

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void MaxQuantity_MatchesTheBoundAdvertisedToTheModel() =>
        // The model is told 1..99 through the [Range] attribute in the tool schema. If the cart
        // enforced a different ceiling, a tool call the model believed was valid would be silently
        // altered, and the assistant would describe a cart that does not exist.
        CartQuantity.Max.Should().Be(GiftShopChatToolCatalog.MaxAddToCartQuantity);

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void MinQuantity_MatchesTheBoundAdvertisedToTheModel() =>
        CartQuantity.Min.Should().Be(GiftShopChatToolCatalog.MinAddToCartQuantity);

    #endregion

    #region FusionCacheCartService bounds

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_WithNegativeQuantity_ClampsToMinimum()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");

        // A model may emit any int it likes. -5 previously produced a negative line quantity and a
        // negative subtotal.
        await host.Service.AddItemAsync(Item(1, quantity: -5));

        host.Service.Items.Should().ContainSingle()
            .Which.Quantity.Should().Be(CartQuantity.Min);
        host.Service.ItemCount.Should().Be(CartQuantity.Min);
        host.Service.Subtotal.Should().BePositive();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_WithExcessiveQuantity_ClampsToMaximum()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");

        await host.Service.AddItemAsync(Item(1, quantity: int.MaxValue));

        host.Service.Items.Should().ContainSingle().Which.Quantity.Should().Be(CartQuantity.Max);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_MergingIntoAnExistingLine_Saturates()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");

        // The original overflow: `existing.Quantity += item.Quantity` wrapped to a negative number,
        // and reading ItemCount afterwards threw during render.
        await host.Service.AddItemAsync(Item(1, quantity: int.MaxValue));
        await host.Service.AddItemAsync(Item(1, quantity: int.MaxValue));

        var readItemCount = () => host.Service.ItemCount;

        readItemCount.Should().NotThrow<OverflowException>();
        host.Service.Items.Should().ContainSingle().Which.Quantity.Should().Be(CartQuantity.Max);
        host.Service.ItemCount.Should().Be(CartQuantity.Max);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_AboveMaximum_ClampsRatherThanRemoving()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");
        await host.Service.AddItemAsync(Item(1, quantity: 2));

        await host.Service.UpdateQuantityAsync(1, int.MaxValue);

        host.Service.Items.Should().ContainSingle().Which.Quantity.Should().Be(CartQuantity.Max);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_AtOrBelowZero_StillRemovesTheLine()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");
        await host.Service.AddItemAsync(Item(1, quantity: 2));

        // Clamping must not swallow the removal gesture: "set quantity to 0" is how the UI deletes
        // a line, and clamping it to 1 would make the delete button silently do nothing.
        await host.Service.UpdateQuantityAsync(1, 0);

        host.Service.Items.Should().BeEmpty();
        host.Service.ItemCount.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_Negative_StillRemovesTheLine()
    {
        await using var host = CartHost.Create();
        await host.Service.InitializeAsync("bounds-user");
        await host.Service.AddItemAsync(Item(1, quantity: 2));

        await host.Service.UpdateQuantityAsync(1, -5);

        host.Service.Items.Should().BeEmpty();
    }

    #endregion

    #region FusionCache persistence and anonymous merge

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_HealsACartPoisonedByAnEarlierBuild()
    {
        // The path bounds alone cannot fix. Persisted state has a 30-day TTL, so a cart written
        // before these bounds existed is still out there; without Sanitize on load, the first
        // render after sign-in throws.
        using var cache = new FusionCache(new FusionCacheOptions());
        await cache.SetAsync(
            "cart:poisoned-user",
            new CartState(
                [
                    new CartItem { ProductId = 1, ProductName = "A", Price = 10m, Quantity = int.MaxValue },
                    new CartItem { ProductId = 2, ProductName = "B", Price = 10m, Quantity = int.MaxValue },
                ],
                []));
        using var instrumentation = new Instrumentation();
        var service = new FusionCacheCartService(
            cache,
            A.Fake<ILogger<FusionCacheCartService>>(),
            instrumentation);

        await service.InitializeAsync("poisoned-user");

        var readItemCount = () => service.ItemCount;
        readItemCount.Should().NotThrow<OverflowException>();
        service.Items.Should().OnlyContain(item => item.Quantity == CartQuantity.Max);
        service.ItemCount.Should().Be(CartQuantity.Max * 2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_MergingAnAnonymousCart_SaturatesInsteadOfWrapping()
    {
        // Sign-in merges the anonymous cart into the user cart. Both sides may hold up to Max, so
        // this is the one code path where a legitimate merge can exceed the ceiling.
        using var cache = new FusionCache(new FusionCacheOptions());
        await cache.SetAsync(
            "cart:merge-user",
            new CartState(
                [new CartItem { ProductId = 1, ProductName = "A", Price = 10m, Quantity = 90 }],
                []));
        await cache.SetAsync(
            "cart:anon-123",
            new CartState(
                [
                    new CartItem { ProductId = 1, ProductName = "A", Price = 10m, Quantity = 90 },
                    new CartItem { ProductId = 2, ProductName = "B", Price = 10m, Quantity = int.MaxValue },
                ],
                []));
        using var instrumentation = new Instrumentation();
        var service = new FusionCacheCartService(
            cache,
            A.Fake<ILogger<FusionCacheCartService>>(),
            instrumentation);

        await service.InitializeAsync("merge-user", "anon-123");

        service.Items.Single(i => i.ProductId == 1).Quantity.Should().Be(CartQuantity.Max);
        service.Items.Single(i => i.ProductId == 2).Quantity.Should().Be(CartQuantity.Max);
        var readItemCount = () => service.ItemCount;
        readItemCount.Should().NotThrow<OverflowException>();
    }

    #endregion

    private static CartItem Item(int productId, int quantity) => new()
    {
        ProductId = productId,
        ProductName = $"Product {productId}",
        Price = 10m,
        Quantity = quantity,
    };

    /// <summary>
    /// Owns the production cart service and its disposable dependencies.
    /// </summary>
    private sealed class CartHost : IAsyncDisposable
    {
        private readonly IDisposable[] _disposables;

        private CartHost(ICartService service, params IDisposable[] disposables)
        {
            Service = service;
            _disposables = disposables;
        }

        public ICartService Service { get; }

        public static CartHost Create()
        {
            var cache = new FusionCache(new FusionCacheOptions());
            var instrumentation = new Instrumentation();
            return new CartHost(
                new FusionCacheCartService(
                    cache,
                    A.Fake<ILogger<FusionCacheCartService>>(),
                    instrumentation),
                cache,
                instrumentation);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
