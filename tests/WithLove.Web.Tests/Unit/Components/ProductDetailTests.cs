using WithLove.Web.Components.Pages;

namespace WithLove.Web.Tests.Unit.Components;

public class ProductDetailTests
{
    [Fact]
    public async Task ParametersSet_WhenProductIdChanges_ReloadsProductAndRecommendations()
    {
        var firstProduct = new Product { Id = 10, Name = "Blush Peony" };
        var secondProduct = new Product { Id = 8, Name = "Silver Dollar" };
        var firstRecommendations = new List<Product> { new() { Id = 11, Name = "Letterpress Card" } };
        var secondRecommendations = new List<Product> { new() { Id = 6, Name = "Bespoke Wrapping" } };
        var productService = A.Fake<IProductService>();
        A.CallTo(() => productService.GetProductAsync(10)).Returns(firstProduct);
        A.CallTo(() => productService.GetRecommendationsAsync(10)).Returns(firstRecommendations);
        A.CallTo(() => productService.GetProductAsync(8)).Returns(secondProduct);
        A.CallTo(() => productService.GetRecommendationsAsync(8)).Returns(secondRecommendations);
        var component = new TestableProductDetail(productService);

        await component.ApplyProductIdAsync(10);

        component.CurrentProduct.Should().BeSameAs(firstProduct);
        component.CurrentRecommendations.Should().BeSameAs(firstRecommendations);

        await component.ApplyProductIdAsync(8);

        component.CurrentProduct.Should().BeSameAs(secondProduct);
        component.CurrentRecommendations.Should().BeSameAs(secondRecommendations);
        A.CallTo(() => productService.GetProductAsync(10)).MustHaveHappenedOnceExactly();
        A.CallTo(() => productService.GetRecommendationsAsync(10)).MustHaveHappenedOnceExactly();
        A.CallTo(() => productService.GetProductAsync(8)).MustHaveHappenedOnceExactly();
        A.CallTo(() => productService.GetRecommendationsAsync(8)).MustHaveHappenedOnceExactly();
    }

    private sealed class TestableProductDetail(IProductService productService) : ProductDetail
    {
        public Task ApplyProductIdAsync(int productId)
        {
            ProductService = productService;
            ProductId = productId;
            return OnParametersSetAsync();
        }
    }
}
