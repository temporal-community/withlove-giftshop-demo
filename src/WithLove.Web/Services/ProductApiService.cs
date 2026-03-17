using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using WithLove.Web.Models;

namespace WithLove.Web.Services;

/// <summary>
/// HTTP-based product service that calls WithLove.ProductsAPI microservice.
/// Uses Aspire service discovery to automatically resolve the "productsapi" endpoint.
/// Implements IProductService with API calls, DTO mapping, and client-side filtering.
///
/// Service Discovery: The HttpClient is configured with Aspire's service discovery,
/// which automatically resolves "productsapi" to the ProductsAPI service running in Aspire.
///
/// Resilience: Built-in via ServiceDefaults configuration (retries, timeouts, circuit breaker).
/// </summary>
public class ProductApiService : IProductService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductApiService> _logger;
    private readonly IMemoryCache _cache;

    // Cache keys
    private const string CategoryCacheKey = "productapi:categories:all";
    private const string CategoryNameToIdMapKey = "productapi:category:nametoIdMap";
    private const string ProductsCacheKeyPrefix = "productapi:products";

    // Cache duration
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

    /// <summary>
    /// Get a single product by ID from the API.
    /// </summary>
    public async Task<Product?> GetProductAsync(int productId)
    {
        try
        {
            _logger.LogDebug("Fetching product with ID: {ProductId}", productId);

            var response = await _httpClient.GetAsync($"/api/products/{productId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch product {ProductId}: HTTP {StatusCode}", productId, response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var productDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            var product = DtoMappers.MapProductResponse(productDto, categoryMap);
            _logger.LogInformation("Successfully fetched product: {ProductId} - {ProductName}", productId, product.Name);

            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product {ProductId}", productId);
            return null;
        }
    }

    /// <summary>
    /// Get all products with default pagination (top=100).
    /// Results are cached for performance.
    /// </summary>
    public async Task<List<Product>> GetProductsAsync()
    {
        try
        {
            _logger.LogDebug("Fetching all products");

            // Check cache first
            if (_cache.TryGetValue(ProductsCacheKeyPrefix, out List<Product>? cachedProducts))
            {
                _logger.LogDebug("Returning products from cache");
                return cachedProducts ?? [];
            }

            // Fetch from API with pagination
            var response = await _httpClient.GetAsync("/api/products?top=100&skip=0&orderBy=addedDate%20desc");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch products: HTTP {StatusCode}", response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            // Map the response - extract the "value" array from PaginatedResponse<ProductResponse>
            var products = new List<Product>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                products = DtoMappers.MapProductsResponse(valueElement, categoryMap);
            }

            // Cache the results
            _cache.Set(ProductsCacheKeyPrefix, products, ProductsCacheDuration);

            _logger.LogInformation("Successfully fetched {ProductCount} products", products.Count);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products");
            return [];
        }
    }

    /// <summary>
    /// Get products in a specific category from the API.
    /// </summary>
    public async Task<List<Product>> GetProductsByCategoryAsync(int categoryId)
    {
        try
        {
            _logger.LogDebug("Fetching products for category: {CategoryId}", categoryId);

            var response = await _httpClient.GetAsync($"/api/products/category/{categoryId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch products for category {CategoryId}: HTTP {StatusCode}", categoryId, response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var categoryMap = await GetCategoryNameToIdMappingAsync();

            // Extract "value" array from PaginatedResponse<ProductResponse>
            var products = new List<Product>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                products = DtoMappers.MapProductsResponse(valueElement, categoryMap);
            }

            _logger.LogInformation("Successfully fetched {ProductCount} products for category {CategoryId}", products.Count, categoryId);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products for category {CategoryId}", categoryId);
            return [];
        }
    }

    /// <summary>
    /// Get all categories from the API.
    /// Results are cached for performance.
    /// </summary>
    public async Task<List<Category>> GetCategoriesAsync()
    {
        try
        {
            _logger.LogDebug("Fetching all categories");

            // Check cache first
            if (_cache.TryGetValue(CategoryCacheKey, out List<Category>? cachedCategories))
            {
                _logger.LogDebug("Returning categories from cache");
                return cachedCategories ?? [];
            }

            var response = await _httpClient.GetAsync("/api/categories");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch categories: HTTP {StatusCode}", response.StatusCode);
                return [];
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var responseDto = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            // Extract "value" array from PaginatedResponse<CategoryResponse>
            var categories = new List<Category>();
            if (responseDto.TryGetProperty("value", out var valueElement))
            {
                categories = DtoMappers.MapCategoriesResponse(valueElement);
            }

            // Cache the results
            _cache.Set(CategoryCacheKey, categories, CategoryCacheDuration);

            _logger.LogInformation("Successfully fetched {CategoryCount} categories", categories.Count);
            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching categories");
            return [];
        }
    }

    /// <summary>
    /// Get a single category by ID from the API.
    /// </summary>
    public async Task<Category?> GetCategoryAsync(int categoryId)
    {
        try
        {
            _logger.LogDebug("Fetching category with ID: {CategoryId}", categoryId);

            var response = await _httpClient.GetAsync($"/api/categories/{categoryId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch category {CategoryId}: HTTP {StatusCode}", categoryId, response.StatusCode);
                return null;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var categoryJson = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var category = DtoMappers.MapCategoryResponse(categoryJson);

            _logger.LogInformation("Successfully fetched category: {CategoryId} - {CategoryName}", categoryId, category.Name);
            return category;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching category {CategoryId}", categoryId);
            return null;
        }
    }

    /// <summary>
    /// Get featured products (client-side filtering from all products).
    /// Featured products are: IDs 1 and 3.
    /// </summary>
    public async Task<List<Product>> GetFeaturedProductsAsync()
    {
        try
        {
            _logger.LogDebug("Fetching featured products");

            var allProducts = await GetProductsAsync();
            var featured = allProducts.Where(p => p.Id is 1 or 3).ToList();

            _logger.LogInformation("Successfully fetched {FeaturedCount} featured products", featured.Count);
            return featured;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching featured products");
            return [];
        }
    }

    /// <summary>
    /// Get small luxury products (client-side filtering from all products).
    /// Small luxuries are: IDs 4, 5, and 6.
    /// </summary>
    public async Task<List<Product>> GetSmallLuxuriesAsync()
    {
        try
        {
            _logger.LogDebug("Fetching small luxury products");

            var allProducts = await GetProductsAsync();
            var smallLuxuries = allProducts.Where(p => p.Id is 4 or 5 or 6).ToList();

            _logger.LogInformation("Successfully fetched {SmallLuxuriesCount} small luxury products", smallLuxuries.Count);
            return smallLuxuries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching small luxury products");
            return [];
        }
    }

    /// <summary>
    /// Get product recommendations (client-side filtering from all products).
    /// Recommendations are: IDs 4, 3, 6, and 11.
    /// </summary>
    public async Task<List<Product>> GetRecommendationsAsync(int productId)
    {
        try
        {
            _logger.LogDebug("Fetching recommendations for product {ProductId}", productId);

            var allProducts = await GetProductsAsync();
            var recommendations = allProducts.Where(p => p.Id is 4 or 3 or 6 or 11).ToList();

            _logger.LogInformation("Successfully fetched {RecommendationCount} recommendations for product {ProductId}",
                recommendations.Count, productId);
            return recommendations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recommendations for product {ProductId}", productId);
            return [];
        }
    }

    /// <summary>
    /// Search products by name/description using the hybrid search API endpoint.
    /// </summary>
    public async Task<List<Product>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            _logger.LogDebug("Searching products with query: {Query}", query);

            var response = await _httpClient.GetAsync(
                $"/api/products/search?q={Uri.EscapeDataString(query)}&top=20", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Search failed: HTTP {StatusCode}", response.StatusCode);
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

            _logger.LogInformation("Search for '{Query}' returned {Count} results", query, products.Count);
            return products;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Search cancelled for query: {Query}", query);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products with query: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// Get gift enhancements (hardcoded - no API endpoint available).
    /// These are returned locally as they're not part of the ProductsAPI.
    /// </summary>
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

    /// <summary>
    /// Get the category name to ID mapping for resolving category IDs from product responses.
    /// Fetches all categories if not cached, then builds a dictionary for lookups.
    /// </summary>
    private async Task<Dictionary<string, int>> GetCategoryNameToIdMappingAsync()
    {
        // Check cache first
        if (_cache.TryGetValue(CategoryNameToIdMapKey, out Dictionary<string, int>? cachedMap))
        {
            return cachedMap ?? [];
        }

        try
        {
            var categories = await GetCategoriesAsync();
            var mapping = categories.ToDictionary(c => c.Name, c => c.Id);

            // Cache the mapping
            _cache.Set(CategoryNameToIdMapKey, mapping, CategoryCacheDuration);

            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building category name to ID mapping");
            return [];
        }
    }
}
