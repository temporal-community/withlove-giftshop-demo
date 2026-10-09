using System.Net;
using System.Text.Json;
using Temporalio.Activities;
using Temporalio.Exceptions;
using WithLove.Workflows.Chat;
using WithLove.Workflows.Loyalty;
using WithLove.Workflows.Workflows;

namespace WithLove.Workflows.Activities;

internal sealed record AddToCartToolResult(string Message, CartAction? Action);

internal sealed class GiftShopChatToolService(IHttpClientFactory httpClientFactory)
{
    internal const int MaxSearchProductMatches = 4;

    public async Task<string> SearchProductsAsync(string query, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync(
            $"/api/products/search?q={Uri.EscapeDataString(query)}&top={MaxSearchProductMatches}",
            cancellationToken);

        if (IsResourceMissing(response, "search_products"))
            return "No products found.";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return SummarizeProductList(json, MaxSearchProductMatches);
    }

    public async Task<string> GetProductDetailsAsync(int productId, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/{productId}", cancellationToken);

        if (IsResourceMissing(response, "get_product_details"))
            return $"There is no product with ID {productId}. Verify the ID from search results.";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return SummarizeProduct(json);
    }

    public async Task<string> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync("/api/categories", cancellationToken);

        if (IsResourceMissing(response, "get_categories"))
            return "No collections found.";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return SummarizeCategoryList(json);
    }

    public async Task<string> BrowseCategoryAsync(int categoryId, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/category/{categoryId}", cancellationToken);

        if (IsResourceMissing(response, "browse_category"))
            return $"There is no collection with ID {categoryId}. Call get_categories for valid IDs.";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return SummarizeProductList(json);
    }

    /// <summary>
    /// Resolves a model-supplied category ID to its collection name, or reports that no such
    /// collection exists.
    /// </summary>
    /// <param name="categoryId">The category ID the model asked to navigate to.</param>
    /// <param name="cancellationToken">Cancels the ProductsAPI lookup.</param>
    /// <returns>
    /// The collection name when the category exists (possibly empty if it is unnamed), or
    /// <see langword="null"/> when ProductsAPI reports it does not exist.
    /// </returns>
    /// <remarks>
    /// Every other tool that takes an ID already fetches the resource, so a hallucinated ID
    /// degrades to a "no such thing" string. <c>navigate_to_collection</c> was the exception: it
    /// only ever built a URL, so an invented ID became a real navigation to a dead route. This is
    /// the backstop that closes that gap, which is why the lookup exists purely to be checked —
    /// the name it returns is a bonus that lets the confirmation message name the collection.
    /// </remarks>
    public async Task<string?> FindCategoryNameAsync(
        int categoryId,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/categories/{categoryId}", cancellationToken);

        if (IsResourceMissing(response, "navigate_to_collection"))
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("name", out var name)
            ? name.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Resolves a model-supplied product ID to its product name, or reports that no such product
    /// exists.
    /// </summary>
    /// <param name="productId">The product ID the model asked to navigate to.</param>
    /// <param name="cancellationToken">Cancels the ProductsAPI lookup.</param>
    /// <returns>
    /// The product name when the product exists (possibly empty if it is unnamed), or
    /// <see langword="null"/> when ProductsAPI reports it does not exist.
    /// </returns>
    /// <remarks>
    /// The product-side twin of <see cref="FindCategoryNameAsync"/>, and it closes the same gap.
    /// <c>get_product_details</c> and <c>add_to_cart</c> fetch the product, so a hallucinated ID
    /// degrades to a "no such product" string; <c>navigate_to_product</c> only ever interpolated
    /// the ID into <c>/product/{id}</c>, which cannot fail — it just lands the customer on a dead
    /// route. Unlike collections there is no degenerate ID here: <c>/product</c> is not a route, so
    /// every ID goes through this lookup rather than being special-cased.
    /// </remarks>
    public async Task<string?> FindProductNameAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/{productId}", cancellationToken);

        if (IsResourceMissing(response, "navigate_to_product"))
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("name", out var name)
            ? name.GetString() ?? string.Empty
            : string.Empty;
    }

    public async Task<AddToCartToolResult> BuildAddToCartAsync(
        int productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("productsApi");
        var response = await http.GetAsync($"/api/products/{productId}", cancellationToken);

        if (IsResourceMissing(response, "add_to_cart"))
        {
            return new AddToCartToolResult(
                $"There is no product with ID {productId}, so nothing was added to the cart. " +
                "Verify the product ID from search results.",
                null);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var product = document.RootElement;

        var id = product.GetProperty("id").GetInt32();
        var name = product.GetProperty("name").GetString() ?? "Unknown";
        var imageUrl = product.TryGetProperty("imageUrl", out var image)
            ? image.GetString() ?? string.Empty
            : string.Empty;
        var price = product.GetProperty("price").GetDecimal();
        var stripePriceId = product.TryGetProperty("stripePriceId", out var stripePrice)
            ? stripePrice.GetString() ?? string.Empty
            : string.Empty;

        var action = new CartAction(
            CartActionType.Add,
            id,
            name,
            imageUrl,
            price,
            stripePriceId,
            quantity);
        return new AddToCartToolResult(
            $"Added {quantity}x {name} (ID: {id}, ${price:F2}) to the cart.",
            action);
    }

    public async Task<string> ViewLoyaltyPointsAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return "Love Tokens are available to logged-in customers. " +
                   "Sign in to see your balance and start earning!";
        }

        var logger = ActivityExecutionContext.Current.Logger;

        try
        {
            var client = ActivityExecutionContext.Current.TemporalClient;
            var handle = client.GetWorkflowHandle<LoyaltyAccountWorkflow>($"loyalty-{userId}");
            var profile = await handle.QueryAsync(workflow => workflow.GetLoyaltyProfile());

            var nextTierMessage = profile.Tier == LoyaltyTier.Gold
                ? "You've reached the highest tier — Gold!"
                : $"Earn {profile.PointsToNextTier} more to reach {NextTierName(profile.Tier)}.";

            return $"You have {profile.Balance} Love Tokens ({profile.Tier} tier). " +
                   $"{nextTierMessage} (Lifetime earned: {profile.LifetimeEarned} pts. " +
                   "Redeem at checkout: 100 pts = $1 off.)";
        }
        catch (RpcException exception) when (exception.Code == RpcException.StatusCode.NotFound)
        {
            // A missing loyalty workflow is an answer, not a fault: this customer has simply never
            // earned tokens. Retrying would return NotFound forever, so report it to the model.
            return "You don't have any Love Tokens yet. Complete a purchase to start earning — " +
                   "1 token per $1 spent!";
        }
        catch (Exception exception)
        {
            // Everything else is infrastructure. Log for operators, then rethrow so the activity
            // fails and Temporal's retry policy actually runs. Swallowing this and returning
            // "please try again in a moment" completed the activity successfully — Temporal saw a
            // healthy workflow, no retry ever fired, and the model relayed advice to the customer
            // that could never work.
            logger.UnableToLoadLoveTokens(exception, userId);
            throw;
        }
    }

    /// <summary>
    /// Separates "this resource does not exist" from "this call failed", and refuses to let the
    /// second masquerade as the first.
    /// </summary>
    /// <param name="response">The ProductsAPI response to classify.</param>
    /// <param name="toolName">Tool name, used to make the failure legible in Temporal history.</param>
    /// <returns>
    /// <see langword="false"/> when the body can be read; <see langword="true"/> when the resource
    /// genuinely does not exist and the caller should return a graceful message to the model.
    /// </returns>
    /// <exception cref="ApplicationFailureException">
    /// Thrown for every other non-success status — see the remarks for why.
    /// </exception>
    /// <remarks>
    /// A durable tool that catches an infrastructure failure and returns apologetic prose
    /// <i>completes successfully</i>. Temporal records a healthy activity, the retry policy never
    /// fires, and the model is handed an outcome it cannot distinguish from a real answer. So only
    /// outcomes the model can genuinely act on are allowed to become strings:
    /// <list type="bullet">
    /// <item><description>
    /// <b>404</b> — a real answer. The product or collection does not exist and never will; a retry
    /// returns the same 404. Handled conversationally by the caller.
    /// </description></item>
    /// <item><description>
    /// <b>5xx, 408, 429</b> — transient infrastructure failure. Thrown as retryable so Temporal
    /// re-runs the activity, which is the whole reason each tool call is its own activity.
    /// </description></item>
    /// <item><description>
    /// <b>Other 4xx</b> — the request itself is wrong (bad parameters, missing API version header).
    /// Thrown as non-retryable: three identical attempts would fail three identical ways, so fail
    /// fast rather than burning the retry budget.
    /// </description></item>
    /// </list>
    /// Network-level faults already throw out of <c>HttpClient</c> and are deliberately left to
    /// propagate for exactly the same reason.
    /// </remarks>
    private static bool IsResourceMissing(HttpResponseMessage response, string toolName)
    {
        if (response.IsSuccessStatusCode)
            return false;

        if (response.StatusCode == HttpStatusCode.NotFound)
            return true;

        var transient =
            (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            || (int)response.StatusCode >= 500;

        throw new ApplicationFailureException(
            $"Durable tool '{toolName}' could not reach ProductsAPI: " +
            $"HTTP {(int)response.StatusCode} {response.StatusCode}.",
            errorType: "ProductsApiRequestFailed",
            nonRetryable: !transient);
    }

    private static string NextTierName(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Bronze => "Silver",
        LoyaltyTier.Silver => "Gold",
        _ => "the next tier",
    };

    internal static string SummarizeProduct(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FormatProductSummary(document.RootElement, detailed: true);
    }

    internal static string SummarizeProductList(string json, int? maxItems = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("value", out var items) || items.GetArrayLength() == 0)
            return "No products found.";

        return string.Join(
            "\n",
            items.EnumerateArray()
                .Take(maxItems ?? int.MaxValue)
                .Select(item => FormatProductSummary(item, detailed: false)));
    }

    private static string FormatProductSummary(JsonElement product, bool detailed)
    {
        var id = product.GetProperty("id").GetInt32();
        var name = product.GetProperty("name").GetString() ?? "Unknown";
        var price = product.GetProperty("price").GetDecimal();
        var category = product.TryGetProperty("categoryName", out var categoryElement)
            ? categoryElement.GetString() ?? string.Empty
            : string.Empty;
        var subCategory = product.TryGetProperty("subCategory", out var subCategoryElement)
            ? subCategoryElement.GetString() ?? string.Empty
            : string.Empty;
        var description = product.TryGetProperty("description", out var descriptionElement)
            ? descriptionElement.GetString() ?? string.Empty
            : string.Empty;

        if (!detailed)
        {
            var shortDescription = LimitDescription(description);
            return $"- ID: {id} | {name} | ${price:F2} | {category}" +
                   (string.IsNullOrEmpty(subCategory) ? string.Empty : $" > {subCategory}") +
                   (string.IsNullOrEmpty(shortDescription) ? string.Empty : $" | Description: {shortDescription}");
        }

        var lines = new List<string>
        {
            $"Product ID: {id}",
            $"Name: {name}",
            $"Price: ${price:F2}",
            $"Collection: {category}" +
            (string.IsNullOrEmpty(subCategory) ? string.Empty : $" > {subCategory}"),
        };

        if (!string.IsNullOrEmpty(description))
            lines.Add($"Description: {description}");

        if (product.TryGetProperty("materials", out var materials) && materials.GetArrayLength() > 0)
        {
            var names = materials.EnumerateArray()
                .Select(material => material.TryGetProperty("name", out var value)
                    ? value.GetString()
                    : null)
                .Where(value => !string.IsNullOrEmpty(value));
            var joinedNames = string.Join(", ", names!);
            if (!string.IsNullOrEmpty(joinedNames))
                lines.Add($"Materials: {joinedNames}");
        }

        if (product.TryGetProperty("storyTitle", out var story)
            && story.GetString() is { Length: > 0 } storyTitle)
        {
            lines.Add($"Story: {storyTitle}");
        }

        return string.Join("\n", lines);
    }

    private static string LimitDescription(string description)
    {
        const int maxLength = 180;
        if (description.Length <= maxLength)
            return description;

        var cutoff = description[..maxLength].LastIndexOf(' ');
        return $"{description[..(cutoff > 0 ? cutoff : maxLength)].TrimEnd()}…";
    }

    internal static string SummarizeCategoryList(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("value", out var items) || items.GetArrayLength() == 0)
            return "No collections found.";

        return string.Join("\n", items.EnumerateArray().Select(item =>
        {
            var id = item.GetProperty("id").GetInt32();
            var name = item.GetProperty("name").GetString() ?? "Unknown";
            var description = item.TryGetProperty("description", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
            return $"- ID: {id} | {name}" +
                   (string.IsNullOrEmpty(description) ? string.Empty : $" | {description}");
        }));
    }
}
