namespace WithLove.Web.Models;

public record Category
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string HeroTitle { get; init; } = "";
    public string HeroSubtitle { get; init; } = "";
    public string Image { get; init; } = "";
    public string HeroImage { get; init; } = "";
    public List<string> SubTypes { get; init; } = [];
    public List<string> Occasions { get; init; } = [];
}
