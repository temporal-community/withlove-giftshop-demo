namespace WithLove.Web.Models;

/// <summary>A single line item within an order, representing either a catalog product or a gift enhancement.</summary>
public record OrderLineItemView(
    /// <summary>The catalog product ID, or <c>null</c> for gift enhancements not in the product catalog.</summary>
    int? ProductId,
    /// <summary>The Stripe Price ID used when the item was purchased.</summary>
    string? StripePriceId,
    /// <summary>Display name of the product or enhancement.</summary>
    string ProductName,
    /// <summary>URL of the product image, or <c>null</c> for enhancements.</summary>
    string? ImageUrl,
    /// <summary>Number of units purchased.</summary>
    int Quantity,
    /// <summary>Per-unit price as actually charged (from Stripe, not the current catalog price).</summary>
    decimal UnitPrice,
    /// <summary><c>true</c> if this item is a gift enhancement (e.g. gift wrap) rather than a catalog product.</summary>
    bool IsEnhancement);

/// <summary>A summary view of a completed order, returned by the order list endpoint.</summary>
/// <remarks>
/// <see cref="LineItems"/> is always empty on the summary — line items are only fetched on the detail page.
/// </remarks>
public record OrderSummaryView(
    /// <summary>Human-readable confirmation number in "WL-XXXXXXX" format; also used as the URL route key.</summary>
    string ConfirmationNumber,
    /// <summary>Human-readable order status (e.g. "Confirmed").</summary>
    string Status,
    /// <summary>UTC date and time when the Stripe Checkout Session was created.</summary>
    DateTime PlacedAt,
    /// <summary>Total amount charged in dollars.</summary>
    decimal AmountTotal,
    /// <summary>Line items — always empty on the summary page; populated only on <see cref="OrderDetailView"/>.</summary>
    List<OrderLineItemView> LineItems);

/// <summary>Full detail view of a completed order including all line items.</summary>
public record OrderDetailView(
    /// <summary>Human-readable confirmation number in "WL-XXXXXXX" format.</summary>
    string ConfirmationNumber,
    /// <summary>Human-readable order status (e.g. "Confirmed").</summary>
    string Status,
    /// <summary>UTC date and time when the Stripe Checkout Session was created.</summary>
    DateTime PlacedAt,
    /// <summary>Total amount charged in dollars.</summary>
    decimal AmountTotal,
    /// <summary>All line items for this order, including products and gift enhancements.</summary>
    List<OrderLineItemView> LineItems);

/// <summary>
/// A cursor-based page of order summaries returned by <c>GetOrdersAsync</c>.
/// Supports the "Load More" pattern: pass <see cref="NextCursor"/> as the <c>cursor</c> argument
/// on the next call to retrieve the following page.
/// </summary>
public record OrdersPage(
    /// <summary>The order summaries for this page.</summary>
    IReadOnlyList<OrderSummaryView> Orders,
    /// <summary><c>true</c> if there are additional pages available.</summary>
    bool HasMore,
    /// <summary>
    /// The Stripe session ID to pass as <c>StartingAfter</c> on the next request,
    /// or <c>null</c> if this is the last page.
    /// </summary>
    string? NextCursor);

/// <summary>Saved shipping or billing address for a user's account.</summary>
public record SavedAddress(
    string Label, string TypeBadge, string BadgeStyle,
    string RecipientName, List<string> AddressLines,
    string IconName, bool IsDefault);
