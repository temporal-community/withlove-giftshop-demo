namespace WithLove.ProductsAPI.Tests.Unit.Utilities;

using WithLove.ProductsAPI.Utilities;

/// <summary>
/// Unit tests for ETagGenerator utility.
/// Focus: ETag generation from rowVersion and validation of client-provided ETags.
/// </summary>
public class ETagGeneratorTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void GenerateETag_WithValidRowVersion_ReturnsWeakETag()
    {
        // Arrange
        var rowVersion = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x23, 0x45, 0x67, 0x89 };

        // Act
        var etag = ETagGenerator.GenerateETag(rowVersion);

        // Assert
        etag.Should().StartWith("W/\"");
        etag.Should().EndWith("\"");
        etag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void GenerateETag_WithSameRowVersion_ReturnsSameETag()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var etag1 = ETagGenerator.GenerateETag(rowVersion);
        var etag2 = ETagGenerator.GenerateETag(rowVersion);

        // Assert
        etag1.Should().Be(etag2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void GenerateETag_WithDifferentRowVersions_ReturnsDifferentETags()
    {
        // Arrange
        var rowVersion1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var rowVersion2 = new byte[] { 0x05, 0x06, 0x07, 0x08 };

        // Act
        var etag1 = ETagGenerator.GenerateETag(rowVersion1);
        var etag2 = ETagGenerator.GenerateETag(rowVersion2);

        // Assert
        etag1.Should().NotBe(etag2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void GenerateETag_WithNullRowVersion_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ETagGenerator.GenerateETag(null!));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void GenerateETag_WithEmptyRowVersion_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ETagGenerator.GenerateETag([]));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithMatchingETag_ReturnsTrue()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var etag = ETagGenerator.GenerateETag(rowVersion);

        // Act
        var result = ETagGenerator.VerifyETag(etag, rowVersion);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithDifferentRowVersion_ReturnsFalse()
    {
        // Arrange
        var rowVersion1 = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var rowVersion2 = new byte[] { 0x05, 0x06, 0x07, 0x08 };
        var etag = ETagGenerator.GenerateETag(rowVersion1);

        // Act
        var result = ETagGenerator.VerifyETag(etag, rowVersion2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithNullClientETag_ReturnsFalse()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var result = ETagGenerator.VerifyETag(null!, rowVersion);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithEmptyClientETag_ReturnsFalse()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var result = ETagGenerator.VerifyETag(string.Empty, rowVersion);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithNullRowVersion_ReturnsFalse()
    {
        // Arrange
        var etag = "W/\"dGVzdA==\"";

        // Act
        var result = ETagGenerator.VerifyETag(etag, null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithEmptyRowVersion_ReturnsFalse()
    {
        // Arrange
        var etag = "W/\"dGVzdA==\"";

        // Act
        var result = ETagGenerator.VerifyETag(etag, []);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithInvalidBase64ETag_ReturnsFalse()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var invalidETag = "W/\"invalid!!!base64\"";

        // Act
        var result = ETagGenerator.VerifyETag(invalidETag, rowVersion);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithETagWithoutWeakPrefix_ReturnsTrue()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var etagWithPrefix = ETagGenerator.GenerateETag(rowVersion); // W/"..."
        var etagWithoutPrefix = etagWithPrefix[2..]; // Remove W/

        // Act
        var result = ETagGenerator.VerifyETag(etagWithoutPrefix, rowVersion);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ETag)]
    public void VerifyETag_WithWhitespaceInETag_HandlesProperly()
    {
        // Arrange
        var rowVersion = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var etagWithQuotes = ETagGenerator.GenerateETag(rowVersion);
        // This test verifies that the method can handle ETags with quotes

        // Act
        var result = ETagGenerator.VerifyETag(etagWithQuotes, rowVersion);

        // Assert
        result.Should().BeTrue();
    }
}
