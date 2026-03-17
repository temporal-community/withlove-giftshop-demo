namespace WithLove.ProductsAPI.Tests.Features;

[Collection("Integration")]
public class PaginationTests
{
    private readonly HttpClient _client;

    public PaginationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.CreateProductsApiClient();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Pagination)]
    public async Task GetProducts_WithTopParameter_ReturnsLimitedResults()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products?top=2");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        jsonDoc.RootElement.TryGetProperty("value", out var valueProp).Should().BeTrue();
        valueProp.GetArrayLength().Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Pagination)]
    public async Task GetProducts_WithTopAndSkip_ReturnsCorrectPage()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products?top=2&skip=2");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Pagination)]
    public async Task GetProducts_ReturnsNextLink_WhenMoreResults()
    {
        // Request top=1 with 3 seeded products — should produce a nextLink
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products?top=1&skip=0");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var hasNextLink = jsonDoc.RootElement.TryGetProperty("nextLink", out var linkProp)
                          && linkProp.ValueKind == JsonValueKind.String;
        hasNextLink.Should().BeTrue("Should have nextLink when more results exist");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Pagination)]
    public async Task GetProducts_NoNextLink_WhenLastPage()
    {
        var request = IntegrationTestFixture.CreateRequest(HttpMethod.Get, "/api/products?top=100&skip=10000");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var hasNextLink = jsonDoc.RootElement.TryGetProperty("nextLink", out var linkProp)
                          && linkProp.ValueKind == JsonValueKind.String;
        hasNextLink.Should().BeFalse("Should not have nextLink when past all results");
    }
}
