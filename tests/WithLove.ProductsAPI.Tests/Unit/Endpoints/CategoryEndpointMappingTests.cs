using System.Reflection;
using WithLove.Data.Models;
using WithLove.ProductsAPI.DTOs;
using WithLove.ProductsAPI.Endpoints;

namespace WithLove.ProductsAPI.Tests.Unit.Endpoints;

/// <summary>
/// Tests for CategoryEndpoints.CategoryToResponse mapping.
/// Covers null/empty collection handling and field mapping.
/// </summary>
public class CategoryEndpointMappingTests
{
    private static readonly MethodInfo CategoryToResponseMethod =
        typeof(CategoryEndpoints).GetMethod("CategoryToResponse", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static CategoryResponse MapCategory(Category category)
    {
        return (CategoryResponse)CategoryToResponseMethod.Invoke(null, [category])!;
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithFullCategory_MapsAllFields()
    {
        // Arrange
        var category = CreateTestCategory();

        // Act
        var response = MapCategory(category);

        // Assert
        response.Id.Should().Be(category.Id);
        response.Name.Should().Be(category.Name);
        response.Description.Should().Be(category.Description);
        response.HeroTitle.Should().Be(category.HeroTitle);
        response.HeroSubtitle.Should().Be(category.HeroSubtitle);
        response.Image.Should().Be(category.Image);
        response.HeroImage.Should().Be(category.HeroImage);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithSubTypes_MapsSubTypesList()
    {
        // Arrange
        var category = CreateTestCategory();
        category.SubTypes = ["Bouquets", "Arrangements", "Singles"];

        // Act
        var response = MapCategory(category);

        // Assert
        response.SubTypes.Should().NotBeNull();
        response.SubTypes.Should().HaveCount(3);
        response.SubTypes.Should().Contain("Bouquets");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithEmptySubTypes_ReturnsNull()
    {
        // Arrange
        var category = CreateTestCategory();
        category.SubTypes = [];

        // Act
        var response = MapCategory(category);

        // Assert — empty list mapped to null for JSON omission
        response.SubTypes.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithOccasions_MapsOccasionsList()
    {
        // Arrange
        var category = CreateTestCategory();
        category.Occasions = ["Birthday", "Anniversary"];

        // Act
        var response = MapCategory(category);

        // Assert
        response.Occasions.Should().NotBeNull();
        response.Occasions.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithEmptyOccasions_ReturnsNull()
    {
        // Arrange
        var category = CreateTestCategory();
        category.Occasions = [];

        // Act
        var response = MapCategory(category);

        // Assert
        response.Occasions.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void CategoryToResponse_WithNullOptionalFields_MapsAsNull()
    {
        // Arrange
        var category = CreateTestCategory();
        category.Description = null;
        category.HeroSubtitle = null;
        category.Image = null;
        category.HeroImage = null;

        // Act
        var response = MapCategory(category);

        // Assert
        response.Description.Should().BeNull();
        response.HeroSubtitle.Should().BeNull();
        response.Image.Should().BeNull();
        response.HeroImage.Should().BeNull();
    }

    private static Category CreateTestCategory()
    {
        return new Category
        {
            Id = 1,
            Name = "Flora",
            Description = "Beautiful floral arrangements",
            HeroTitle = "Handcrafted Florals",
            HeroSubtitle = "Made with love",
            Image = "/images/flora.jpg",
            HeroImage = "/images/flora-hero.jpg",
            SubTypes = [],
            Occasions = [],
            RowVersion = [1, 2, 3]
        };
    }
}
