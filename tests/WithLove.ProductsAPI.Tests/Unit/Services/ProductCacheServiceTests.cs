namespace WithLove.ProductsAPI.Tests.Unit.Services;

using FakeItEasy;
using MockQueryable.FakeItEasy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WithLove.Data;
using WithLove.Data.Models;
using WithLove.ProductsAPI.Services;
using ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Unit tests for ProductCacheService.
/// Focus: Cache behavior, parameter validation, and cache invalidation.
/// Uses MockQueryable.FakeItEasy to mock DbSet<Product>.
/// </summary>
public class ProductCacheServiceTests
{
    private readonly ProductsDbContext _fakeDbContext;
    private readonly IFusionCache _fakeCache;
    private readonly ILogger<ProductCacheService> _fakeLogger;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _fakeEmbeddingGenerator;
    private readonly List<Product> _testProducts;

    public ProductCacheServiceTests()
    {
        // Create minimal DbContextOptions (needed for FakeItEasy to construct the fake)
        // The options are only used for the constructor; MockQueryable provides the actual DbSet
        var options = new DbContextOptionsBuilder<ProductsDbContext>().Options;

        _fakeDbContext = A.Fake<ProductsDbContext>(opts =>
            opts.WithArgumentsForConstructor(new object[] { options }));

        // Use a real FusionCache instance instead of mocking (FusionCache is self-contained)
        _fakeCache = new FusionCache(new FusionCacheOptions());
        _fakeLogger = A.Fake<ILogger<ProductCacheService>>();
        _fakeEmbeddingGenerator = A.Fake<IEmbeddingGenerator<string, Embedding<float>>>();

        // Create test data
        _testProducts = new List<Product>
        {
            new() { Id = 1, Name = "Product 1", Price = 19.99m, CategoryId = 1, IsEnabled = true, AddedDate = DateTime.UtcNow.AddDays(-5), RowVersion = new byte[] { 1, 2, 3 } },
            new() { Id = 2, Name = "Product 2", Price = 29.99m, CategoryId = 1, IsEnabled = true, AddedDate = DateTime.UtcNow.AddDays(-3), RowVersion = new byte[] { 1, 2, 4 } },
            new() { Id = 3, Name = "Product 3", Price = 39.99m, CategoryId = 2, IsEnabled = true, AddedDate = DateTime.UtcNow.AddDays(-1), RowVersion = new byte[] { 1, 2, 5 } },
            new() { Id = 4, Name = "Product 4", Price = 49.99m, CategoryId = 2, IsEnabled = false, AddedDate = DateTime.UtcNow, RowVersion = new byte[] { 1, 2, 6 } },
        };
    }

    private ProductCacheService CreateService()
    {
        return new ProductCacheService(_fakeDbContext, _fakeCache, _fakeLogger, _fakeEmbeddingGenerator, new Instrumentation());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act & Assert
        var act = () => CreateService();
        act.Should().NotThrow();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void Constructor_WithNullDbContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProductCacheService(null!, _fakeCache, _fakeLogger, _fakeEmbeddingGenerator, new Instrumentation()));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void Constructor_WithNullCache_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProductCacheService(_fakeDbContext, null!, _fakeLogger, _fakeEmbeddingGenerator, new Instrumentation()));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProductCacheService(_fakeDbContext, _fakeCache, null!, _fakeEmbeddingGenerator, new Instrumentation()));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductByIdAsync_WithValidId_ReturnsProduct()
    {
        // Arrange
        var productId = 1;
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var result = await service.GetProductByIdAsync(productId);

        // Assert
        result.Should().NotBeNull();
        result?.Id.Should().Be(productId);
        result?.Name.Should().Be("Product 1");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var productId = 999;
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var result = await service.GetProductByIdAsync(productId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductByIdAsync_OnlyReturnsEnabledProducts()
    {
        // Arrange - Try to get disabled product
        var disabledProductId = 4;
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var result = await service.GetProductByIdAsync(disabledProductId);

        // Assert
        result.Should().BeNull(); // Disabled product should not be returned
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductListAsync_WithDefaultPagination_ReturnsFirstPage()
    {
        // Arrange
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, total) = await service.GetProductListAsync();

        // Assert
        products.Should().NotBeEmpty();
        total.Should().Be(3); // Only 3 enabled products
        products.Count.Should().BeLessThanOrEqualTo(10); // Default page size
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductListAsync_OnlyReturnsEnabledProducts()
    {
        // Arrange
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, total) = await service.GetProductListAsync();

        // Assert
        products.Should().AllSatisfy(p => p.IsEnabled.Should().BeTrue());
        total.Should().Be(3);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductListAsync_WithNegativePageNumber_DefaultsToPageOne()
    {
        // Arrange
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, _) = await service.GetProductListAsync(pageNumber: -5);

        // Assert - Should not throw and should return results
        products.Should().NotBeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductListAsync_WithNegativePageSize_DefaultsToPageSize10()
    {
        // Arrange
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, _) = await service.GetProductListAsync(pageSize: -5);

        // Assert
        products.Count.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductsByCategoryAsync_WithValidCategoryId_ReturnsProductsForCategory()
    {
        // Arrange
        var categoryId = 1;
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, total) = await service.GetProductsByCategoryAsync(categoryId);

        // Assert
        products.Should().NotBeEmpty();
        products.Should().AllSatisfy(p => p.CategoryId.Should().Be(categoryId));
        products.Should().AllSatisfy(p => p.IsEnabled.Should().BeTrue());
        total.Should().Be(2); // Two enabled products in category 1
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task GetProductsByCategoryAsync_OnlyReturnsEnabledProducts()
    {
        // Arrange - Category 2 has one enabled and one disabled product
        var categoryId = 2;
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, total) = await service.GetProductsByCategoryAsync(categoryId);

        // Assert
        products.Should().AllSatisfy(p => p.IsEnabled.Should().BeTrue());
        total.Should().Be(1); // Only Product 3 is enabled in category 2
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task SearchProductsAsync_WithValidQuery_ReturnsMatchingProducts()
    {
        // Arrange
        var query = "Product 1";
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, total) = await service.SearchProductsAsync(query);

        // Assert
        products.Should().NotBeEmpty();
        products.Should().AllSatisfy(p => p.Name.ToLower().Should().Contain(query.ToLower()));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task SearchProductsAsync_CaseInsensitive_ReturnsMatches()
    {
        // Arrange
        var query = "PRODUCT";
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, _) = await service.SearchProductsAsync(query);

        // Assert
        products.Count.Should().Be(3); // All three enabled products match "product"
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task SearchProductsAsync_WithEmptyQuery_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (products, total) = await service.SearchProductsAsync("");

        // Assert
        products.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task SearchProductsAsync_WithWhitespaceQuery_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (products, total) = await service.SearchProductsAsync("   ");

        // Assert
        products.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task SearchProductsAsync_OnlyReturnsEnabledProducts()
    {
        // Arrange
        var query = "Product";
        var mockDbSet = _testProducts.BuildMockDbSet();
        A.CallTo(() => _fakeDbContext.Products).Returns(mockDbSet);

        var service = CreateService();

        // Act
        var (products, _) = await service.SearchProductsAsync(query);

        // Assert
        products.Should().AllSatisfy(p => p.IsEnabled.Should().BeTrue());
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task InvalidateProductCacheAsync_WithValidId_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();
        var productId = 42;

        // Act
        var act = async () => await service.InvalidateProductCacheAsync(productId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task InvalidateProductListCacheAsync_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.InvalidateProductListCacheAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task InvalidateCategoryCacheAsync_WithValidCategoryId_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();
        var categoryId = 7;

        // Act
        var act = async () => await service.InvalidateCategoryCacheAsync(categoryId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public async Task InvalidateSearchCacheAsync_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.InvalidateSearchCacheAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }
}
