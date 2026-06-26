using System.Diagnostics;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WithLove.Data;
using WithLove.Data.Models;
using WithLove.ProductsAPI.DTOs;
using ZiggyCreatures.Caching.Fusion;

namespace WithLove.ProductsAPI.Services;

/// <summary>
/// Implements product caching with FusionCache.
/// Automatically invalidates caches when products are modified.
/// Uses different TTLs for different query types based on access patterns.
/// </summary>
public partial class ProductCacheService : IProductCacheService
{
    private readonly ProductsDbContext _dbContext;
    private readonly IFusionCache _cache;
    private readonly ILogger<ProductCacheService> _logger;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly Instrumentation _instrumentation;

    private const string ProductKeyPrefix = "product:";
    private const string ProductListKey = "product:v2:all";
    private const string ProductCategoryKeyPrefix = "product:v2:category:";
    private const string ProductSearchKeyPrefix = "product:v2:search:";

    public ProductCacheService(
        ProductsDbContext dbContext,
        IFusionCache cache,
        ILogger<ProductCacheService> logger,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        Instrumentation instrumentation)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _instrumentation = instrumentation ?? throw new ArgumentNullException(nameof(instrumentation));
    }

    public async Task<Product?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{ProductKeyPrefix}{id}";

        var cached = await _cache.TryGetAsync<Product?>(cacheKey, token: cancellationToken);
        _instrumentation.CacheRequests.Add(1, new TagList
        {
            { "operation", "get_by_id" },
            { "cache_result", cached.HasValue ? "hit" : "miss" },
        });

        if (cached.HasValue)
            return cached.Value;

        return await _cache.GetOrSetAsync(
            cacheKey,
            async (ctx) => await FetchProductByIdAsync(id, ctx),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
            cancellationToken);
    }

    public async Task<CachedPage<Product>> GetProductListAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{ProductListKey}:page{pageNumber}:size{pageSize}:order{orderBy.Replace(" ", "")}";

        var cached = await _cache.TryGetAsync<CachedPage<Product>>(cacheKey, token: cancellationToken);
        _instrumentation.CacheRequests.Add(1, new TagList
        {
            { "operation", "list" },
            { "cache_result", cached.HasValue ? "hit" : "miss" },
        });

        if (cached.HasValue)
            return cached.Value;

        return await _cache.GetOrSetAsync(
            cacheKey,
            async (ctx) => await FetchProductListAsync(pageNumber, pageSize, orderBy, ctx),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            cancellationToken);
    }

    public async Task<CachedPage<Product>> GetProductsByCategoryAsync(
        int categoryId,
        int pageNumber = 1,
        int pageSize = 10,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{ProductCategoryKeyPrefix}{categoryId}:page{pageNumber}:size{pageSize}:order{orderBy.Replace(" ", "")}";

        _instrumentation.CategoryRequests.Add(1, new KeyValuePair<string, object?>("category_id", categoryId.ToString()));

        var cached = await _cache.TryGetAsync<CachedPage<Product>>(cacheKey, token: cancellationToken);
        _instrumentation.CacheRequests.Add(1, new TagList
        {
            { "operation", "by_category" },
            { "cache_result", cached.HasValue ? "hit" : "miss" },
        });

        if (cached.HasValue)
            return cached.Value;

        return await _cache.GetOrSetAsync(
            cacheKey,
            async (ctx) => await FetchProductsByCategoryAsync(categoryId, pageNumber, pageSize, orderBy, ctx),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            cancellationToken);
    }

    public async Task<CachedPage<Product>> SearchProductsAsync(
        string query,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{ProductSearchKeyPrefix}{query}:page{pageNumber}:size{pageSize}";

        var result = await _cache.GetOrSetAsync(
            cacheKey,
            async (ctx) => await FetchSearchResultsAsync(query, pageNumber, pageSize, ctx),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
            ["search"],
            cancellationToken);

        return result;
    }

    public async Task InvalidateProductCacheAsync(int productId)
    {
        var cacheKey = $"{ProductKeyPrefix}{productId}";
        await _cache.RemoveAsync(cacheKey);
        _logger.InvalidatedProductCache(productId);
    }

    public async Task InvalidateProductListCacheAsync()
    {
        await _cache.RemoveAsync(ProductListKey);
        _logger.InvalidatedProductListCache();
    }

    public async Task InvalidateCategoryCacheAsync(int categoryId)
    {
        var cacheKey = $"{ProductCategoryKeyPrefix}{categoryId}";
        await _cache.RemoveAsync(cacheKey);
        _logger.InvalidatedCategoryCache(categoryId);
    }

    public async Task InvalidateSearchCacheAsync()
    {
        await _cache.RemoveByTagAsync("search");
        _logger.InvalidatedSearchCaches();
    }

    private async Task<Product?> FetchProductByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && p.IsEnabled, cancellationToken);
    }

    private async Task<CachedPage<Product>> FetchProductListAsync(
        int pageNumber,
        int pageSize,
        string orderBy,
        CancellationToken cancellationToken)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (string.IsNullOrWhiteSpace(orderBy)) orderBy = "addedDate desc";

        try
        {
            var query = _dbContext.Products;
            if (query == null)
                throw new InvalidOperationException("DbContext or Products DbSet is null");

            var filtered = query
                .AsNoTracking()
                .Include(p => p.Category)
                .Where(p => p.IsEnabled);

            var total = await filtered.CountAsync(cancellationToken);

            var sorted = ApplySorting(filtered, orderBy);
            if (sorted == null)
                throw new InvalidOperationException("ApplySorting returned null");

            var products = await sorted
                .Skip(Math.Max(0, (pageNumber - 1) * pageSize))
                .Take(Math.Max(1, pageSize))
                .ToListAsync(cancellationToken);

            return new CachedPage<Product>(products, total);
        }
        catch (Exception ex)
        {
            _logger.FetchProductListFailed(ex, ex.Message);
            throw;
        }
    }

    private async Task<CachedPage<Product>> FetchProductsByCategoryAsync(
        int categoryId,
        int pageNumber,
        int pageSize,
        string orderBy,
        CancellationToken cancellationToken)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;

        var query = _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.CategoryId == categoryId && p.IsEnabled);

        var total = await query.CountAsync(cancellationToken);

        var products = await ApplySorting(query, orderBy)
            .Skip(Math.Max(0, (pageNumber - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .ToListAsync(cancellationToken);

        return new CachedPage<Product>(products, total);
    }

    private async Task<CachedPage<Product>> FetchSearchResultsAsync(
        string query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new CachedPage<Product>([], 0);

        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;

        using var activity = _instrumentation.ActivitySource.StartActivity("product.search");

        List<(int ProductId, int Rank)> ftsResults;
        var ftsUsedFallback = false;
        try
        {
            var ftsRaw = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.IsEnabled &&
                    (EF.Functions.FreeText(p.Name, query) ||
                     EF.Functions.FreeText(p.Description!, query)))
                .Select(p => new { p.Id })
                .ToListAsync(cancellationToken);
            ftsResults = ftsRaw.Select((r, i) => (r.Id, Rank: i + 1)).ToList();
        }
        catch (Exception ex)
        {
            // Full-text search can be unavailable before SQL Server finishes indexing.
            LogSearchFtsFallback(_logger, ex);
            _instrumentation.SearchFallbacks.Add(1);
            ftsUsedFallback = true;
            var fallbackRaw = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.IsEnabled &&
                    (p.Name.ToLower().Contains(query.ToLower()) ||
                     (p.Description != null && p.Description.ToLower().Contains(query.ToLower()))))
                .Select(p => new { p.Id })
                .ToListAsync(cancellationToken);
            ftsResults = fallbackRaw.Select((r, i) => (r.Id, Rank: i + 1)).ToList();
        }

        // Cosine distance ranges from 0 (identical) to 2 (opposite).
        const float maxCosineDistance = 0.8f;

        List<(int ProductId, int Rank)> vectorResults = [];
        try
        {
            var embeddingResult = await _embeddingGenerator.GenerateAsync(
                [query], cancellationToken: cancellationToken);
            var queryEmbedding = new SqlVector<float>(embeddingResult[0].Vector.ToArray());

            var vectorRaw = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.IsEnabled && p.Embedding != null
                    && EF.Functions.VectorDistance("cosine", p.Embedding!.Value, queryEmbedding) <= maxCosineDistance)
                .OrderBy(p => EF.Functions.VectorDistance("cosine", p.Embedding!.Value, queryEmbedding))
                .Take(50)
                .Select(p => new { p.Id })
                .ToListAsync(cancellationToken);
            vectorResults = vectorRaw.Select((r, i) => (r.Id, Rank: i + 1)).ToList();
        }
        catch (Exception ex)
        {
            LogSearchVectorFailed(_logger, ex);
            _instrumentation.VectorFailures.Add(1);
        }

        var rankedIds = MergeWithRrf(ftsResults, vectorResults);

        var strategy = ftsUsedFallback ? "fallback_like"
            : (ftsResults.Count > 0 && vectorResults.Count > 0) ? "hybrid"
            : ftsResults.Count > 0 ? "fts"
            : vectorResults.Count > 0 ? "vector"
            : "empty";

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("search.strategy", strategy);
            activity.SetTag("search.fts_count", ftsResults.Count);
            activity.SetTag("search.vector_count", vectorResults.Count);
            activity.SetTag("search.total_results", rankedIds.Count);
        }

        var tags = new TagList { { "strategy", strategy } };
        _instrumentation.SearchRequests.Add(1, tags);
        _instrumentation.SearchResultCount.Record(rankedIds.Count);

        var total = rankedIds.Count;

        var pageIds = rankedIds
            .Skip(Math.Max(0, (pageNumber - 1) * pageSize))
            .Take(Math.Max(1, pageSize))
            .ToList();

        if (pageIds.Count == 0)
            return new CachedPage<Product>([], total);

        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => pageIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var orderedProducts = pageIds
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<Product>()
            .ToList();

        return new CachedPage<Product>(orderedProducts, total);
    }

    /// <summary>Merges ranked search results using Reciprocal Rank Fusion.</summary>
    internal static List<int> MergeWithRrf(
        List<(int ProductId, int Rank)> primaryResults,
        List<(int ProductId, int Rank)> secondaryResults,
        double k = 60.0)
    {
        var scores = new Dictionary<int, double>();

        foreach (var (productId, rank) in primaryResults)
            scores[productId] = 1.0 / (k + rank);

        foreach (var (productId, rank) in secondaryResults)
        {
            scores.TryGetValue(productId, out var existing);
            scores[productId] = existing + 1.0 / (k + rank);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static IQueryable<Product> ApplySorting(IQueryable<Product> query, string orderBy)
    {
        return orderBy.ToLowerInvariant() switch
        {
            "name asc" => query.OrderBy(p => p.Name),
            "name desc" => query.OrderByDescending(p => p.Name),
            "price asc" => query.OrderBy(p => p.Price),
            "price desc" => query.OrderByDescending(p => p.Price),
            "addeddate asc" => query.OrderBy(p => p.AddedDate),
            _ => query.OrderByDescending(p => p.AddedDate)
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Full-text search failed, falling back to LIKE search")]
    private static partial void LogSearchFtsFallback(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector search failed, using keyword results only")]
    private static partial void LogSearchVectorFailed(ILogger logger, Exception ex);
}
