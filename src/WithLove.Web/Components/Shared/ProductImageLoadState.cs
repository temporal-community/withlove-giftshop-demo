namespace WithLove.Web.Components.Shared;

/// <summary>Tracks browser image failures and resets when a component receives a new source.</summary>
internal sealed class ProductImageLoadState
{
    private string? _source;
    private bool _failed;

    public bool ShouldRender(string? source)
    {
        if (!string.Equals(_source, source, StringComparison.Ordinal))
        {
            _source = source;
            _failed = false;
        }

        return !_failed && !string.IsNullOrWhiteSpace(source);
    }

    public void MarkFailed() => _failed = true;
}
