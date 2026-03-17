namespace WithLove.ProductsAPI.Tests.Unit.Utilities;

using WithLove.ProductsAPI.Utilities;
using WithLove.ProductsAPI.Models;
using System.Reflection;

/// <summary>
/// Unit tests for ProblemDetailsResults utility.
/// Focus: Creation of RFC 9457 Problem Details responses for various error scenarios.
/// </summary>
public class ProblemDetailsResultsTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void BadRequest_WithDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.BadRequest("Invalid input");

        // Assert
        result.Should().NotBeNull();
        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void BadRequest_WithDetailAndInstance_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.BadRequest("Invalid input", instance: "/api/products");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void BadRequest_WithDetailInstanceAndErrors_CreatesProblemDetailsResponse()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "Name", ["Name is required"] },
            { "Price", ["Price must be positive"] }
        };

        // Act
        var result = ProblemDetailsResults.BadRequest("Validation failed", instance: "/api/products", errors: errors);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void ValidationFailed_WithErrors_CreatesProblemDetailsResponse()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "Name", ["Name is required"] }
        };

        // Act
        var result = ProblemDetailsResults.ValidationFailed(errors);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void ValidationFailed_WithErrorsAndInstance_CreatesProblemDetailsResponse()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            { "Name", ["Name is required"] }
        };

        // Act
        var result = ProblemDetailsResults.ValidationFailed(errors, instance: "/api/products");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void NotFound_WithDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.NotFound("Product not found");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void NotFound_WithDetailAndInstance_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.NotFound("Product not found", instance: "/api/products/1");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void Conflict_WithDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.Conflict("SKU already exists");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void Conflict_WithDetailAndInstance_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.Conflict("SKU already exists", instance: "/api/products");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void PreconditionFailed_WithDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.PreconditionFailed("ETag mismatch");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void PreconditionFailed_WithDetailAndInstance_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.PreconditionFailed("ETag mismatch", instance: "/api/products/1");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void InternalServerError_WithDefaultDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.InternalServerError();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void InternalServerError_WithCustomDetail_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.InternalServerError("Database error");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void InternalServerError_WithDetailAndInstance_CreatesProblemDetailsResponse()
    {
        // Act
        var result = ProblemDetailsResults.InternalServerError("Database error", instance: "/api/products");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void FromException_WithException_CreatesProblemDetailsResponse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = ProblemDetailsResults.FromException(exception);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Contain("internal-server-error");
        result.Status.Should().Be(500);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void FromException_WithExceptionAndInstance_CreatesProblemDetailsResponse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = ProblemDetailsResults.FromException(exception, instance: "/api/products");

        // Assert
        result.Should().NotBeNull();
        result.Instance.Should().Be("/api/products");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void FromException_WithExceptionAndIncludeDetails_IncludesExceptionMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error message");

        // Act
        var result = ProblemDetailsResults.FromException(exception, includeDetails: true);

        // Assert
        result.Should().NotBeNull();
        result.Detail.Should().Contain("Test error message");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void FromException_WithoutIncludeDetails_DoesNotIncludeExceptionMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error message");

        // Act
        var result = ProblemDetailsResults.FromException(exception, includeDetails: false);

        // Assert
        result.Should().NotBeNull();
        result.Detail.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.ErrorHandling)]
    public void FromException_WithDifferentExceptionTypes_CreatesProblemDetailsResponse()
    {
        // Arrange
        var exception = new TimeoutException("Timeout occurred");

        // Act
        var result = ProblemDetailsResults.FromException(exception);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(500);
    }
}
