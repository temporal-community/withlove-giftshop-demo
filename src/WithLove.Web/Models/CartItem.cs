namespace WithLove.Web.Models;

public class CartItem
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public decimal Price { get; init; }
    public string StripePriceId { get; init; } = "";
    public int Quantity { get; set; } = 1;
    public string? PersonalNote { get; set; }
}
