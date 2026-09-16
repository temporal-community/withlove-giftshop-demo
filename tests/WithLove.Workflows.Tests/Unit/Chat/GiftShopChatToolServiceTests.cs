using System.Net;
using System.Text;
using WithLove.Workflows.Activities;

namespace WithLove.Workflows.Tests.Unit.Chat;

public class GiftShopChatToolServiceTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SearchProducts_ReturnsOnlyTopFourMatches()
    {
        var handler = new CapturingHandler();
        var service = CreateService(handler);

        var result = await service.SearchProductsAsync("keepsake", CancellationToken.None);

        handler.PathAndQuery.Should().Be("/api/products/search?q=keepsake&top=4");
        result.Split('\n').Should().HaveCount(GiftShopChatToolService.MaxSearchProductMatches);
        result.Should().Contain("Product 4");
        result.Should().Contain("Description: A thoughtful gift 4");
        result.Should().NotContain("Product 5");
    }

    /// <summary>
    /// Every product-shaped tool result is free of the catalogue's image URL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ProductsAPI returns <c>imageUrl</c> on every product and the tool summaries deliberately drop
    /// it: the chat panel renders product cards from the cart/navigation state, not from prose, so a
    /// URL in the tool result buys nothing and costs tokens on every model step that reads it — and
    /// invites the model to paste a raw path, or an invented variant of one, into the reply.
    /// </para>
    /// <para>
    /// Nothing downstream reads the field, which is exactly why its return would be silent: no
    /// renderer breaks, no assertion elsewhere fires, the summaries simply get longer and the model
    /// starts quoting paths. These tests are the only thing standing between the field and the
    /// prompt. Each asserts the fixture JSON <i>did</i> carry an image URL before asserting the
    /// summary does not — a fixture that quietly lost the field would otherwise make the negative
    /// assertions pass by describing nothing.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task SearchProducts_OmitsCatalogImageUrlFromToolResult()
    {
        ImageBearingProductsHandler.ProductJson.Should().Contain("\"imageUrl\"");
        var service = CreateService(new ImageBearingProductsHandler());

        var result = await service.SearchProductsAsync("keepsake", CancellationToken.None);

        result.Should().Contain("Keepsake Box");
        AssertNoImageUrl(result);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task BrowseCategory_OmitsCatalogImageUrlFromToolResult()
    {
        ImageBearingProductsHandler.ProductJson.Should().Contain("\"imageUrl\"");
        var service = CreateService(new ImageBearingProductsHandler());

        var result = await service.BrowseCategoryAsync(3, CancellationToken.None);

        result.Should().Contain("Keepsake Box");
        AssertNoImageUrl(result);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Chat)]
    public async Task GetProductDetails_OmitsCatalogImageUrlFromToolResult()
    {
        ImageBearingProductsHandler.ProductJson.Should().Contain("\"imageUrl\"");
        var service = CreateService(new ImageBearingProductsHandler());

        var result = await service.GetProductDetailsAsync(7, CancellationToken.None);

        // The detailed summary is the widest of the three — it carries materials and the story
        // title — so it is the most likely place for an image field to be reintroduced.
        result.Should().Contain("Name: Keepsake Box");
        result.Should().Contain("Materials: Wood");
        AssertNoImageUrl(result);
    }

    private static void AssertNoImageUrl(string result)
    {
        result.Should().NotContain("imageUrl");
        result.Should().NotContain("Image");
        result.Should().NotContain("image");
        result.Should().NotContain("/images/");
        result.Should().NotContain(".jpg");
    }

    private static GiftShopChatToolService CreateService(HttpMessageHandler handler)
    {
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpClientFactory.CreateClient("productsApi"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://products.test") });
        return new GiftShopChatToolService(httpClientFactory);
    }

    /// <summary>A ProductsAPI transport whose every product carries an <c>imageUrl</c>.</summary>
    private sealed class ImageBearingProductsHandler : HttpMessageHandler
    {
        internal const string ProductJson =
            """
            {"id":7,"name":"Keepsake Box","price":25.00,"categoryName":"Comfort","subCategory":"Keepsakes","description":"A handcrafted box.","imageUrl":"/images/keepsake-box.jpg","stripePriceId":"price_7","materials":[{"name":"Wood"}],"storyTitle":"Made with care"}
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            var isList = path.Contains("/search", StringComparison.Ordinal)
                         || path.Contains("/category/", StringComparison.Ordinal);
            var json = isList ? $$"""{"value":[{{ProductJson}}]}""" : ProductJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery;
            var products = string.Join(
                ",",
                Enumerable.Range(1, 5).Select(id =>
                    $$"""{"id":{{id}},"name":"Product {{id}}","price":{{id}}.00,"description":"A thoughtful gift {{id}}"}"""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"value":[{{products}}]}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
