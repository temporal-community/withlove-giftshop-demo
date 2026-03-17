namespace WithLove.ProductsAPI.DTOs;

/// <summary>
/// API response DTO for a category.
/// ETag for optimistic concurrency control is returned via the ETag response header, not in the body.
/// </summary>
public record CategoryResponse(
    int Id,
    string Name,
    string? Description,
    string HeroTitle,
    string? HeroSubtitle,
    string? Image,
    string? HeroImage,
    List<string>? SubTypes,
    List<string>? Occasions);
