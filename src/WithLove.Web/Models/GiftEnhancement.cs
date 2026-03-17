namespace WithLove.Web.Models;

public class GiftEnhancement
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal Price { get; init; }
    public string ImageUrl { get; init; } = "";
    public bool IsSelected { get; set; }
}
