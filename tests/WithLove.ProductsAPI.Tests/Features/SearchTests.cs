namespace WithLove.ProductsAPI.Tests.Features;

[Collection("Integration")]
public class SearchTests
{
    private readonly HttpClient _client;

    public SearchTests(IntegrationTestFixture fixture)
    {
        _client = fixture.CreateProductsApiClient();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public async Task SearchProducts_WithValidQuery_ReturnsMatches()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products/search?q=Rose");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        jsonDoc.RootElement.TryGetProperty("value", out var valueProp).Should().BeTrue();
        valueProp.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public async Task SearchProducts_CaseInsensitive_ReturnsMatches()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products/search?q=rose");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        jsonDoc.RootElement.TryGetProperty("value", out var valueProp).Should().BeTrue();
        valueProp.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public async Task SearchProducts_WithoutQuery_Returns400()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products/search");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Search)]
    public async Task SearchProducts_WithEmptyQuery_Returns400()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products/search?q=");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
