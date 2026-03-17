namespace WithLove.Web.Models;

public record OrderHistoryItem(
    string OrderNumber, string Status, string StatusStyle,
    string DatePlaced, decimal Total,
    List<OrderProductThumbnail> Thumbnails, int ExtraItemCount,
    string PrimaryAction, string PrimaryActionStyle);

public record OrderProductThumbnail(string ImageUrl, string AltText);

public record SavedAddress(
    string Label, string TypeBadge, string BadgeStyle,
    string RecipientName, List<string> AddressLines,
    string IconName, bool IsDefault);
