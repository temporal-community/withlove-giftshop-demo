using WithLove.ProductsAPI.DTOs;

namespace WithLove.ProductsAPI.Tests.Unit.DTOs;

/// <summary>
/// Tests for CachedPage serialization safety.
/// Prevents regression of the ValueTuple serialization bug where
/// FusionCache deserialized stale tuple entries as null Items.
/// </summary>
public class CachedPageTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_RoundTripsThrough_SystemTextJson()
    {
        // Arrange
        var original = new CachedPage<string>(["a", "b", "c"], 3);

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CachedPage<string>>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeEquivalentTo(original.Items);
        deserialized.Total.Should().Be(original.Total);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_WithEmptyList_RoundTripsCorrectly()
    {
        // Arrange
        var original = new CachedPage<int>([], 0);

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CachedPage<int>>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeEmpty();
        deserialized.Total.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_JsonPropertyNames_AreCamelCase()
    {
        // Arrange — verify the JSON shape matches what FusionCache will store
        var page = new CachedPage<string>(["x"], 1);

        // Act
        var json = JsonSerializer.Serialize(page);
        var doc = JsonDocument.Parse(json);

        // Assert — System.Text.Json uses PascalCase by default for records
        doc.RootElement.TryGetProperty("Items", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Total", out _).Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_DeserializeFromNullItems_ItemsIsNull()
    {
        // Arrange — simulate a corrupted or mismatched cache entry
        var json = """{"Items":null,"Total":5}""";

        // Act
        var deserialized = JsonSerializer.Deserialize<CachedPage<string>>(json);

        // Assert — Items will be null; callers must null-guard
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeNull();
        deserialized.Total.Should().Be(5);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_DeserializeFromMismatchedShape_ItemsIsNull()
    {
        // Arrange — simulate deserializing old ValueTuple-shaped JSON
        var json = """{"Item1":[{"Id":1}],"Item2":1}""";

        // Act
        var deserialized = JsonSerializer.Deserialize<CachedPage<string>>(json);

        // Assert — mismatched property names mean Items/Total get defaults
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeNull();
        deserialized.Total.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Caching)]
    public void CachedPage_WithComplexType_RoundTripsCorrectly()
    {
        // Arrange — use a record similar to ProductResponse/CategoryResponse
        var items = new List<TestItem>
        {
            new(1, "Product A", 19.99m),
            new(2, "Product B", 29.99m),
        };
        var original = new CachedPage<TestItem>(items, 2);

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CachedPage<TestItem>>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().HaveCount(2);
        deserialized.Items[0].Name.Should().Be("Product A");
        deserialized.Items[1].Price.Should().Be(29.99m);
        deserialized.Total.Should().Be(2);
    }

    private record TestItem(int Id, string Name, decimal Price);
}
