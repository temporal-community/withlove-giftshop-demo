using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WithLove.Web.Models;

namespace WithLove.Web.Services;

/// <summary>HTTP product client with local caching and DTO mapping.</summary>
public class ProductApiService : IProductService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductApiService> _logger;
    private readonly IMemoryCache _cache;

    private const string CategoryCacheKey = "productapi:categories:all";
    private const string CategoryNameToIdMapKey = "productapi:category:nametoIdMap";
    private const string ProductsCacheKeyPrefix = "productapi:products";

    private static readonly TimeSpan CategoryCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProductsCacheDuration = TimeSpan.FromMinutes(15);

    public ProductApiService(
        HttpClient httpClient,
        ILogger<ProductApiService> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>Gets a single product by ID.</summary>
    public async Task<Product?> GetProductAsync(int productId)
    {
        try
        {
            _logger.FetchingProduct(productId);

            var response = await _httpClient.GetAsync($"/api/products/{productId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.FailedToFetchProduct(productId, response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var productDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            var product = DtoMappers.MapProductResponse(productDto, categoryMap);
            _logger.FetchedProduct(productId, product.Name);

            return product;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingProduct(ex, productId);
            return null;
        }
    }

    /// <summary>Gets all products with default pagination.</summary>
    public async Task<List<Product>> GetProductsAsync()
    {
        try
        {
            _logger.FetchingProducts();

            if (_cache.TryGetValue(ProductsCacheKeyPrefix, out List<Product>? cachedProducts))
            {
                _logger.ReturningProductsFromCache();
                return cachedProducts ?? [];
            }

            var response = await _httpClient.GetAsync("/api/products?top=100&skip=0&orderBy=addedDate%20desc");

            if (!response.IsSuccessStatusCode)
            {
                _logger.FailedToFetchProducts(response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            var products = new List<Product>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                products = DtoMappers.MapProductsResponse(valueElement, categoryMap);
            }

            _cache.Set(ProductsCacheKeyPrefix, products, ProductsCacheDuration);

            _logger.FetchedProducts(products.Count);
            return products;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingProducts(ex);
            return [];
        }
    }

    /// <summary>Gets products in a category.</summary>
    public async Task<List<Product>> GetProductsByCategoryAsync(int categoryId)
    {
        try
        {
            _logger.FetchingProductsForCategory(categoryId);

            var response = await _httpClient.GetAsync($"/api/products/category/{categoryId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.FailedToFetchProductsForCategory(categoryId, response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            var products = new List<Product>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                products = DtoMappers.MapProductsResponse(valueElement, categoryMap);
            }

            _logger.FetchedProductsForCategory(products.Count, categoryId);
            return products;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingProductsForCategory(ex, categoryId);
            return [];
        }
    }

    /// <summary>Gets all categories.</summary>
    public async Task<List<Category>> GetCategoriesAsync()
    {
        try
        {
            _logger.FetchingCategories();

            if (_cache.TryGetValue(CategoryCacheKey, out List<Category>? cachedCategories))
            {
                _logger.ReturningCategoriesFromCache();
                return cachedCategories ?? [];
            }

            var response = await _httpClient.GetAsync("/api/categories");

            if (!response.IsSuccessStatusCode)
            {
                _logger.FailedToFetchCategories(response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            var categories = new List<Category>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                categories = DtoMappers.MapCategoriesResponse(valueElement);
            }

            _cache.Set(CategoryCacheKey, categories, CategoryCacheDuration);

            _logger.FetchedCategories(categories.Count);
            return categories;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingCategories(ex);
            return [];
        }
    }

    /// <summary>Gets a category by ID.</summary>
    public async Task<Category?> GetCategoryAsync(int categoryId)
    {
        try
        {
            _logger.FetchingCategory(categoryId);

            var response = await _httpClient.GetAsync($"/api/categories/{categoryId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.FailedToFetchCategory(categoryId, response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var categoryJson = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var category = DtoMappers.MapCategoryResponse(categoryJson);

            _logger.FetchedCategory(categoryId, category.Name);
            return category;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingCategory(ex, categoryId);
            return null;
        }
    }

    /// <summary>Gets featured products from the cached product list.</summary>
    public async Task<List<Product>> GetFeaturedProductsAsync()
    {
        try
        {
            _logger.FetchingFeaturedProducts();

            var allProducts = await GetProductsAsync();
            var featured = allProducts.Where(p => p.Id is 1 or 3).ToList();

            _logger.FetchedFeaturedProducts(featured.Count);
            return featured;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingFeaturedProducts(ex);
            return [];
        }
    }

    /// <summary>Gets small-luxury products from the cached product list.</summary>
    public async Task<List<Product>> GetSmallLuxuriesAsync()
    {
        try
        {
            _logger.FetchingSmallLuxuryProducts();

            var allProducts = await GetProductsAsync();
            var smallLuxuries = allProducts.Where(p => p.Id is 4 or 5 or 6).ToList();

            _logger.FetchedSmallLuxuryProducts(smallLuxuries.Count);
            return smallLuxuries;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingSmallLuxuryProducts(ex);
            return [];
        }
    }

    /// <summary>Gets recommendation products from the cached product list.</summary>
    public async Task<List<Product>> GetRecommendationsAsync(int productId)
    {
        try
        {
            _logger.FetchingRecommendations(productId);

            var allProducts = await GetProductsAsync();
            var recommendations = allProducts.Where(p => p.Id is 4 or 3 or 6 or 11).ToList();

            _logger.FetchedRecommendations(recommendations.Count, productId);
            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.ErrorFetchingRecommendations(ex, productId);
            return [];
        }
    }

    /// <summary>Searches products by name and description.</summary>
    public async Task<List<Product>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            _logger.SearchingProducts(query);

            var response = await _httpClient.GetAsync(
                $"/api/products/search?q={Uri.EscapeDataString(query)}&top=20", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.SearchFailed(response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            var products = new List<Product>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                products = DtoMappers.MapProductsResponse(valueElement, categoryMap);
            }

            _logger.SearchReturnedResults(query, products.Count);
            return products;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.SearchCancelled(query);
            return [];
        }
        catch (Exception ex)
        {
            _logger.ErrorSearchingProducts(ex, query);
            return [];
        }
    }

    /// <summary>Gets local gift enhancement options.</summary>
    public Task<List<GiftEnhancement>> GetGiftEnhancementsAsync()
    {
        var enhancements = new List<GiftEnhancement>
        {
            new GiftEnhancement
            {
                Id = "bespoke-wrapping",
                Name = "Bespoke Wrapping",
                Description = "Sustainable Washi paper with dried floral accent.",
                Price = 15m,
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY"
            },
            new GiftEnhancement
            {
                Id = "handwritten-card",
                Name = "Handwritten Card",
                Description = "Personalized message on thick cotton cardstock.",
                Price = 8m,
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI"
            },
            new GiftEnhancement
            {
                Id = "silk-ribbon",
                Name = "Silk Ribbon Detail",
                Description = "Artisan silk ribbon in your choice of colours.",
                Price = 6m,
                ImageUrl = "https://images.unsplash.com/photo-1513201099705-a9746e1e201f"
            },
            new GiftEnhancement
            {
                Id = "personal-note",
                Name = "Personal Note",
                Description = "Handwritten note included with your gift.",
                Price = 4m,
                ImageUrl = "https://images.unsplash.com/photo-1764385828126-56b5ec728083"
            }
        };

        return Task.FromResult(enhancements);
    }

    /// <summary>Maps category names from product responses to route IDs.</summary>
    private async Task<Dictionary<string, int>> GetCategoryNameToIdMappingAsync()
    {
        if (_cache.TryGetValue(CategoryNameToIdMapKey, out Dictionary<string, int>? cachedMap))
        {
            return cachedMap ?? [];
        }

        try
        {
            var categories = await GetCategoriesAsync();
            var mapping = categories.ToDictionary(c => c.Name, c => c.Id);

            _cache.Set(CategoryNameToIdMapKey, mapping, CategoryCacheDuration);

            return mapping;
        }
        catch (Exception ex)
        {
            _logger.ErrorBuildingCategoryMap(ex);
            return [];
        }
    }
}
