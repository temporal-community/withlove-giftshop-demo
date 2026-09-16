using WithLove.Web.Components.Shared;

namespace WithLove.Web.Tests.Unit.Components;

public class ChatMessageContentParserTests
{
    [Fact]
    public void Parse_CartConfirmationWithProductAndPrice_PreservesTheConfirmationAsText()
    {
        const string response =
            "Velvet Crimson — $89.00 has been added to your cart. Would you like to add another item?";

        var segment = ChatMessageContentParser.Parse(response).Should().ContainSingle().Which;

        segment.Type.Should().Be(ChatMessageContentParser.SegmentType.Text);
        segment.Content.Should().Be(response);
    }

    [Fact]
    public void Parse_ProductBlock_ProjectsOnlyDisplayFields()
    {
        const string response = """
            Here are two lovely choices:
            - Velvet Crimson (ID 9) — $89
            Large, lush bouquet with dramatic red tones and premium blooms. Image:
            ![Velvet Crimson](https://lh3.googleusercontent.com/aida-public/abc123)
            """;

        var segments = ChatMessageContentParser.Parse(response);

        var productSegment = segments.Should().ContainSingle(
            segment => segment.Type == ChatMessageContentParser.SegmentType.Product).Which;
        productSegment.Product.Should().NotBeNull();
        var product = productSegment.Product!;
        product!.Name.Should().Be("Velvet Crimson");
        product.Price.Should().Be(89m);
        product.Description.Should().Be(
            "Large, lush bouquet with dramatic red tones and premium blooms.");
        product.Name.Should().NotContain("ID");
        product.Description.Should().NotContain("Image:");
        segments.Select(segment => segment.Content).Should().NotContain(value =>
            value.Contains("ID", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Image:", StringComparison.OrdinalIgnoreCase)
            || value.Contains("![", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MultipleProductBlocks_CreatesOneCardPerProduct()
    {
        const string response = """
            - Velvet Crimson (ID 9) — $89
            Rich red blooms. Image: ![Velvet Crimson](https://images.test/velvet)
            - Blush Peony (ID 10) — $78
            Soft pink peonies. Image: ![Blush Peony](https://images.test/peony)
            """;

        var products = ChatMessageContentParser.Parse(response)
            .Where(segment => segment.Type == ChatMessageContentParser.SegmentType.Product)
            .Select(segment => segment.Product)
            .ToArray();

        products.Should().HaveCount(2);
        products.Select(product => product!.Name).Should().Equal("Velvet Crimson", "Blush Peony");
        products.Select(product => product!.Price).Should().Equal(89m, 78m);
        products.Should().OnlyContain(product => product!.Description.Length <= 180);
    }

    [Fact]
    public void Parse_ProductWithoutBullet_StillRemovesInternalId()
    {
        const string response = "Velvet Crimson (ID 9) — $89\nA signature red bouquet.";

        var product = ChatMessageContentParser.Parse(response)
            .Single(segment => segment.Type == ChatMessageContentParser.SegmentType.Product)
            .Product;

        product!.Name.Should().Be("Velvet Crimson");
        product.Description.Should().Be("A signature red bouquet.");
    }

    [Fact]
    public void Parse_LabeledProductResponse_RecognizesProductAndDropsModelImageUrl()
    {
        const string response = """
            Product: Silver Dollar
            Price: $35.00
            Why it’s special: A timeless keepsake for marking an important occasion.
            ![Silver Dollar](https://images.test/uGuG9nK79Bgnoa)
            """;

        var segments = ChatMessageContentParser.Parse(response);

        var product = segments.Should().ContainSingle(
            segment => segment.Type == ChatMessageContentParser.SegmentType.Product).Which.Product;
        product.Should().NotBeNull();
        product!.Name.Should().Be("Silver Dollar");
        product.Price.Should().Be(35m);
        product.Description.Should().Be("A timeless keepsake for marking an important occasion.");
        segments.Select(segment => segment.Content)
            .Should().NotContain(content => content.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnmatchedMarkdownImage_DropsUrlAndKeepsSurroundingText()
    {
        const string response =
            "Take a look:\n![Bouquet](https://lh3.googleusercontent.com/aida-public/opaque-token)";

        var segment = ChatMessageContentParser.Parse(response).Should().ContainSingle().Which;

        segment.Type.Should().Be(ChatMessageContentParser.SegmentType.Text);
        segment.Content.Should().Be("Take a look:");
    }

    [Fact]
    public void FindCatalogProduct_MatchesProductNameEmbeddedInModelLeadIn()
    {
        var parsed = new ChatMessageContentParser.ProductCard(
            "Oh, what a romantic pick! Velvet Crimson",
            89m,
            string.Empty);
        var catalog = new[]
        {
            new Product
            {
                Id = 9,
                Name = "Velvet Crimson",
                Price = 89m,
                ImageUrl = "https://catalog.example/velvet.jpg",
            },
            new Product { Name = "Crimson", Price = 50m },
        };

        var match = ChatMessageContentParser.FindCatalogProduct(parsed, catalog);

        match.Should().NotBeNull();
        match!.Id.Should().Be(9);
        match!.Name.Should().Be("Velvet Crimson");
        match.ImageUrl.Should().Be("https://catalog.example/velvet.jpg");
    }
}
