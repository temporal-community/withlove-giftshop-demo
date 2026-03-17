namespace WithLove.Web.Models;

public record Product
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal Price { get; init; }
    public string StripePriceId { get; init; } = "";
    public int CategoryId { get; init; }
    public string? SubCategory { get; init; }
    public string? ImageUrl { get; init; }
    public List<ProductMaterial> Materials { get; init; } = [];
    public List<ProductFeature> Features { get; init; } = [];
    public string? StoryTitle { get; init; }
    public string? StoryDescription { get; init; }
}
