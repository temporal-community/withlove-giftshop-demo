using System.Reflection;
using WithLove.Data.Models;
using WithLove.ProductsAPI.DTOs;
using WithLove.ProductsAPI.Endpoints;

namespace WithLove.ProductsAPI.Tests.Unit.Endpoints;

/// <summary>
/// Tests for ProductEndpoints.ProductToResponse mapping.
/// Covers null collection handling, navigation property access, and field mapping.
/// </summary>
public class ProductEndpointMappingTests
{
    // Access the private static method via reflection for direct unit testing
    private static readonly MethodInfo ProductToResponseMethod =
        typeof(ProductEndpoints).GetMethod("ProductToResponse", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static ProductResponse MapProduct(Product product)
    {
        return (ProductResponse)ProductToResponseMethod.Invoke(null, [product])!;
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithFullProduct_MapsAllFields()
    {
        // Arrange
        var product = CreateTestProduct();

        // Act
        var response = MapProduct(product);

        // Assert
        response.Id.Should().Be(product.Id);
        response.Name.Should().Be(product.Name);
        response.Description.Should().Be(product.Description);
        response.Price.Should().Be(product.Price);
        response.ImageUrl.Should().Be(product.ImageUrl);
        response.CategoryName.Should().Be(product.Category!.Name);
        response.StripePriceId.Should().Be(product.StripePriceId);
        response.SubCategory.Should().Be(product.SubCategory);
        response.StoryTitle.Should().Be(product.StoryTitle);
        response.StoryDescription.Should().Be(product.StoryDescription);
        response.IsEnabled.Should().Be(product.IsEnabled);
        response.AddedDate.Should().Be(product.AddedDate);
        response.UpdatedDate.Should().Be(product.UpdatedDate);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithMaterials_MapsMaterialsList()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Materials =
        [
            new ProductMaterial { Icon = "spa", Name = "Rose Petals" },
            new ProductMaterial { Icon = "eco", Name = "Organic Cotton" }
        ];

        // Act
        var response = MapProduct(product);

        // Assert
        response.Materials.Should().NotBeNull();
        response.Materials.Should().HaveCount(2);
        response.Materials![0].Icon.Should().Be("spa");
        response.Materials[0].Name.Should().Be("Rose Petals");
        response.Materials[1].Icon.Should().Be("eco");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithEmptyMaterials_ReturnsNull()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Materials = [];

        // Act
        var response = MapProduct(product);

        // Assert — empty list mapped to null for JSON omission
        response.Materials.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithFeatures_MapsFeaturesList()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Features =
        [
            new ProductFeature { Icon = "star", Title = "Handcrafted", Description = "Made by hand" }
        ];

        // Act
        var response = MapProduct(product);

        // Assert
        response.Features.Should().NotBeNull();
        response.Features.Should().HaveCount(1);
        response.Features![0].Icon.Should().Be("star");
        response.Features[0].Title.Should().Be("Handcrafted");
        response.Features[0].Description.Should().Be("Made by hand");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithEmptyFeatures_ReturnsNull()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Features = [];

        // Act
        var response = MapProduct(product);

        // Assert — empty list mapped to null for JSON omission
        response.Features.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_WithNullOptionalFields_MapsAsNull()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Description = null;
        product.ImageUrl = null;
        product.StripePriceId = null;
        product.SubCategory = null;
        product.StoryTitle = null;
        product.StoryDescription = null;

        // Act
        var response = MapProduct(product);

        // Assert
        response.Description.Should().BeNull();
        response.ImageUrl.Should().BeNull();
        response.StripePriceId.Should().BeNull();
        response.SubCategory.Should().BeNull();
        response.StoryTitle.Should().BeNull();
        response.StoryDescription.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void ProductToResponse_CategoryName_ComesFromNavigationProperty()
    {
        // Arrange
        var product = CreateTestProduct();
        product.Category = new Category { Id = 99, Name = "Unique Category" };

        // Act
        var response = MapProduct(product);

        // Assert — CategoryName comes from Category.Name, not CategoryId
        response.CategoryName.Should().Be("Unique Category");
    }

    private static Product CreateTestProduct()
    {
        return new Product
        {
            Id = 1,
            Name = "Test Product",
            Description = "A test product",
            Price = 29.99m,
            ImageUrl = "https://example.com/image.jpg",
            CategoryId = 1,
            Category = new Category { Id = 1, Name = "Test Category" },
            StripePriceId = "price_123",
            SubCategory = "Sub Cat",
            Materials = [],
            Features = [],
            StoryTitle = "Story",
            StoryDescription = "Once upon a time",
            IsEnabled = true,
            AddedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            RowVersion = [1, 2, 3, 4]
        };
    }
}
