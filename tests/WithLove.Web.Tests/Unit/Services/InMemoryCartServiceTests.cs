namespace WithLove.Web.Tests.Unit.Services;

public class InMemoryCartServiceTests
{
    private readonly InMemoryCartService _service = new();

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

    #region Initial State

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void InitialState_IsEmpty()
    {
        _service.Items.Should().BeEmpty();
        _service.ItemCount.Should().Be(0);
        _service.Subtotal.Should().Be(0);
        _service.EnhancementsTotal.Should().Be(0);
        _service.Total.Should().Be(0);
        _service.Enhancements.Should().BeEmpty();
    }

    #endregion

    #region AddItemAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_AddsNewItem()
    {
        await _service.AddItemAsync(CreateItem(1, 25m));

        _service.Items.Should().HaveCount(1);
        _service.Items[0].ProductId.Should().Be(1);
        _service.ItemCount.Should().Be(1);
        _service.Subtotal.Should().Be(25m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_SameProduct_MergesQuantity()
    {
        await _service.AddItemAsync(CreateItem(1, 10m, 2));
        await _service.AddItemAsync(CreateItem(1, 10m, 3));

        _service.Items.Should().HaveCount(1);
        _service.Items[0].Quantity.Should().Be(5);
        _service.Subtotal.Should().Be(50m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_DifferentProducts_AddsSeparately()
    {
        await _service.AddItemAsync(CreateItem(1, 10m));
        await _service.AddItemAsync(CreateItem(2, 20m));

        _service.Items.Should().HaveCount(2);
        _service.ItemCount.Should().Be(2);
        _service.Subtotal.Should().Be(30m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task AddItemAsync_FiresOnChange()
    {
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
        await _service.AddItemAsync(CreateItem(1));
        await _service.AddItemAsync(CreateItem(2));

        await _service.RemoveItemAsync(1);

        _service.Items.Should().HaveCount(1);
        _service.Items[0].ProductId.Should().Be(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task RemoveItemAsync_NonExistentProduct_NoError()
    {
        await _service.AddItemAsync(CreateItem(1));

        await _service.RemoveItemAsync(999);

        _service.Items.Should().HaveCount(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task RemoveItemAsync_FiresOnChange()
    {
        var fired = false;
        await _service.AddItemAsync(CreateItem(1));
        _service.OnChange += () => fired = true;

        await _service.RemoveItemAsync(1);

        fired.Should().BeTrue();
    }

    #endregion

    #region UpdateQuantityAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_UpdatesQuantity()
    {
        await _service.AddItemAsync(CreateItem(1, 10m));

        await _service.UpdateQuantityAsync(1, 5);

        _service.Items[0].Quantity.Should().Be(5);
        _service.Subtotal.Should().Be(50m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_ZeroQuantity_RemovesItem()
    {
        await _service.AddItemAsync(CreateItem(1));

        await _service.UpdateQuantityAsync(1, 0);

        _service.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_NegativeQuantity_RemovesItem()
    {
        await _service.AddItemAsync(CreateItem(1));

        await _service.UpdateQuantityAsync(1, -1);

        _service.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_NonExistentProduct_NoError()
    {
        await _service.UpdateQuantityAsync(999, 5);

        _service.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task UpdateQuantityAsync_FiresOnChange()
    {
        var fired = false;
        await _service.AddItemAsync(CreateItem(1));
        _service.OnChange += () => fired = true;

        await _service.UpdateQuantityAsync(1, 3);

        fired.Should().BeTrue();
    }

    #endregion

    #region ToggleEnhancementAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_AddsNewEnhancement_Selected()
    {
        var enhancement = CreateEnhancement("wrap", 5m);

        await _service.ToggleEnhancementAsync(enhancement);

        _service.Enhancements.Should().HaveCount(1);
        _service.EnhancementsTotal.Should().Be(5m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_TogglesExistingOff()
    {
        var enhancement = CreateEnhancement("wrap", 5m);
        await _service.ToggleEnhancementAsync(enhancement);

        await _service.ToggleEnhancementAsync(enhancement);

        _service.Enhancements.Should().BeEmpty();
        _service.EnhancementsTotal.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_TogglesBackOn()
    {
        var enhancement = CreateEnhancement("wrap", 5m);
        await _service.ToggleEnhancementAsync(enhancement);
        await _service.ToggleEnhancementAsync(enhancement);

        await _service.ToggleEnhancementAsync(enhancement);

        _service.Enhancements.Should().HaveCount(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ToggleEnhancementAsync_FiresOnChange()
    {
        var fired = false;
        _service.OnChange += () => fired = true;

        await _service.ToggleEnhancementAsync(CreateEnhancement());

        fired.Should().BeTrue();
    }

    #endregion

    #region ClearAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ClearAsync_RemovesAllItemsAndEnhancements()
    {
        await _service.AddItemAsync(CreateItem(1));
        await _service.AddItemAsync(CreateItem(2));
        await _service.ToggleEnhancementAsync(CreateEnhancement());

        await _service.ClearAsync();

        _service.Items.Should().BeEmpty();
        _service.Enhancements.Should().BeEmpty();
        _service.Total.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ClearAsync_FiresOnChange()
    {
        var fired = false;
        _service.OnChange += () => fired = true;

        await _service.ClearAsync();

        fired.Should().BeTrue();
    }

    #endregion

    #region Computed Properties

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task Total_IncludesSubtotalAndEnhancements()
    {
        await _service.AddItemAsync(CreateItem(1, 20m, 2));
        var enhancement = CreateEnhancement("wrap", 8m);
        await _service.ToggleEnhancementAsync(enhancement);

        _service.Subtotal.Should().Be(40m);
        _service.EnhancementsTotal.Should().Be(8m);
        _service.Total.Should().Be(48m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task ItemCount_SumsAllQuantities()
    {
        await _service.AddItemAsync(CreateItem(1, 10m, 3));
        await _service.AddItemAsync(CreateItem(2, 10m, 7));

        _service.ItemCount.Should().Be(10);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public async Task EnhancementsTotal_OnlyCountsSelected()
    {
        var e1 = CreateEnhancement("a", 5m);
        var e2 = CreateEnhancement("b", 10m);
        await _service.ToggleEnhancementAsync(e1); // selected
        await _service.ToggleEnhancementAsync(e2); // selected
        await _service.ToggleEnhancementAsync(e2); // removed from list

        _service.Enhancements.Should().HaveCount(1);
        _service.EnhancementsTotal.Should().Be(5m);
    }

    #endregion
}
