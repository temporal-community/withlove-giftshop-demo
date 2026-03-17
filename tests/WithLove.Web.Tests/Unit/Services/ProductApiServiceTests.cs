using Microsoft.Extensions.Caching.Memory;

namespace WithLove.Web.Tests.Unit.Services;

public class ProductApiServiceTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductApiService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ProductApiService _service;

    public ProductApiServiceTests()
    {
        _handler = new FakeHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://fake-api") };
        _logger = A.Fake<ILogger<ProductApiService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new ProductApiService(_httpClient, _logger, _cache);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
        _cache.Dispose();
    }

    #region Constructor

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public void Constructor_NullHttpClient_Throws()
    {
        var act = () => new ProductApiService(null!, _logger, _cache);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ProductApiService(_httpClient, null!, _cache);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new ProductApiService(_httpClient, _logger, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("cache");
    }

    #endregion

    #region GetProductAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductAsync_Success_ReturnsProduct()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products/1", """
        {
            "id": 1,
            "name": "Rose Bouquet",
            "description": "Beautiful roses",
            "price": 49.99,
            "categoryName": "Flora"
        }
        """);

        var product = await _service.GetProductAsync(1);

        product.Should().NotBeNull();
        product!.Id.Should().Be(1);
        product.Name.Should().Be("Rose Bouquet");
        product.Price.Should().Be(49.99m);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductAsync_NotFound_ReturnsNull()
    {
        _handler.SetupResponse("/api/products/999", "", HttpStatusCode.NotFound);

        var product = await _service.GetProductAsync(999);

        product.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductAsync_ServerError_ReturnsNull()
    {
        _handler.SetupResponse("/api/products/1", "", HttpStatusCode.InternalServerError);

        var product = await _service.GetProductAsync(1);

        product.Should().BeNull();
    }

    #endregion

    #region GetProductsAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductsAsync_Success_ReturnsProducts()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 1, "name": "A", "description": "", "price": 10, "categoryName": "Flora" },
                { "id": 2, "name": "B", "description": "", "price": 20, "categoryName": "Cacao" }
            ],
            "nextLink": null
        }
        """);

        var products = await _service.GetProductsAsync();

        products.Should().HaveCount(2);
        products[0].Name.Should().Be("A");
        products[1].Name.Should().Be("B");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductsAsync_ServerError_ReturnsEmpty()
    {
        _handler.SetupResponse("/api/products", "", HttpStatusCode.InternalServerError);

        var products = await _service.GetProductsAsync();

        products.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductsAsync_CachesResults()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 1, "name": "A", "description": "", "price": 10 }
            ]
        }
        """);

        var first = await _service.GetProductsAsync();
        // Change the handler response — but cache should return the same result
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 1, "name": "A", "description": "", "price": 10 },
                { "id": 2, "name": "B", "description": "", "price": 20 }
            ]
        }
        """);
        var second = await _service.GetProductsAsync();

        second.Should().HaveCount(1, "second call should return cached result");
    }

    #endregion

    #region GetProductsByCategoryAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductsByCategoryAsync_Success_ReturnsProducts()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products/category/2", """
        {
            "value": [
                { "id": 3, "name": "Tulip", "description": "", "price": 30, "categoryName": "Flora" }
            ]
        }
        """);

        var products = await _service.GetProductsByCategoryAsync(2);

        products.Should().HaveCount(1);
        products[0].Name.Should().Be("Tulip");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetProductsByCategoryAsync_ServerError_ReturnsEmpty()
    {
        _handler.SetupResponse("/api/products/category/2", "", HttpStatusCode.InternalServerError);

        var products = await _service.GetProductsByCategoryAsync(2);

        products.Should().BeEmpty();
    }

    #endregion

    #region GetCategoriesAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetCategoriesAsync_Success_ReturnsCategories()
    {
        _handler.SetupResponse("/api/categories", """
        {
            "value": [
                { "id": 1, "name": "Cacao", "description": "Chocolate" },
                { "id": 2, "name": "Flora", "description": "Flowers" }
            ]
        }
        """);

        var categories = await _service.GetCategoriesAsync();

        categories.Should().HaveCount(2);
        categories[0].Name.Should().Be("Cacao");
        categories[1].Name.Should().Be("Flora");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetCategoriesAsync_ServerError_ReturnsEmpty()
    {
        _handler.SetupResponse("/api/categories", "", HttpStatusCode.InternalServerError);

        var categories = await _service.GetCategoriesAsync();

        categories.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetCategoriesAsync_CachesResults()
    {
        _handler.SetupResponse("/api/categories", """
        {
            "value": [{ "id": 1, "name": "Cacao" }]
        }
        """);

        var first = await _service.GetCategoriesAsync();
        _handler.SetupResponse("/api/categories", """
        {
            "value": [{ "id": 1, "name": "Cacao" }, { "id": 2, "name": "Flora" }]
        }
        """);
        var second = await _service.GetCategoriesAsync();

        second.Should().HaveCount(1, "second call should return cached result");
    }

    #endregion

    #region GetCategoryAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetCategoryAsync_Success_ReturnsCategory()
    {
        _handler.SetupResponse("/api/categories/1", """
        {
            "id": 1,
            "name": "Cacao",
            "description": "Chocolate gifts"
        }
        """);

        var category = await _service.GetCategoryAsync(1);

        category.Should().NotBeNull();
        category!.Name.Should().Be("Cacao");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetCategoryAsync_NotFound_ReturnsNull()
    {
        _handler.SetupResponse("/api/categories/999", "", HttpStatusCode.NotFound);

        var category = await _service.GetCategoryAsync(999);

        category.Should().BeNull();
    }

    #endregion

    #region GetFeaturedProductsAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetFeaturedProductsAsync_FiltersById1And3()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 1, "name": "Featured1", "description": "", "price": 10 },
                { "id": 2, "name": "NotFeatured", "description": "", "price": 20 },
                { "id": 3, "name": "Featured2", "description": "", "price": 30 }
            ]
        }
        """);

        var featured = await _service.GetFeaturedProductsAsync();

        featured.Should().HaveCount(2);
        featured.Select(p => p.Id).Should().BeEquivalentTo([1, 3]);
    }

    #endregion

    #region GetSmallLuxuriesAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetSmallLuxuriesAsync_FiltersById4_5_6()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 1, "name": "A", "description": "", "price": 10 },
                { "id": 4, "name": "Lux1", "description": "", "price": 40 },
                { "id": 5, "name": "Lux2", "description": "", "price": 50 },
                { "id": 6, "name": "Lux3", "description": "", "price": 60 }
            ]
        }
        """);

        var luxuries = await _service.GetSmallLuxuriesAsync();

        luxuries.Should().HaveCount(3);
        luxuries.Select(p => p.Id).Should().BeEquivalentTo([4, 5, 6]);
    }

    #endregion

    #region GetRecommendationsAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetRecommendationsAsync_FiltersById4_3_6_11()
    {
        SetupCategoriesResponse();
        _handler.SetupResponse("/api/products", """
        {
            "value": [
                { "id": 3, "name": "R1", "description": "", "price": 10 },
                { "id": 4, "name": "R2", "description": "", "price": 20 },
                { "id": 6, "name": "R3", "description": "", "price": 30 },
                { "id": 7, "name": "Not", "description": "", "price": 40 },
                { "id": 11, "name": "R4", "description": "", "price": 50 }
            ]
        }
        """);

        var recommendations = await _service.GetRecommendationsAsync(1);

        recommendations.Should().HaveCount(4);
        recommendations.Select(p => p.Id).Should().BeEquivalentTo([3, 4, 6, 11]);
    }

    #endregion

    #region GetGiftEnhancementsAsync

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetGiftEnhancementsAsync_ReturnsFourEnhancements()
    {
        var enhancements = await _service.GetGiftEnhancementsAsync();

        enhancements.Should().HaveCount(4);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetGiftEnhancementsAsync_IncludesExpectedIds()
    {
        var enhancements = await _service.GetGiftEnhancementsAsync();

        enhancements.Select(e => e.Id).Should().BeEquivalentTo(
            ["bespoke-wrapping", "handwritten-card", "silk-ribbon", "personal-note"]);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ProductApi)]
    public async Task GetGiftEnhancementsAsync_AllHavePositivePrices()
    {
        var enhancements = await _service.GetGiftEnhancementsAsync();

        enhancements.Should().AllSatisfy(e => e.Price.Should().BeGreaterThan(0));
    }

    #endregion

    #region Helpers

    private void SetupCategoriesResponse()
    {
        _handler.SetupResponse("/api/categories", """
        {
            "value": [
                { "id": 1, "name": "Cacao", "description": "Chocolate" },
                { "id": 2, "name": "Flora", "description": "Flowers" }
            ]
        }
        """);
    }

    #endregion
}

/// <summary>
/// Fake HTTP message handler that returns pre-configured responses based on URL path.
/// </summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (string Content, HttpStatusCode StatusCode)> _responses = new();

    public void SetupResponse(string pathPrefix, string jsonContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses[pathPrefix] = (jsonContent, statusCode);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";

        // Find matching response by path prefix (longest match wins)
        var match = _responses
            .Where(r => path.StartsWith(r.Key))
            .OrderByDescending(r => r.Key.Length)
            .FirstOrDefault();

        if (match.Value.Content is not null)
        {
            return Task.FromResult(new HttpResponseMessage(match.Value.StatusCode)
            {
                Content = new StringContent(match.Value.Content, System.Text.Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
