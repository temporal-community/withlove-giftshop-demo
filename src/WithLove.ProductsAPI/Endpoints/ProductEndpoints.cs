using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WithLove.Data;
using WithLove.ProductsAPI.DTOs;
using DataModels = WithLove.Data.Models;
using WithLove.ProductsAPI.Filters;
using WithLove.ProductsAPI.Services;
using WithLove.ProductsAPI.Utilities;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace WithLove.ProductsAPI.Endpoints;

/// <summary>
/// Product API endpoints (read-only).
/// All endpoints require X-WITHLOVE-API-VERSION header.
/// </summary>
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products");
        
        // Apply API version validation to all endpoints
        group.AddEndpointFilter<ApiVersionValidationFilter>();

        // List all products with pagination
        group.MapGet("/", GetProductList)
            .WithName("GetProducts")
            .WithSummary("List all products with pagination")
            .WithDescription("Retrieves a paginated list of active products. Supports sorting by name, price, or added date.")
            .WithTags("Products");

        // Get single product by ID
        group.MapGet("/{id}", GetProductById)
            .WithName("GetProductById")
            .WithSummary("Get a single product by ID")
            .WithDescription("Retrieves a single product by its ID. Supports conditional requests via If-None-Match header for caching.")
            .WithTags("Products");

        // Search products by name
        group.MapGet("/search", SearchProducts)
            .WithName("SearchProducts")
            .WithSummary("Search products by name")
            .WithDescription("Searches for products by name using case-insensitive substring matching. Returns paginated results.")
            .WithTags("Products");

        // Get products by category
        group.MapGet("/category/{categoryId}", GetProductsByCategory)
            .WithName("GetProductsByCategory")
            .WithSummary("Get products by category")
            .WithDescription("Retrieves all products in a specific category. Returns 404 if the category does not exist.")
            .WithTags("Products");
    }

    private static async Task<Ok<PaginatedResponse<ProductResponse>>> GetProductList(
        IProductCacheService cacheService,
        int top = 10,
        int skip = 0,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        top = Math.Min(top, 100);
        if (top < 1) top = 10;
        if (skip < 0) skip = 0;

        var pageNumber = (skip / top) + 1;

        var result = await cacheService.GetProductListAsync(pageNumber, top, orderBy, cancellationToken);

        var responses = (result.Items ?? []).Select(ProductToResponse).ToArray();
        var hasMore = (skip + top) < result.Total;
        var nextLink = hasMore ? $"/api/products?top={top}&skip={skip + top}&orderby={Uri.EscapeDataString(orderBy)}" : null;

        return Ok(new PaginatedResponse<ProductResponse>(responses, nextLink));
    }

    private static async Task<IResult> GetProductById(
        int id,
        IProductCacheService cacheService,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var product = await cacheService.GetProductByIdAsync(id, cancellationToken);
        if (product == null)
            return ProblemDetailsResults.NotFound($"Product {id} does not exist", instance: $"/api/products/{id}");

        var response = ProductToResponse(product);

        // Support conditional requests (If-None-Match and If-Modified-Since)
        var etag = ETagGenerator.GenerateETag(product.RowVersion);
        var lastModified = product.UpdatedDate.ToString("R");

        // Check If-None-Match (ETag-based)
        if (httpContext.Request.Headers.TryGetValue("If-None-Match", out var clientETag) &&
            ETagGenerator.VerifyETag(clientETag.ToString(), product.RowVersion))
        {
            return StatusCode(304); // Not Modified
        }

        // Check If-Modified-Since (date-based)
        if (httpContext.Request.Headers.TryGetValue("If-Modified-Since", out var modifiedSince) &&
            DateTimeOffset.TryParse(modifiedSince.ToString(), out var clientDate))
        {
            // If the resource hasn't been modified since the client's date, return 304
            if (product.UpdatedDate <= clientDate)
            {
                return StatusCode(304); // Not Modified
            }
        }

        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers["Last-Modified"] = lastModified;

        return Ok(response);
    }

    private static async Task<IResult> SearchProducts(
        string? q,
        IProductCacheService cacheService,
        int top = 10,
        int skip = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return ProblemDetailsResults.BadRequest(
                detail: "The 'q' parameter is required.",
                instance: "/api/products/search");

        top = Math.Min(top, 100);
        if (top < 1) top = 10;
        if (skip < 0) skip = 0;

        var pageNumber = (skip / top) + 1;
        var result = await cacheService.SearchProductsAsync(q, pageNumber, top, cancellationToken);

        var responses = (result.Items ?? []).Select(ProductToResponse).ToArray();
        var hasMore = (skip + top) < result.Total;
        var nextLink = hasMore ? $"/api/products/search?q={Uri.EscapeDataString(q)}&top={top}&skip={skip + top}" : null;

        return Ok(new PaginatedResponse<ProductResponse>(responses, nextLink));
    }

    private static async Task<IResult> GetProductsByCategory(
        int categoryId,
        IProductCacheService cacheService,
        ProductsDbContext dbContext,
        int top = 10,
        int skip = 0,
        string orderBy = "addedDate desc",
        CancellationToken cancellationToken = default)
    {
        // Verify category exists
        var categoryExists = await dbContext.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken);
        if (!categoryExists)
            return ProblemDetailsResults.NotFound(
                detail: $"Category {categoryId} does not exist.",
                instance: $"/api/products/category/{categoryId}");

        top = Math.Min(top, 100);
        if (top < 1) top = 10;
        if (skip < 0) skip = 0;

        var pageNumber = (skip / top) + 1;
        var result = await cacheService.GetProductsByCategoryAsync(categoryId, pageNumber, top, orderBy, cancellationToken);

        var responses = (result.Items ?? []).Select(ProductToResponse).ToArray();
        var hasMore = (skip + top) < result.Total;
        var nextLink = hasMore ? $"/api/products/category/{categoryId}?top={top}&skip={skip + top}&orderby={Uri.EscapeDataString(orderBy)}" : null;

        return Ok(new PaginatedResponse<ProductResponse>(responses, nextLink));
    }

    private static ProductResponse ProductToResponse(DataModels.Product product)
    {
        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Price,
            product.ImageUrl,
            product.Category!.Name,
            product.StripePriceId,
            product.SubCategory,
            product.Materials.Count > 0 ? product.Materials.Select(m => new ProductMaterialResponse(m.Icon, m.Name)).ToList() : null,
            product.Features.Count > 0 ? product.Features.Select(f => new ProductFeatureResponse(f.Icon, f.Title, f.Description)).ToList() : null,
            product.StoryTitle,
            product.StoryDescription,
            product.IsEnabled,
            product.AddedDate,
            product.UpdatedDate);
    }
}
