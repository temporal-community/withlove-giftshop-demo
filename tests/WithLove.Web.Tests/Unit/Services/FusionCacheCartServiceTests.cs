using ZiggyCreatures.Caching.Fusion;

namespace WithLove.Web.Tests.Unit.Services;

public class FusionCacheCartServiceTests : IDisposable
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheCartService> _logger;
    private readonly FusionCacheCartService _service;

    public FusionCacheCartServiceTests()
    {
        _cache = new FusionCache(new FusionCacheOptions());
        _logger = A.Fake<ILogger<FusionCacheCartService>>();
        _service = new FusionCacheCartService(_cache, _logger);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private static CartItem CreateItem(int productId = 1, decimal price = 10m, int quantity = 1) => new()
    {
        ProductId = productId,
        ProductName = $"Product {productId}",
        Price = price,
        Quantity = quantity
    };

    private static GiftEnhancement CreateEnhancement(string id = "wrap", decimal price = 5m) => new()
    {
        Id = id,
        Name = $"Enhancement {id}",
        Price = price
    };

    #region Constructor

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Constructor_WithNullCache_Throws()
    {
        var act = () => new FusionCacheCartService(null!, _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("cache");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Constructor_WithNullLogger_Throws()
    {
        var act = () => new FusionCacheCartService(_cache, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region InitializeAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithNullUserId_Throws()
    {
        var act = () => _service.InitializeAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithEmptyUserId_Throws()
    {
        var act = () => _service.InitializeAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithWhitespaceUserId_Throws()
    {
        var act = () => _service.InitializeAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_EmptyCache_StartsWithEmptyCart()
    {
        await _service.InitializeAsync("user1");

        _service.Items.Should().BeEmpty();
        _service.ItemCount.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_FiresOnChange()
    {
        var fired = false;
        _service.OnChange += () => fired = true;

        await _service.InitializeAsync("user1");

        fired.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_SecondCall_IsIdempotent()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));

        // Second call should be a no-op
        await _service.InitializeAsync("user2");

        // Items should still be there (not reset by second init)
        _service.Items.Should().HaveCount(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_LoadsPersistedState()
    {
        var state = new CartState([CreateItem(42, 99m)], []);
        await _cache.SetAsync("cart:user1", state);

        await _service.InitializeAsync("user1");

        _service.Items.Should().HaveCount(1);
        _service.Items[0].ProductId.Should().Be(42);
    }

    #endregion

    #region AddItemAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_NullItem_Throws()
    {
        await _service.InitializeAsync("user1");

        var act = () => _service.AddItemAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_AddsItem()
    {
        await _service.InitializeAsync("user1");

        await _service.AddItemAsync(CreateItem(1, 25m));

        _service.Items.Should().HaveCount(1);
        _service.Subtotal.Should().Be(25m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_SameProduct_MergesQuantity()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1, 10m, 2));

        await _service.AddItemAsync(CreateItem(1, 10m, 3));

        _service.Items.Should().HaveCount(1);
        _service.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_FiresOnChange()
    {
        await _service.InitializeAsync("user1");
        var fired = false;
        _service.OnChange += () => fired = true;

        await _service.AddItemAsync(CreateItem());

        fired.Should().BeTrue();
    }

    #endregion

    #region RemoveItemAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task RemoveItemAsync_RemovesItem()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));
        await _service.AddItemAsync(CreateItem(2));

        await _service.RemoveItemAsync(1);

        _service.Items.Should().HaveCount(1);
        _service.Items[0].ProductId.Should().Be(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task RemoveItemAsync_NonExistent_NoError()
    {
        await _service.InitializeAsync("user1");

        await _service.RemoveItemAsync(999);

        _service.Items.Should().BeEmpty();
    }

    #endregion

    #region UpdateQuantityAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_UpdatesQuantity()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1, 10m));

        await _service.UpdateQuantityAsync(1, 5);

        _service.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_ZeroQuantity_RemovesItem()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));

        await _service.UpdateQuantityAsync(1, 0);

        _service.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_NegativeQuantity_RemovesItem()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));

        await _service.UpdateQuantityAsync(1, -1);

        _service.Items.Should().BeEmpty();
    }

    #endregion

    #region ToggleEnhancementAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_NullEnhancement_Throws()
    {
        await _service.InitializeAsync("user1");

        var act = () => _service.ToggleEnhancementAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_AddsAndSelects()
    {
        await _service.InitializeAsync("user1");

        await _service.ToggleEnhancementAsync(CreateEnhancement("wrap", 5m));

        _service.Enhancements.Should().HaveCount(1);
        _service.EnhancementsTotal.Should().Be(5m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_TogglesOff()
    {
        await _service.InitializeAsync("user1");
        var enhancement = CreateEnhancement("wrap", 5m);
        await _service.ToggleEnhancementAsync(enhancement);

        await _service.ToggleEnhancementAsync(enhancement);

        _service.Enhancements.Should().BeEmpty();
        _service.EnhancementsTotal.Should().Be(0);
    }

    #endregion

    #region ClearAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ClearAsync_RemovesEverything()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));
        await _service.ToggleEnhancementAsync(CreateEnhancement());

        await _service.ClearAsync();

        _service.Items.Should().BeEmpty();
        _service.Enhancements.Should().BeEmpty();
        _service.Total.Should().Be(0);
    }

    #endregion

    #region Persistence

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_PersistsToCache()
    {
        await _service.InitializeAsync("user1");

        await _service.AddItemAsync(CreateItem(1, 15m));

        // Create a new service instance and load from same cache
        var service2 = new FusionCacheCartService(_cache, _logger);
        await service2.InitializeAsync("user1");

        service2.Items.Should().HaveCount(1);
        service2.Items[0].ProductId.Should().Be(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ClearAsync_PersistsEmptyState()
    {
        await _service.InitializeAsync("user1");
        await _service.AddItemAsync(CreateItem(1));
        await _service.ClearAsync();

        var service2 = new FusionCacheCartService(_cache, _logger);
        await service2.InitializeAsync("user1");

        service2.Items.Should().BeEmpty();
    }

    #endregion

    #region Anonymous Cart Merge

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithAnonymousCart_MergesItems()
    {
        await _cache.SetAsync("cart:anon123", new CartState([CreateItem(10, 30m, 2)], []));

        await _service.InitializeAsync("user1", "anon123");

        _service.Items.Should().HaveCount(1);
        _service.Items[0].ProductId.Should().Be(10);
        _service.Items[0].Quantity.Should().Be(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithAnonymousCart_MergesQuantitiesForSameProduct()
    {
        await _cache.SetAsync("cart:user1", new CartState([CreateItem(10, 30m, 1)], []));
        await _cache.SetAsync("cart:anon123", new CartState([CreateItem(10, 30m, 3)], []));

        await _service.InitializeAsync("user1", "anon123");

        _service.Items.Should().HaveCount(1);
        _service.Items[0].Quantity.Should().Be(4);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_WithAnonymousCart_RemovesAnonKey()
    {
        await _cache.SetAsync("cart:anon123", new CartState([CreateItem(10)], []));

        await _service.InitializeAsync("user1", "anon123");

        var remaining = await _cache.GetOrDefaultAsync<CartState>("cart:anon123");
        remaining.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task InitializeAsync_AnonymousIdSameAsUserId_NoMerge()
    {
        await _cache.SetAsync("cart:user1", new CartState([CreateItem(1)], []));

        // anonymousCartId == userId — should skip merge
        await _service.InitializeAsync("user1", "user1");

        _service.Items.Should().HaveCount(1);
    }

    #endregion
}
