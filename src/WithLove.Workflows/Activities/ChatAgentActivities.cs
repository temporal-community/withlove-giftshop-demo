using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using WithLove.Workflows.Chat;

namespace WithLove.Workflows.Activities;

public class ChatAgentActivities(IChatClient chatClient, IHttpClientFactory httpClientFactory)
{
    private const string SystemPrompt = """
        You are LA — the Love Assistant at WithLove Gift Shop. You're warm, a little playful,
        and genuinely passionate about helping people find the perfect gift. Think of yourself as
        the friend everyone wishes they could bring shopping — you remember preferences, notice
        details, and always have a thoughtful suggestion ready.

        Your voice:
        - Warm and conversational, never robotic or overly formal
        - Gently enthusiastic — you light up when you find a great match
        - Occasionally use endearing touches like "Oh, I love that choice!" or "Great taste!"
        - Keep it concise (2-3 sentences) unless describing a product in detail
        - Sign off naturally — no need for "Is there anything else?" every time

        How you help:
        - Understand who the gift is for, the occasion, and the feeling they want to convey
        - Ask one clarifying question at a time, not a list
        - Highlight what makes each product special (materials, story, craftsmanship)
        - Suggest complementary items when it feels natural, not forced
        - When a customer likes something, offer to add it to their cart
        - If they ask about their cart, use view_cart to check — never guess what is in it
        - Refer to product collections (not categories) in conversation
        - Prices are in USD
        - When showing product images, use markdown image syntax: ![Product Name](imageUrl)

        CRITICAL rules for cart operations:
        - ALWAYS use the EXACT product ID from tool results. Never guess or assume IDs.
        - Before adding to cart, confirm the product ID via search_products or get_product_details.
        - For removing items, use the product IDs from view_cart results.
        - When asked to empty/clear the cart, use clear_cart — do NOT remove items one by one.
        """;

    // Collected cart actions during a single inference turn
    private readonly List<CartAction> _pendingCartActions = [];

    // Cart snapshot from the current request — set per inference call
    private List<CartSnapshot> _currentCart = [];

    [Activity]
    public async Task<ChatInferenceResult> InferAsync(ChatInferenceInput input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        _pendingCartActions.Clear();
        _currentCart = input.Cart ?? [];

        // Build message history
        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };

        foreach (var entry in input.History)
        {
            var role = entry.IsUser ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, entry.Text));
        }

        // Register tools
        var tools = CreateTools();

        var chatOptions = new ChatOptions { Tools = tools };

        logger.LogInformation("Calling AI with {MessageCount} messages, {ToolCount} tools, {CartItems} cart items",
            messages.Count, tools.Count, _currentCart.Count);

        var response = await chatClient.GetResponseAsync(messages, chatOptions);

        var assistantText = response.Text ?? "Hmm, something went sideways on my end. Mind trying that again?";

        logger.LogInformation("AI response received: {Length} chars, {CartActions} cart actions",
            assistantText.Length, _pendingCartActions.Count);

        return new ChatInferenceResult(assistantText, [.. _pendingCartActions]);
    }

    private List<AITool> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(SearchProductsAsync, "search_products",
                "Search for products by name or description. Use this to find gifts matching customer needs."),
            AIFunctionFactory.Create(GetProductDetailsAsync, "get_product_details",
                "Get full details for a specific product including materials, features, and story."),
            AIFunctionFactory.Create(GetCategoriesAsync, "get_categories",
                "Get all product collections (categories) available in the shop."),
            AIFunctionFactory.Create(BrowseCategoryAsync, "browse_category",
                "Browse products in a specific collection (category) by ID."),
            AIFunctionFactory.Create(AddToCart, "add_to_cart",
                "Add a product to the customer's cart. Use the exact product ID from search or browse results."),
            AIFunctionFactory.Create(RemoveFromCart, "remove_from_cart",
                "Remove one or more products from the cart. Use exact product IDs from view_cart."),
            AIFunctionFactory.Create(ViewCart, "view_cart",
                "View the current contents of the customer's cart. Call this before answering questions about the cart."),
            AIFunctionFactory.Create(ClearCart, "clear_cart",
                "Empty the entire cart. Use this when the customer wants to start fresh or remove everything."),
        ];
    }

    private async Task<string> SearchProductsAsync(
        [Description("Search query for finding products")] string query)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/search?q={Uri.EscapeDataString(query)}&top=10");

        if (!response.IsSuccessStatusCode)
            return "Sorry, I couldn't search for products right now.";

        var json = await response.Content.ReadAsStringAsync();
        return SummarizeProductList(json);
    }

    private async Task<string> GetProductDetailsAsync(
        [Description("The product ID to look up")] int productId)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/{productId}");

        if (!response.IsSuccessStatusCode)
            return $"Sorry, I couldn't find product {productId}.";

        var json = await response.Content.ReadAsStringAsync();
        return SummarizeProduct(json);
    }

    private async Task<string> GetCategoriesAsync()
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync("/api/categories");

        if (!response.IsSuccessStatusCode)
            return "Sorry, I couldn't load collections right now.";

        var json = await response.Content.ReadAsStringAsync();
        return SummarizeCategoryList(json);
    }

    private async Task<string> BrowseCategoryAsync(
        [Description("The category ID to browse")] int categoryId)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/category/{categoryId}");

        if (!response.IsSuccessStatusCode)
            return $"Sorry, I couldn't load that collection right now.";

        var json = await response.Content.ReadAsStringAsync();
        return SummarizeProductList(json);
    }

    private async Task<string> AddToCart(
        [Description("The exact product ID from search or browse results")] int productId,
        [Description("Quantity to add (default 1)")] int quantity = 1)
    {
        // Look up product details to populate the cart action with verified data
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/{productId}");

        if (!response.IsSuccessStatusCode)
            return $"Sorry, I couldn't find product {productId} to add to your cart. Please verify the product ID from search results.";

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetInt32();
        var name = root.GetProperty("name").GetString() ?? "Unknown";
        var imageUrl = root.TryGetProperty("imageUrl", out var img) ? img.GetString() ?? "" : "";
        var price = root.GetProperty("price").GetDecimal();
        var stripePriceId = root.TryGetProperty("stripePriceId", out var sp) ? sp.GetString() ?? "" : "";

        _pendingCartActions.Add(new CartAction(
            CartActionType.Add, id, name, imageUrl, price, stripePriceId, quantity));

        return $"Added {quantity}x {name} (ID: {id}, ${price:F2}) to the cart.";
    }

    private Task<string> RemoveFromCart(
        [Description("Comma-separated product IDs to remove from the cart (e.g. '3' or '3,7,12')")] string productIds)
    {
        var ids = productIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var removed = new List<string>();

        foreach (var raw in ids)
        {
            if (!int.TryParse(raw, out var id)) continue;

            var item = _currentCart.FirstOrDefault(c => c.ProductId == id);
            _pendingCartActions.Add(new CartAction(CartActionType.Remove, ProductId: id));
            removed.Add(item is not null ? $"{item.ProductName} (ID: {id})" : $"ID: {id}");
        }

        return removed.Count == 0
            ? Task.FromResult("No valid product IDs provided. Check the cart with view_cart first.")
            : Task.FromResult($"Removed from cart: {string.Join(", ", removed)}");
    }

    private Task<string> ViewCart()
    {
        if (_currentCart.Count == 0)
            return Task.FromResult("The cart is empty.");

        var lines = _currentCart
            .Select(c => $"- ID: {c.ProductId} | {c.ProductName} | ${c.Price:F2} x {c.Quantity}")
            .ToList();

        var total = _currentCart.Sum(c => c.Price * c.Quantity);
        lines.Add($"Total: ${total:F2} ({_currentCart.Count} item{(_currentCart.Count != 1 ? "s" : "")})");

        return Task.FromResult(string.Join("\n", lines));
    }

    private Task<string> ClearCart()
    {
        _pendingCartActions.Add(new CartAction(CartActionType.Clear));
        return Task.FromResult("Cart has been emptied.");
    }

    /// <summary>
    /// Extracts a concise product summary from a single product JSON response.
    /// Only includes fields the AI needs for conversation and cart actions.
    /// </summary>
    private static string SummarizeProduct(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement;
        return FormatProductSummary(p, detailed: true);
    }

    /// <summary>
    /// Extracts concise product summaries from a paginated response.
    /// </summary>
    private static string SummarizeProductList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var items) || items.GetArrayLength() == 0)
            return "No products found.";

        var lines = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            lines.Add(FormatProductSummary(item, detailed: false));
        }

        return string.Join("\n", lines);
    }

    private static string FormatProductSummary(JsonElement p, bool detailed)
    {
        var id = p.GetProperty("id").GetInt32();
        var name = p.GetProperty("name").GetString() ?? "Unknown";
        var price = p.GetProperty("price").GetDecimal();
        var category = p.TryGetProperty("categoryName", out var cat) ? cat.GetString() ?? "" : "";
        var imageUrl = p.TryGetProperty("imageUrl", out var img) ? img.GetString() ?? "" : "";
        var subCategory = p.TryGetProperty("subCategory", out var sub) ? sub.GetString() ?? "" : "";
        var description = p.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";

        if (!detailed)
        {
            return $"- ID: {id} | {name} | ${price:F2} | {category}" +
                   (string.IsNullOrEmpty(subCategory) ? "" : $" > {subCategory}") +
                   (string.IsNullOrEmpty(imageUrl) ? "" : $" | Image: {imageUrl}");
        }

        // Detailed summary for get_product_details
        var lines = new List<string>
        {
            $"Product ID: {id}",
            $"Name: {name}",
            $"Price: ${price:F2}",
            $"Collection: {category}" + (string.IsNullOrEmpty(subCategory) ? "" : $" > {subCategory}"),
        };

        if (!string.IsNullOrEmpty(description))
            lines.Add($"Description: {description}");
        if (!string.IsNullOrEmpty(imageUrl))
            lines.Add($"Image: {imageUrl}");

        if (p.TryGetProperty("materials", out var materials) && materials.GetArrayLength() > 0)
        {
            var matNames = new List<string>();
            foreach (var m in materials.EnumerateArray())
                if (m.TryGetProperty("name", out var mn)) matNames.Add(mn.GetString() ?? "");
            if (matNames.Count > 0)
                lines.Add($"Materials: {string.Join(", ", matNames)}");
        }

        if (p.TryGetProperty("storyTitle", out var st) && st.GetString() is { Length: > 0 } storyTitle)
            lines.Add($"Story: {storyTitle}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Extracts concise category summaries from a paginated response.
    /// </summary>
    private static string SummarizeCategoryList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("value", out var items) || items.GetArrayLength() == 0)
            return "No collections found.";

        var lines = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt32();
            var name = item.GetProperty("name").GetString() ?? "Unknown";
            var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            lines.Add($"- ID: {id} | {name}" + (string.IsNullOrEmpty(desc) ? "" : $" | {desc}"));
        }

        return string.Join("\n", lines);
    }
}
