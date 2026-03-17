namespace WithLove.ProductsAPI.Tests.Features;

[Collection("Integration")]
public class ResponseHeaderTests
{
    private readonly IntegrationTestFixture _fixture;
    private readonly HttpClient _client;

    public ResponseHeaderTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateProductsApiClient();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    public async Task GetProduct_IncludesETagHeader()
    {
        var request = IntegrationTestFixture.CreateRequest(
            HttpMethod.Get, $"/api/products/{_fixture.ValidProductId}");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    public async Task GetProduct_IncludesLastModifiedHeader()
    {
        var request = IntegrationTestFixture.CreateRequest(
            HttpMethod.Get, $"/api/products/{_fixture.ValidProductId}");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.LastModified.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    public async Task GetProduct_IncludesCacheControlHeader()
    {
        var request = IntegrationTestFixture.CreateRequest(
            HttpMethod.Get, $"/api/products/{_fixture.ValidProductId}");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    public async Task Response_IncludesNosniffHeader()
    {
        var request = IntegrationTestFixture.CreateRequest(
            HttpMethod.Get, "/api/products");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Content-Type-Options");
    }
}
