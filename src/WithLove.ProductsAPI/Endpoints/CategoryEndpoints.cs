using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WithLove.Data;
using WithLove.Data.Models;
using WithLove.ProductsAPI.DTOs;
using WithLove.ProductsAPI.Filters;
using WithLove.ProductsAPI.Utilities;
using ZiggyCreatures.Caching.Fusion;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace WithLove.ProductsAPI.Endpoints;

/// <summary>
/// Category API endpoints (read-only).
/// Admin operations (create/update/delete) are out of scope.
/// All endpoints require X-WITHLOVE-API-VERSION header.
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/categories");
        
        // Apply API version validation to all endpoints
        group.AddEndpointFilter<ApiVersionValidationFilter>();

        // List all categories with pagination
        group.MapGet("/", GetCategories)
            .WithName("GetCategories")
            .WithSummary("List all categories")
            .WithDescription("Retrieves a paginated list of all product categories. Categories are cached for 30 minutes.")
            .WithTags("Categories");

        // Get single category by ID
        group.MapGet("/{id}", GetCategoryById)
            .WithName("GetCategoryById")
            .WithSummary("Get a single category by ID")
            .WithDescription("Retrieves a single category by its ID. Supports conditional requests via If-None-Match header for caching. Categories are cached for 30 minutes.")
            .WithTags("Categories");
    }

    private static async Task<Ok<PaginatedResponse<CategoryResponse>>> GetCategories(
        ProductsDbContext dbContext,
        IFusionCache cache,
        int top = 10,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        top = Math.Min(top, 100);
        if (top < 1) top = 10;
        if (skip < 0) skip = 0;

        var pageNumber = (skip / top) + 1;
        var cacheKey = $"categories:v2:page{pageNumber}:size{top}";

        var result = await cache.GetOrSetAsync(
            cacheKey,
            async (ctx) =>
            {
                var query = dbContext.Categories.AsNoTracking();
                var count = await query.CountAsync(ctx);
                var items = await query
                    .OrderBy(c => c.Name)
                    .Skip((pageNumber - 1) * top)
                    .Take(top)
                    .ToListAsync(ctx);

                return new CachedPage<Category>(items, count);
            },
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(30) },
            cancellationToken);

        var responses = (result.Items ?? []).Select(CategoryToResponse).ToArray();
        var hasMore = (skip + top) < result.Total;
        var nextLink = hasMore ? $"/api/categories?top={top}&skip={skip + top}" : null;

        return Ok(new PaginatedResponse<CategoryResponse>(responses, nextLink));
    }

    private static async Task<IResult> GetCategoryById(
        int id,
        ProductsDbContext dbContext,
        IFusionCache cache,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"category:{id}";

        var category = await cache.GetOrSetAsync(
            cacheKey,
            async (ctx) => await dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, ctx),
            new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(30) },
            cancellationToken);

        if (category == null)
            return ProblemDetailsResults.NotFound(
                detail: $"Category {id} does not exist.",
                instance: $"/api/categories/{id}");

        var response = CategoryToResponse(category);

        // Support conditional requests (If-None-Match and If-Modified-Since)
        var etag = ETagGenerator.GenerateETag(category.RowVersion);
        var lastModified = category.UpdatedDate.ToString("R");

        // Check If-None-Match (ETag-based)
        if (httpContext.Request.Headers.TryGetValue("If-None-Match", out var clientETag) &&
            ETagGenerator.VerifyETag(clientETag.ToString(), category.RowVersion))
        {
            return StatusCode(304); // Not Modified
        }

        // Check If-Modified-Since (date-based)
        if (httpContext.Request.Headers.TryGetValue("If-Modified-Since", out var modifiedSince) &&
            DateTimeOffset.TryParse(modifiedSince.ToString(), out var clientDate))
        {
            // If the resource hasn't been modified since the client's date, return 304
            if (category.UpdatedDate <= clientDate)
            {
                return StatusCode(304); // Not Modified
            }
        }

        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers["Last-Modified"] = lastModified;

        return Ok(response);
    }

    private static CategoryResponse CategoryToResponse(Category category)
    {
        return new CategoryResponse(
            category.Id,
            category.Name,
            category.Description,
            category.HeroTitle,
            category.HeroSubtitle,
            category.Image,
            category.HeroImage,
            category.SubTypes.Count > 0 ? category.SubTypes : null,
            category.Occasions.Count > 0 ? category.Occasions : null);
    }
}
