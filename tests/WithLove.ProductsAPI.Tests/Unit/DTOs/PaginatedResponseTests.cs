using WithLove.ProductsAPI.DTOs;

namespace WithLove.ProductsAPI.Tests.Unit.DTOs;

/// <summary>
/// Tests for PaginatedResponse serialization and structure.
/// </summary>
public class PaginatedResponseTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void PaginatedResponse_WithItems_SerializesCorrectly()
    {
        // Arrange
        var response = new PaginatedResponse<string>(["a", "b"], "/next");

        // Act
        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("Value", out var value).Should().BeTrue();
        value.GetArrayLength().Should().Be(2);
        doc.RootElement.TryGetProperty("NextLink", out var next).Should().BeTrue();
        next.GetString().Should().Be("/next");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void PaginatedResponse_WithNullNextLink_OmitsNextLink()
    {
        // Arrange
        var response = new PaginatedResponse<int>([1, 2, 3]);

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<PaginatedResponse<int>>(json);

        // Assert
        deserialized!.NextLink.Should().BeNull();
        deserialized.Value.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void PaginatedResponse_WithEmptyArray_SerializesCorrectly()
    {
        // Arrange
        var response = new PaginatedResponse<string>(Array.Empty<string>());

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<PaginatedResponse<string>>(json);

        // Assert
        deserialized!.Value.Should().BeEmpty();
        deserialized.NextLink.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void PaginatedResponse_RoundTrips_SystemTextJson()
    {
        // Arrange
        var original = new PaginatedResponse<int>([10, 20], "/api/items?skip=2");

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PaginatedResponse<int>>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().BeEquivalentTo(original.Value);
        deserialized.NextLink.Should().Be(original.NextLink);
    }
}
