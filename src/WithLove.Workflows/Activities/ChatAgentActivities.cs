using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Loyalty;
using WithLove.Workflows.Telemetry;
using WithLove.Workflows.Workflows;

namespace WithLove.Workflows.Activities;

public partial class ChatAgentActivities(IChatClient chatClient, IHttpClientFactory httpClientFactory)
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
        - Product IDs are internal references for tool calls only — never mention them in responses to the customer

        CRITICAL rules for cart operations:
        - ALWAYS use the EXACT product ID from tool results. Never guess or assume IDs.
        - Before adding to cart, confirm the product ID via search_products or get_product_details.
        - For removing items, use the product IDs from view_cart results.
        - When asked to empty/clear the cart, use clear_cart — do NOT remove items one by one.
        - AFTER EVERY cart mutation (add_to_cart, remove_from_cart, clear_cart), you MUST immediately
          call view_cart to verify the result. Compare what you intended with what view_cart shows.
        - If view_cart reveals an unexpected state (wrong item, wrong quantity, extra items),
          fix it immediately using remove_from_cart or add_to_cart before responding.
        - When confirming a cart change, always state the specific product name, quantity, and price.
          Never give vague confirmations like "added to your cart."
        - If the customer asks for a specific quantity, verify after adding that view_cart shows
          the correct total quantity (existing + newly added).

        Navigation tools:
        - Use navigate_to_product when a customer wants to see a product page.
        - Use navigate_to_collection when a customer wants to browse a collection.
          IMPORTANT: navigate_to_collection requires a numeric category ID.
          If the customer names a collection (e.g. "Comfort", "Romantic"), you MUST call
          get_categories first, find the matching category ID, then call navigate_to_collection
          with that ID. Never guess or invent an ID.
        - Use navigate_to_cart when a customer wants to review their full cart.
        - Use navigate_to_checkout when a customer is ready to purchase.
        - Only navigate when the customer's intent clearly suggests it. Do not navigate proactively.

        Love Tokens (loyalty points):
        - If a customer asks about their Love Tokens balance, use view_loyalty_points to check.
          You can view their balance but CANNOT redeem tokens on their behalf — redemption happens at checkout.
          Tiers: Bronze (0–499 lifetime pts), Silver (500–1,999), Gold (2,000+). 1 token per $1 spent. 100 tokens = $1 off.
        """;

    // Collected cart actions during a single inference turn
    private readonly List<CartAction> _pendingCartActions = [];

    // Navigation actions collected during a single inference turn
    private readonly List<NavigationAction> _pendingNavigationActions = [];

    // Cart snapshot from the current request — set per inference call
    private List<CartSnapshot> _currentCart = [];

    // Mutable working copy of the cart — updated by cart tools so view_cart reflects mutations
    private WorkingCart _workingCart = new([]);

    // Authenticated userId for the current inference turn
    private string? _currentUserId;

    [Activity]
    public async Task<ChatInferenceResult> InferAsync(ChatInferenceInput input)
    {
        var logger = ActivityExecutionContext.Current.Logger;
        _pendingCartActions.Clear();
        _pendingNavigationActions.Clear();
        _currentCart = input.Cart ?? [];
        _workingCart = new WorkingCart(_currentCart);
        _currentUserId = null;
        _currentUserId = input.User?.UserId;

        // Build message history
        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };

        if (input.User is { } user)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(user.Name))  parts.Add($"Name: {user.Name}");
            if (!string.IsNullOrEmpty(user.Email)) parts.Add($"Email: {user.Email}");
            if (parts.Count > 0)
                messages.Add(new ChatMessage(ChatRole.System,
                    $"Customer context — {string.Join(", ", parts)}. " +
                    "Address them by first name when it feels natural."));
        }

        foreach (var entry in input.History)
        {
            var role = entry.IsUser ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, entry.Text));
        }

        // Register tools
        var tools = CreateTools();

        var chatOptions = new ChatOptions { Tools = tools };

        // Enrich Temporal's existing activity span rather than creating a new one
        var activity = Activity.Current;
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("ai.message_count", messages.Count);
            activity.SetTag("ai.tool_count", tools.Count);
            activity.SetTag("ai.cart_items", _currentCart.Count);
        }

        LogInferenceStarted(logger, messages.Count, tools.Count, _currentCart.Count);

        var sw = Stopwatch.StartNew();
        var response = await chatClient.GetResponseAsync(messages, chatOptions);
        sw.Stop();

        var assistantText = response.Text ?? "Hmm, something went sideways on my end. Mind trying that again?";

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("ai.response_length", assistantText.Length);
            activity.SetTag("ai.cart_actions", _pendingCartActions.Count);
            activity.SetTag("ai.nav_actions", _pendingNavigationActions.Count);
        }

        LogInferenceCompleted(logger, assistantText.Length, _pendingCartActions.Count);

        ChatTelemetry.InferenceDuration.Record(sw.Elapsed.TotalMilliseconds);

        foreach (var action in _pendingCartActions)
            ChatTelemetry.CartMutations.Add(1, new TagList { { "type", action.Type.ToString().ToLower() } });

        return new ChatInferenceResult(assistantText, [.. _pendingCartActions], [.. _pendingNavigationActions]);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Calling AI with {MessageCount} messages, {ToolCount} tools, {CartItems} cart items")]
    private static partial void LogInferenceStarted(ILogger logger, int messageCount, int toolCount, int cartItems);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AI response received: {Length} chars, {CartActions} cart actions")]
    private static partial void LogInferenceCompleted(ILogger logger, int length, int cartActions);

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
            AIFunctionFactory.Create(NavigateToProduct, "navigate_to_product",
                "Navigate the customer to a product detail page."),
            AIFunctionFactory.Create(NavigateToCollection, "navigate_to_collection",
                "Navigate the customer to a product collection (category) page. " +
                "ALWAYS call get_categories first to find the numeric category ID — never guess it."),
            AIFunctionFactory.Create(NavigateToCart, "navigate_to_cart",
                "Navigate the customer to their cart page."),
            AIFunctionFactory.Create(NavigateToCheckout, "navigate_to_checkout",
                "Navigate the customer to the checkout page."),
            AIFunctionFactory.Create(ViewLoyaltyPointsAsync, "view_loyalty_points",
                "Get the current customer's Love Tokens balance, tier, and progress toward the next tier. Use when the customer asks about their points, balance, rewards, or tier status."),
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

        // Update working cart so view_cart reflects this mutation
        _workingCart.Add(id, name, price, quantity);

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

            var item = _workingCart.FindById(id);
            _pendingCartActions.Add(new CartAction(CartActionType.Remove, ProductId: id));
            removed.Add(item is not null ? $"{item.ProductName} (ID: {id})" : $"ID: {id}");
            _workingCart.Remove(id);
        }

        return removed.Count == 0
            ? Task.FromResult("No valid product IDs provided. Check the cart with view_cart first.")
            : Task.FromResult($"Removed from cart: {string.Join(", ", removed)}");
    }

    private Task<string> ViewCart() => Task.FromResult(_workingCart.Summarize());

    private Task<string> ClearCart()
    {
        _pendingCartActions.Add(new CartAction(CartActionType.Clear));
        _workingCart.Clear();
        return Task.FromResult("Cart has been emptied.");
    }

    private Task<string> NavigateToProduct(
        [Description("The product ID to navigate to")] int productId)
    {
        _pendingNavigationActions.Add(new NavigationAction(NavigationTarget.Product, $"/product/{productId}"));
        return Task.FromResult($"Navigating to product {productId} page.");
    }

    private Task<string> NavigateToCollection(
        [Description("Numeric category ID from get_categories results. Use 0 only to show all collections.")] int categoryId = 0)
    {
        var url = categoryId > 0 ? $"/collections/{categoryId}" : "/collections";
        _pendingNavigationActions.Add(new NavigationAction(NavigationTarget.Collection, url));
        return Task.FromResult(categoryId > 0 ? $"Navigating to collection {categoryId}." : "Navigating to all collections.");
    }

    private Task<string> NavigateToCart()
    {
        _pendingNavigationActions.Add(new NavigationAction(NavigationTarget.Cart, "/cart"));
        return Task.FromResult("Navigating to your cart.");
    }

    private Task<string> NavigateToCheckout()
    {
        _pendingNavigationActions.Add(new NavigationAction(NavigationTarget.Checkout, "/checkout"));
        return Task.FromResult("Navigating to checkout.");
    }

    private async Task<string> ViewLoyaltyPointsAsync()
    {
        if (string.IsNullOrEmpty(_currentUserId))
            return "Love Tokens are available to logged-in customers. Sign in to see your balance and start earning!";

        var logger = ActivityExecutionContext.Current.Logger;

        try
        {
            var client = ActivityExecutionContext.Current.TemporalClient;
            var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{_currentUserId}");
            var profile = await handle.QueryAsync(wf => wf.GetLoyaltyProfile());

            var nextTierMsg = profile.Tier == LoyaltyTier.Gold
                ? "You've reached the highest tier — Gold!"
                : $"Earn {profile.PointsToNextTier} more to reach {NextTierName(profile.Tier)}.";

            return $"You have {profile.Balance} Love Tokens ({profile.Tier} tier). {nextTierMsg} " +
                   $"(Lifetime earned: {profile.LifetimeEarned} pts. Redeem at checkout: 100 pts = $1 off.)";
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
        {
            return "You don't have any Love Tokens yet. Complete a purchase to start earning — 1 token per $1 spent!";
        }
        catch (Exception ex)
        {
            logger.UnableToLoadLoveTokens(ex, _currentUserId);
            return "I couldn't load your Love Tokens balance right now. Please try again in a moment.";
        }
    }

    private static string NextTierName(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Bronze => "Silver",
        LoyaltyTier.Silver => "Gold",
        _ => "the next tier"
    };

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
