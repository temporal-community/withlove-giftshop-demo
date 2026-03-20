namespace WithLove.Workflows.Tests.Unit.Chat;

public class WorkingCartTests
{
    private static CartSnapshot Item(int id = 1, decimal price = 10m, int qty = 1) =>
        new(id, $"Product {id}", price, qty);

    #region Constructor

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Constructor_EmptyInitial_StartsEmpty()
    {
        var cart = new WorkingCart([]);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Constructor_WithItems_LoadsAll()
    {
        var cart = new WorkingCart([Item(1), Item(2)]);

        cart.Items.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Constructor_IsolatesFromSourceList()
    {
        var source = new List<CartSnapshot> { Item(1) };
        var cart = new WorkingCart(source);

        // Mutating the source list after construction should not affect the working cart
        source.Clear();

        cart.Items.Should().HaveCount(1);
    }

    #endregion

    #region Add

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_NewProduct_AppendsItem()
    {
        var cart = new WorkingCart([]);

        cart.Add(1, "Widget", 9.99m, 1);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].ProductId.Should().Be(1);
        cart.Items[0].ProductName.Should().Be("Widget");
        cart.Items[0].Price.Should().Be(9.99m);
        cart.Items[0].Quantity.Should().Be(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_ExistingProduct_MergesQuantity()
    {
        var cart = new WorkingCart([Item(1, 10m, 2)]);

        cart.Add(1, "Product 1", 10m, 3);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_ExistingProduct_DoesNotDuplicateEntry()
    {
        var cart = new WorkingCart([Item(1, 10m, 1)]);

        cart.Add(1, "Product 1", 10m, 1);

        cart.Items.Should().HaveCount(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_MultipleQuantity_SetsCorrectQuantity()
    {
        var cart = new WorkingCart([]);

        cart.Add(1, "Product 1", 10m, 5);

        cart.Items[0].Quantity.Should().Be(5);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_DifferentProducts_AllPresent()
    {
        var cart = new WorkingCart([]);

        cart.Add(1, "Product 1", 10m, 1);
        cart.Add(2, "Product 2", 20m, 1);
        cart.Add(3, "Product 3", 30m, 1);

        cart.Items.Should().HaveCount(3);
        cart.Items.Select(i => i.ProductId).Should().BeEquivalentTo([1, 2, 3]);
    }

    #endregion

    #region Remove

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Remove_ExistingProduct_RemovesIt()
    {
        var cart = new WorkingCart([Item(1), Item(2)]);

        cart.Remove(1);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].ProductId.Should().Be(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Remove_NonExistentProduct_NoError()
    {
        var cart = new WorkingCart([Item(1)]);

        var act = () => cart.Remove(999);

        act.Should().NotThrow();
        cart.Items.Should().HaveCount(1);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Remove_OnlyItem_LeavesCartEmpty()
    {
        var cart = new WorkingCart([Item(1)]);

        cart.Remove(1);

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Add_ThenRemove_LeavesCartEmpty()
    {
        var cart = new WorkingCart([]);
        cart.Add(1, "Product 1", 10m, 2);

        cart.Remove(1);

        cart.Items.Should().BeEmpty();
    }

    #endregion

    #region Clear

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Clear_RemovesAllItems()
    {
        var cart = new WorkingCart([Item(1), Item(2), Item(3)]);

        cart.Clear();

        cart.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Clear_OnEmptyCart_NoError()
    {
        var cart = new WorkingCart([]);

        var act = () => cart.Clear();

        act.Should().NotThrow();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Clear_ThenAdd_WorksCorrectly()
    {
        var cart = new WorkingCart([Item(1), Item(2)]);
        cart.Clear();

        cart.Add(3, "Product 3", 30m, 1);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].ProductId.Should().Be(3);
    }

    #endregion

    #region FindById

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void FindById_ExistingProduct_ReturnsIt()
    {
        var cart = new WorkingCart([Item(42, 99m, 3)]);

        var result = cart.FindById(42);

        result.Should().NotBeNull();
        result!.ProductId.Should().Be(42);
        result.Price.Should().Be(99m);
        result.Quantity.Should().Be(3);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void FindById_NonExistentProduct_ReturnsNull()
    {
        var cart = new WorkingCart([Item(1)]);

        var result = cart.FindById(999);

        result.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void FindById_AfterRemove_ReturnsNull()
    {
        var cart = new WorkingCart([Item(1)]);
        cart.Remove(1);

        var result = cart.FindById(1);

        result.Should().BeNull();
    }

    #endregion

    #region Summarize

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_EmptyCart_ReturnsEmptyMessage()
    {
        var cart = new WorkingCart([]);

        cart.Summarize().Should().Be("The cart is empty.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_SingleItem_ContainsIdNamePriceQuantity()
    {
        var cart = new WorkingCart([new CartSnapshot(7, "Silk Scarf", 45.00m, 2)]);

        var result = cart.Summarize();

        result.Should().Contain("ID: 7");
        result.Should().Contain("Silk Scarf");
        result.Should().Contain("$45.00");
        result.Should().Contain("x 2");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_SingleItem_UsesItemSingular()
    {
        var cart = new WorkingCart([Item(1, 10m, 1)]);

        cart.Summarize().Should().Contain("1 item)");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_MultipleItems_UsesItemsPlural()
    {
        var cart = new WorkingCart([Item(1), Item(2)]);

        cart.Summarize().Should().Contain("2 items)");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_CalculatesTotalCorrectly()
    {
        // 2 x $10 + 3 x $20 = $80
        var cart = new WorkingCart([Item(1, 10m, 2), Item(2, 20m, 3)]);

        cart.Summarize().Should().Contain("$80.00");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_AfterAdd_ReflectsMutation()
    {
        var cart = new WorkingCart([]);
        cart.Add(1, "Candle", 25m, 1);

        var result = cart.Summarize();

        result.Should().Contain("Candle");
        result.Should().Contain("$25.00");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Summarize_AfterClear_ReturnsEmptyMessage()
    {
        var cart = new WorkingCart([Item(1), Item(2)]);
        cart.Clear();

        cart.Summarize().Should().Be("The cart is empty.");
    }

    #endregion

    #region Mutation Sequencing (the core correctness scenario)

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void AddThenViewCart_ShowsUpdatedState()
    {
        // Simulates: AI calls add_to_cart then immediately calls view_cart
        var cart = new WorkingCart([]);

        cart.Add(5, "Rose Bouquet", 35m, 1);
        var summary = cart.Summarize();

        summary.Should().Contain("Rose Bouquet");
        summary.Should().NotBe("The cart is empty.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void RemoveThenViewCart_ShowsUpdatedState()
    {
        var cart = new WorkingCart([Item(1, 10m), Item(2, 20m)]);

        cart.Remove(1);
        var summary = cart.Summarize();

        summary.Should().NotContain("Product 1");
        summary.Should().Contain("Product 2");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void ClearThenViewCart_ShowsEmptyState()
    {
        var cart = new WorkingCart([Item(1), Item(2), Item(3)]);

        cart.Clear();

        cart.Summarize().Should().Be("The cart is empty.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void AddSameProductTwice_ViewCartShowsCorrectTotal()
    {
        // Simulates: user has 1 in cart, AI adds 2 more → view_cart should show 3
        var cart = new WorkingCart([Item(1, 15m, 1)]);

        cart.Add(1, "Product 1", 15m, 2);
        var summary = cart.Summarize();

        summary.Should().Contain("x 3");
        summary.Should().Contain("$45.00");
    }

    #endregion
}
