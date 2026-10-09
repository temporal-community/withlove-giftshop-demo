using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using WithLove.Web.Components.Shared;

namespace WithLove.Web.Tests.Unit.Components;

public class ProductCardGridRenderingTests
{
    [Fact]
    public async Task Render_WithMissingImage_ShowsPlaceholderAndKeepsCanonicalProductLink()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());
        var product = new Product
        {
            Id = 9,
            Name = "Silver Dollar",
            Description = "A timeless keepsake.",
            Price = 35m,
            ImageUrl = null,
        };

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ProductCardGrid>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(ProductCardGrid.Product)] = product,
                    [nameof(ProductCardGrid.Compact)] = true,
                }));
            return component.ToHtmlString();
        });

        html.Should().Contain("href=\"/product/9\"");
        html.Should().Contain("Image unavailable for Silver Dollar");
        html.Should().NotContain("<img");
    }
}
