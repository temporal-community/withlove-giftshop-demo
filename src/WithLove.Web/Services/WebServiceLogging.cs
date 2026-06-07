using System.Net;
using Microsoft.Extensions.Logging;

namespace WithLove.Web.Services;

internal static partial class WebServiceLogging
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Merged anonymous cart {AnonKey} into {UserKey}")]
    internal static partial void MergedAnonymousCart(this ILogger logger, string anonKey, string userKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load cart for userId: {UserId}. Starting with empty cart.")]
    internal static partial void FailedToLoadCart(this ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist cart state to cache for key: {CacheKey}")]
    internal static partial void FailedToPersistCart(this ILogger logger, Exception exception, string cacheKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to ensure loyalty workflow for user {UserId} — continuing anyway")]
    internal static partial void FailedToEnsureLoyaltyWorkflow(this ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching product with ID: {ProductId}")]
    internal static partial void FetchingProduct(this ILogger logger, int productId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch product {ProductId}: HTTP {StatusCode}")]
    internal static partial void FailedToFetchProduct(this ILogger logger, int productId, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched product: {ProductId} - {ProductName}")]
    internal static partial void FetchedProduct(this ILogger logger, int productId, string productName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching product {ProductId}")]
    internal static partial void ErrorFetchingProduct(this ILogger logger, Exception exception, int productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching all products")]
    internal static partial void FetchingProducts(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Returning products from cache")]
    internal static partial void ReturningProductsFromCache(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch products: HTTP {StatusCode}")]
    internal static partial void FailedToFetchProducts(this ILogger logger, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {ProductCount} products")]
    internal static partial void FetchedProducts(this ILogger logger, int productCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching products")]
    internal static partial void ErrorFetchingProducts(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching products for category: {CategoryId}")]
    internal static partial void FetchingProductsForCategory(this ILogger logger, int categoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch products for category {CategoryId}: HTTP {StatusCode}")]
    internal static partial void FailedToFetchProductsForCategory(this ILogger logger, int categoryId, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {ProductCount} products for category {CategoryId}")]
    internal static partial void FetchedProductsForCategory(this ILogger logger, int productCount, int categoryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching products for category {CategoryId}")]
    internal static partial void ErrorFetchingProductsForCategory(this ILogger logger, Exception exception, int categoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching all categories")]
    internal static partial void FetchingCategories(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Returning categories from cache")]
    internal static partial void ReturningCategoriesFromCache(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch categories: HTTP {StatusCode}")]
    internal static partial void FailedToFetchCategories(this ILogger logger, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {CategoryCount} categories")]
    internal static partial void FetchedCategories(this ILogger logger, int categoryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching categories")]
    internal static partial void ErrorFetchingCategories(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching category with ID: {CategoryId}")]
    internal static partial void FetchingCategory(this ILogger logger, int categoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch category {CategoryId}: HTTP {StatusCode}")]
    internal static partial void FailedToFetchCategory(this ILogger logger, int categoryId, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched category: {CategoryId} - {CategoryName}")]
    internal static partial void FetchedCategory(this ILogger logger, int categoryId, string categoryName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching category {CategoryId}")]
    internal static partial void ErrorFetchingCategory(this ILogger logger, Exception exception, int categoryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching featured products")]
    internal static partial void FetchingFeaturedProducts(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {FeaturedCount} featured products")]
    internal static partial void FetchedFeaturedProducts(this ILogger logger, int featuredCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching featured products")]
    internal static partial void ErrorFetchingFeaturedProducts(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching small luxury products")]
    internal static partial void FetchingSmallLuxuryProducts(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {SmallLuxuriesCount} small luxury products")]
    internal static partial void FetchedSmallLuxuryProducts(this ILogger logger, int smallLuxuriesCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching small luxury products")]
    internal static partial void ErrorFetchingSmallLuxuryProducts(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetching recommendations for product {ProductId}")]
    internal static partial void FetchingRecommendations(this ILogger logger, int productId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully fetched {RecommendationCount} recommendations for product {ProductId}")]
    internal static partial void FetchedRecommendations(this ILogger logger, int recommendationCount, int productId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fetching recommendations for product {ProductId}")]
    internal static partial void ErrorFetchingRecommendations(this ILogger logger, Exception exception, int productId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching products with query: {Query}")]
    internal static partial void SearchingProducts(this ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Search failed: HTTP {StatusCode}")]
    internal static partial void SearchFailed(this ILogger logger, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search for '{Query}' returned {Count} results")]
    internal static partial void SearchReturnedResults(this ILogger logger, string query, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Search cancelled for query: {Query}")]
    internal static partial void SearchCancelled(this ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error searching products with query: {Query}")]
    internal static partial void ErrorSearchingProducts(this ILogger logger, Exception exception, string query);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error building category name to ID mapping")]
    internal static partial void ErrorBuildingCategoryMap(this ILogger logger, Exception exception);
}
