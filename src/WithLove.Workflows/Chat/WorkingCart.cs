namespace WithLove.Workflows.Chat;

/// <summary>
/// Mutable working copy of a cart snapshot for a single inference turn.
/// Cart-mutating tools update this so view_cart reflects mutations within the same turn.
/// Diverges from the real cart if mutations fail client-side; self-corrects on the next
/// message when the caller passes a fresh snapshot.
/// </summary>
internal sealed class WorkingCart
{
    private readonly List<CartSnapshot> _items;

    public WorkingCart(IEnumerable<CartSnapshot> initial)
    {
        _items = initial.Select(c => c with { }).ToList();
    }

    public IReadOnlyList<CartSnapshot> Items => _items;

    public void Add(int productId, string productName, decimal price, int quantity)
    {
        var existing = _items.FirstOrDefault(c => c.ProductId == productId);
        if (existing is not null)
        {
            _items.Remove(existing);
            _items.Add(existing with { Quantity = existing.Quantity + quantity });
        }
        else
        {
            _items.Add(new CartSnapshot(productId, productName, price, quantity));
        }
    }

    public void Remove(int productId)
    {
        _items.RemoveAll(c => c.ProductId == productId);
    }

    public void Clear()
    {
        _items.Clear();
    }

    public CartSnapshot? FindById(int productId) =>
        _items.FirstOrDefault(c => c.ProductId == productId);

    public string Summarize()
    {
        if (_items.Count == 0)
            return "The cart is empty.";

        var lines = _items
            .Select(c => $"- ID: {c.ProductId} | {c.ProductName} | ${c.Price:F2} x {c.Quantity}")
            .ToList();

        var total = _items.Sum(c => c.Price * c.Quantity);
        lines.Add($"Total: ${total:F2} ({_items.Count} item{(_items.Count != 1 ? "s" : "")})");

        return string.Join("\n", lines);
    }
}
