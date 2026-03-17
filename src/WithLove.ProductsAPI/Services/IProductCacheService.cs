using WithLove.Data.Models;
using WithLove.ProductsAPI.DTOs;

namespace WithLove.ProductsAPI.Services;

/// <summary>
/// Caching service for products with intelligent cache invalidation strategies.
/// Reduces database load and improves response times using FusionCache with fail-safe mode.
/// </summary>
public interface IProductCacheService
{
    /// <summary>
    /// Gets a single product by ID from cache or database. 10-minute TTL.
    /// </summary>
    Task<Product?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated product list from cache or database. 5-minute TTL.
    /// </summary>
    Task<CachedPage<Product>> GetProductListAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets products for a specific category. 5-minute TTL per category.
    /// </summary>
    Task<CachedPage<Product>> GetProductsByCategoryAsync(
        int categoryId,
        int pageNumber = 1,
        int pageSize = 10,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches products by name using hybrid search (FTS + vector). Results are ranked by
    /// Reciprocal Rank Fusion relevance score, not by a caller-specified sort order. 5-minute TTL per query.
    /// </summary>
    Task<CachedPage<Product>> SearchProductsAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cache for a specific product after create/update/delete operations.
    /// </summary>
    Task InvalidateProductCacheAsync(int productId);

    /// <summary>
    /// Invalidates all product list and category caches.
    /// Called when product is created, updated, or deleted.
    /// </summary>
    Task InvalidateProductListCacheAsync();

    /// <summary>
    /// Invalidates caches related to a specific category.
    /// Called when category content changes.
    /// </summary>
    Task InvalidateCategoryCacheAsync(int categoryId);

    /// <summary>
    /// Invalidates all product search caches.
    /// Called when product data changes.
    /// </summary>
    Task InvalidateSearchCacheAsync();
}
