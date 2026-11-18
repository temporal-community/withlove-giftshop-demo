using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using WithLove.Web.Components.Shared;

namespace WithLove.Web.Tests.Unit.Components;

[Trait(TestTraits.Category, TestTraits.Unit)]
public class PriceFormatterTests
{
    [Fact]
    public async Task ProductCardGrid_RendersDollarPriceUnderInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ProductCardGrid.Product)] = new Product { Name = "Gift", Price = 165m },
            });

            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<ProductCardGrid>(parameters)).ToHtmlString());

            html.Should().Contain("$165.00").And.NotContain("¤");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("", "165", "$165.00")]
    [InlineData("en-US", "1234.5", "$1,234.50")]
    [InlineData("fr-FR", "1234.5", "$1,234.50")]
    [InlineData("fr-FR", "0", "$0.00")]
    [InlineData("en-US", "-12.5", "($12.50)")]
    public void FormatUsd_UsesDollarFormattingRegardlessOfCurrentCulture(string culture, string value, string expected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

            PriceFormatter.FormatUsd(amount).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
