namespace WithLove.ProductsAPI.Tests.Features;

[Collection("Integration")]
public class HealthCheckTests
{
    private readonly HttpClient _client;

    public HealthCheckTests(IntegrationTestFixture fixture)
    {
        _client = fixture.CreateProductsApiClient();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Health)]
    public async Task HealthEndpoint_Returns200Ok()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Health)]
    public async Task HealthEndpoint_IncludesHealthyStatus()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Healthy");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Health)]
    public async Task AliveEndpoint_Returns200Ok()
    {
        var response = await _client.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
