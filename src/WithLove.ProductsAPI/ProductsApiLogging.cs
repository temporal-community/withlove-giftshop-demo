namespace WithLove.ProductsAPI.Filters
{
    internal static partial class ApiFilterLogging
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Missing {HeaderName} header in request")]
        internal static partial void MissingApiVersionHeader(this ILogger logger, string headerName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid API version format: {Version}")]
        internal static partial void InvalidApiVersionFormat(this ILogger logger, string version);
    }
}

namespace WithLove.ProductsAPI.Middleware
{
    internal static partial class ApiMiddlewareLogging
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception occurred: {ExceptionType} - {Message}")]
        internal static partial void UnhandledException(this ILogger logger, Exception exception, string exceptionType, string message);
    }
}

namespace WithLove.ProductsAPI.Services
{
    internal static partial class ProductApiServiceLogging
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Generated embedding for product {ProductId}: {ProductName}")]
        internal static partial void GeneratedProductEmbedding(this ILogger logger, int productId, string productName);

        [LoggerMessage(Level = LogLevel.Information, Message = "All products already have embeddings")]
        internal static partial void ProductsAlreadyEmbedded(this ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Generating embeddings for {Count} products")]
        internal static partial void GeneratingEmbeddings(this ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Generated embeddings for {Count} products")]
        internal static partial void GeneratedEmbeddings(this ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated cache for product {ProductId}")]
        internal static partial void InvalidatedProductCache(this ILogger logger, int productId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated product list cache")]
        internal static partial void InvalidatedProductListCache(this ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated cache for category {CategoryId}")]
        internal static partial void InvalidatedCategoryCache(this ILogger logger, int categoryId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Invalidated all search caches")]
        internal static partial void InvalidatedSearchCaches(this ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Error in FetchProductListAsync: {Message}")]
        internal static partial void FetchProductListFailed(this ILogger logger, Exception exception, string message);
    }
}
