namespace WithLove.ProductsAPI.DTOs;

/// <summary>
/// Serialization-safe wrapper for paginated cache entries.
/// ValueTuples don't round-trip through System.Text.Json, so FusionCache
/// needs a concrete type with named properties.
/// </summary>
public record CachedPage<T>(List<T> Items, int Total);
