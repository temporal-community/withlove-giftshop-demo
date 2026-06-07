using WithLove.Web.Models;
using ZiggyCreatures.Caching.Fusion;

namespace WithLove.Web.Services;

/// <summary>FusionCache-backed cart with an in-memory snapshot for synchronous UI reads.</summary>
public class FusionCacheCartService : ICartService
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheCartService> _logger;

    private List<CartItem> _items = [];
    private List<GiftEnhancement> _enhancements = [];
    private string _cacheKey = "";

    public event Action? OnChange;

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

    /// <summary>Loads the cart and merges an anonymous cart when supplied.</summary>
    public async Task InitializeAsync(string userId, string? anonymousCartId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

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
                    _logger.MergedAnonymousCart(anonKey, _cacheKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.FailedToLoadCart(ex, userId);
            _items = [];
            _enhancements = [];
        }

        OnChange?.Invoke();
    }

    /// <summary>Adds an item, merging quantity when the product already exists.</summary>
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

    /// <summary>Removes all quantities of a product.</summary>
    public async Task RemoveItemAsync(int productId)
    {
        _items.RemoveAll(i => i.ProductId == productId);
        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>Updates quantity, removing the item when quantity is zero.</summary>
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

    /// <summary>Toggles a gift enhancement.</summary>
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

    /// <summary>Clears items and enhancements.</summary>
    public async Task ClearAsync()
    {
        _items.Clear();
        _enhancements.Clear();
        OnChange?.Invoke();
        await PersistAsync();
    }

    /// <summary>Persists the current cart state with a 30-day TTL.</summary>
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
            _logger.FailedToPersistCart(ex, _cacheKey);
        }
    }
}
