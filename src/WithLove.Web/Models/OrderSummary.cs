namespace WithLove.Web.Models;

public record OrderSummaryItem(string Name, string? Detail, decimal Price);

public record OrderSummary(
    List<OrderSummaryItem> Items,
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal Total);
