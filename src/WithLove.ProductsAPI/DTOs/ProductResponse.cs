namespace WithLove.ProductsAPI.DTOs;

/// <summary>
/// API response DTO for a single product.
/// </summary>
public record ProductResponse(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    string? ImageUrl,
    string CategoryName,
    string? StripePriceId,
    string? SubCategory,
    List<ProductMaterialResponse>? Materials,
    List<ProductFeatureResponse>? Features,
    string? StoryTitle,
    string? StoryDescription,
    bool IsEnabled,
    DateTime AddedDate,
    DateTime UpdatedDate);
