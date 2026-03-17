namespace WithLove.ProductsAPI.DTOs;

/// <summary>
/// Generic pagination wrapper for collections.
/// Follows Microsoft REST API Guidelines: value array + nextLink for continuation.
/// </summary>
/// <typeparam name="T">Type of items in the collection</typeparam>
/// <param name="Value">Array of items in current page</param>
/// <param name="NextLink">Optional URL for fetching next page (omitted if no more results)</param>
public record PaginatedResponse<T>(
    T[] Value,
    string? NextLink = null);
