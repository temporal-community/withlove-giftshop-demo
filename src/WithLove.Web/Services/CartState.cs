using WithLove.Web.Models;

namespace WithLove.Web.Services;

/// <summary>
/// Internal serialization record for persisting cart state to FusionCache.
/// Not exposed via the ICartService interface.
/// </summary>
internal record CartState(List<CartItem> Items, List<GiftEnhancement> Enhancements);
