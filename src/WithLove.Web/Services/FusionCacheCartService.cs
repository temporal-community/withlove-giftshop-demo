using WithLove.Web.Models;
using ZiggyCreatures.Caching.Fusion;

namespace WithLove.Web.Services;

/// <summary>
/// FusionCache-backed cart service with hybrid model:
/// - In-memory snapshot for fast synchronous reads
/// - Async FusionCache (L1 memory + L2 Redis) for persistence
///
/// On mutation: update local snapshot immediately, fire OnChange (instant UI),
/// then await write to FusionCache (fire-and-forget pattern).
///
/// Cache key format: cart:{userId}
/// TTL: 30 days (abandoned cart support)
/// </summary>
public class FusionCacheCartService : ICartService
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheCartService> _logger;

    // Local in-memory snapshot
    private List<CartItem> _items = [];
    private List<GiftEnhancement> _enhancements = [];
    private string _cacheKey = "";

    public event Action? OnChange;

    // Synchronous computed reads from local snapshot
    public IReadOnlyList<CartItem> Items => _items.AsReadOnly();
    public int ItemCount => _items.Sum(i => i.Quantity);
    public decimal Subtotal => _items.Sum(i => i.Price * i.Quantity);
    public decimal EnhancementsTotal => _enhancements.Sum(e => e.Price);
    public decimal Total => Subtotal + EnhancementsTotal;
    public List<GiftEnhancement> Enhancements => _enhancements;

    public FusionCacheCartService(IFusionCache cache, ILogger<FusionCacheCartService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initialize cart from FusionCache or start with empty cart.
    /// If anonymousCartId is provided and differs from userId, merges the anonymous cart
    /// into the user's cart and removes the anonymous cart key.
    /// </summary>
    public async Task InitializeAsync(string userId, string? anonymousCartId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Already initialized in this scope (e.g., CartBadge already called this)
        if (!string.IsNullOrEmpty(_cacheKey))
            return;

        _cacheKey = $"cart:{userId}";

        try
        {
            var state = await _cache.GetOrDefaultAsync<CartState>(_cacheKey);
            _items = state?.Items ?? [];
            _enhancements = state?.Enhancements ?? [];

            if (!string.IsNullOrEmpty(anonymousCartId) && anonymousCartId != userId)
            {
                var anonKey = $"cart:{anonymousCartId}";
                var anonState = await _cache.GetOrDefaultAsync<CartState>(anonKey);
                if (anonState?.Items.Count > 0)
                {
                    foreach (var anonItem in anonState.Items)
                    {
                        var existing = _items.FirstOrDefault(i => i.ProductId == anonItem.ProductId);
                        if (existing is not null)
                            existing.Quantity += anonItem.Quantity;
                        else
                            _items.Add(anonItem);
                    }
                    foreach (var anonEnh in anonState.Enhancements)
                    {
                        if (!_enhancements.Any(e => e.Id == anonEnh.Id))
                            _enhancements.Add(anonEnh);
                    }
                    await PersistAsync();
                    await _cache.RemoveAsync(anonKey);
                    _logger.LogInformation("Merged anonymous cart {AnonKey} into {UserKey}", anonKey, _cacheKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cart for userId: {UserId}. Starting with empty cart.", userId);
            _items = [];
            _enhancements = [];
        }

        OnChange?.Invoke();
    }

    /// <summary>
    /// Add item to cart (merge quantity if product already exists).
    /// Updates local snapshot immediately, fires OnChange, then persists to cache.
    /// </summary>
    public async Task AddItemAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var existing = _items.FirstOrDefault(i => i.ProductId == item.ProductId);
        if (existing is not null)
        {
            existing.Quantity += item.Quantity;
        }
        else
        {
            _items.Add(item);
        }

        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>
    /// Remove all quantities of a product from cart.
    /// </summary>
    public async Task RemoveItemAsync(int productId)
    {
        _items.RemoveAll(i => i.ProductId == productId);
        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>
    /// Update quantity for a product. Removes item if quantity <= 0.
    /// </summary>
    public async Task UpdateQuantityAsync(int productId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            if (quantity <= 0)
            {
                _items.Remove(item);
            }
            else
            {
                item.Quantity = quantity;
            }
        }

        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>
    /// Toggle a gift enhancement on/off. If not selected, adds it; if selected, removes it.
    /// </summary>
    public async Task ToggleEnhancementAsync(GiftEnhancement enhancement)
    {
        ArgumentNullException.ThrowIfNull(enhancement);

        var existing = _enhancements.FirstOrDefault(e => e.Id == enhancement.Id);
        if (existing is not null)
            _enhancements.Remove(existing);
        else
            _enhancements.Add(enhancement);

        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>
    /// Clear all items and enhancements from cart.
    /// </summary>
    public async Task ClearAsync()
    {
        _items.Clear();
        _enhancements.Clear();
        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>
    /// Persist current cart state to FusionCache with 30-day TTL.
    /// </summary>
    private async Task PersistAsync()
    {
        try
        {
            var state = new CartState(_items, _enhancements);
            await _cache.SetAsync(
                _cacheKey,
                state,
                new FusionCacheEntryOptions { Duration = TimeSpan.FromDays(30) }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist cart state to cache for key: {CacheKey}", _cacheKey);
            // Don't throw — cache failure shouldn't break the app.
            // User still has local snapshot; persistence will retry on next mutation.
        }
    }
}
