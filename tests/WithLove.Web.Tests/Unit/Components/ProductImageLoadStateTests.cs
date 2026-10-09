using WithLove.Web.Components.Shared;

namespace WithLove.Web.Tests.Unit.Components;

public class ProductImageLoadStateTests
{
    [Fact]
    public void ShouldRender_AfterFailure_HidesFailedSourceAndResetsForReplacement()
    {
        var state = new ProductImageLoadState();

        state.ShouldRender("https://catalog.example/first.jpg").Should().BeTrue();

        state.MarkFailed();

        state.ShouldRender("https://catalog.example/first.jpg").Should().BeFalse();
        state.ShouldRender("https://catalog.example/replacement.jpg").Should().BeTrue();
        state.ShouldRender(null).Should().BeFalse();
    }
}
