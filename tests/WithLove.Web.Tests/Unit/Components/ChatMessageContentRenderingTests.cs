using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using WithLove.Web.Components.Shared;

namespace WithLove.Web.Tests.Unit.Components;

public class ChatMessageContentRenderingTests
{
    [Fact]
    public async Task Render_LabeledProduct_UsesCanonicalProductCardAndDiscardsModelImage()
    {
        const string malformedModelImage = "https://images.test/uGuG9nK79Bgnoa";
        const string canonicalImage = "https://catalog.example/uGuG9ntK79Bgno";
        var productService = A.Fake<IProductService>();
        A.CallTo(() => productService.GetProductsAsync()).Returns(
        [
            new Product
            {
                Id = 12,
                Name = "Silver Dollar",
                Description = "The catalog-owned description.",
                Price = 35m,
                ImageUrl = canonicalImage,
            },
        ]);
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(productService)
            .BuildServiceProvider();
        var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());
        var response = $"""
            Product: Silver Dollar
            Price: $35.00
            Why it’s special: A model-authored description.
            ![Silver Dollar]({malformedModelImage})
            """;

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ChatMessageContent>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(ChatMessageContent.Text)] = response,
                }));
            return component.ToHtmlString();
        });

        html.Should().Contain("href=\"/product/12\"");
        html.Should().Contain(canonicalImage);
        html.Should().Contain("The catalog-owned description.");
        html.Should().NotContain(malformedModelImage);
        A.CallTo(() => productService.GetProductsAsync()).MustHaveHappenedOnceExactly();
    }
}
