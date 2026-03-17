namespace WithLove.Web.Tests.Unit.Services;

public class DtoMappersTests
{
    private static readonly Dictionary<string, int> CategoryMap = new()
    {
        ["Cacao"] = 1,
        ["Flora"] = 2
    };

    #region MapProductResponse

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_MapsAllFields()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 42,
            "name": "Rose Bouquet",
            "description": "A lovely bouquet",
            "price": 49.99,
            "categoryName": "Flora",
            "subCategory": "Seasonal",
            "imageUrl": "https://example.com/rose.jpg",
            "storyTitle": "The Rose Story",
            "storyDescription": "Handpicked roses",
            "materials": [
                { "icon": "leaf", "name": "Organic Petals" }
            ],
            "features": [
                { "icon": "star", "title": "Premium", "description": "Top quality" }
            ]
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.Id.Should().Be(42);
        product.Name.Should().Be("Rose Bouquet");
        product.Description.Should().Be("A lovely bouquet");
        product.Price.Should().Be(49.99m);
        product.CategoryId.Should().Be(2);
        product.SubCategory.Should().Be("Seasonal");
        product.ImageUrl.Should().Be("https://example.com/rose.jpg");
        product.StoryTitle.Should().Be("The Rose Story");
        product.StoryDescription.Should().Be("Handpicked roses");
        product.Materials.Should().HaveCount(1);
        product.Materials[0].Icon.Should().Be("leaf");
        product.Materials[0].Name.Should().Be("Organic Petals");
        product.Features.Should().HaveCount(1);
        product.Features[0].Icon.Should().Be("star");
        product.Features[0].Title.Should().Be("Premium");
        product.Features[0].Description.Should().Be("Top quality");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_UnknownCategory_DefaultsToZero()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Test",
            "description": "",
            "price": 10,
            "categoryName": "Unknown"
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.CategoryId.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_MissingOptionalFields_DefaultsToNull()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Minimal",
            "description": "Basic",
            "price": 5
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.SubCategory.Should().BeNull();
        product.ImageUrl.Should().BeNull();
        product.StoryTitle.Should().BeNull();
        product.StoryDescription.Should().BeNull();
        product.Materials.Should().BeEmpty();
        product.Features.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_NullCategoryName_DefaultsToZero()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Test",
            "description": "",
            "price": 10,
            "categoryName": null
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.CategoryId.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_EmptyCategoryName_DefaultsToZero()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Test",
            "description": "",
            "price": 10,
            "categoryName": ""
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.CategoryId.Should().Be(0);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_MultipleMaterials()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Test",
            "description": "",
            "price": 10,
            "materials": [
                { "icon": "a", "name": "Material A" },
                { "icon": "b", "name": "Material B" },
                { "icon": "c", "name": "Material C" }
            ]
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.Materials.Should().HaveCount(3);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductResponse_MultipleFeatures()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Test",
            "description": "",
            "price": 10,
            "features": [
                { "icon": "a", "title": "F1", "description": "D1" },
                { "icon": "b", "title": "F2", "description": "D2" }
            ]
        }
        """).RootElement;

        var product = DtoMappers.MapProductResponse(json, CategoryMap);

        product.Features.Should().HaveCount(2);
        product.Features[1].Title.Should().Be("F2");
    }

    #endregion

    #region MapCategoryResponse

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapCategoryResponse_MapsAllFields()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 5,
            "name": "Botanica",
            "description": "Plant-based gifts",
            "heroTitle": "The Garden",
            "heroSubtitle": "Nature's finest",
            "image": "https://example.com/img.jpg",
            "heroImage": "https://example.com/hero.jpg",
            "subTypes": ["Indoor", "Outdoor"],
            "occasions": ["Birthday", "Holiday"]
        }
        """).RootElement;

        var category = DtoMappers.MapCategoryResponse(json);

        category.Id.Should().Be(5);
        category.Name.Should().Be("Botanica");
        category.Description.Should().Be("Plant-based gifts");
        category.HeroTitle.Should().Be("The Garden");
        category.HeroSubtitle.Should().Be("Nature's finest");
        category.Image.Should().Be("https://example.com/img.jpg");
        category.HeroImage.Should().Be("https://example.com/hero.jpg");
        category.SubTypes.Should().BeEquivalentTo(["Indoor", "Outdoor"]);
        category.Occasions.Should().BeEquivalentTo(["Birthday", "Holiday"]);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapCategoryResponse_MissingOptionalFields_DefaultsToEmpty()
    {
        var json = JsonDocument.Parse("""
        {
            "id": 1,
            "name": "Minimal"
        }
        """).RootElement;

        var category = DtoMappers.MapCategoryResponse(json);

        category.Description.Should().BeNull();
        category.HeroTitle.Should().BeEmpty();
        category.SubTypes.Should().BeEmpty();
        category.Occasions.Should().BeEmpty();
    }

    #endregion

    #region MapProductsResponse / MapCategoriesResponse

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductsResponse_MapsArrayOfProducts()
    {
        var json = JsonDocument.Parse("""
        [
            { "id": 1, "name": "A", "description": "", "price": 10 },
            { "id": 2, "name": "B", "description": "", "price": 20 }
        ]
        """).RootElement;

        var products = DtoMappers.MapProductsResponse(json, CategoryMap);

        products.Should().HaveCount(2);
        products[0].Name.Should().Be("A");
        products[1].Name.Should().Be("B");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductsResponse_NonArray_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("""{ "id": 1 }""").RootElement;

        var products = DtoMappers.MapProductsResponse(json, CategoryMap);

        products.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapCategoriesResponse_MapsArrayOfCategories()
    {
        var json = JsonDocument.Parse("""
        [
            { "id": 1, "name": "Cat A" },
            { "id": 2, "name": "Cat B" }
        ]
        """).RootElement;

        var categories = DtoMappers.MapCategoriesResponse(json);

        categories.Should().HaveCount(2);
        categories[0].Name.Should().Be("Cat A");
        categories[1].Name.Should().Be("Cat B");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapCategoriesResponse_NonArray_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("""{ "id": 1 }""").RootElement;

        var categories = DtoMappers.MapCategoriesResponse(json);

        categories.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Mapping)]
    public void MapProductsResponse_EmptyArray_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("[]").RootElement;

        var products = DtoMappers.MapProductsResponse(json, CategoryMap);

        products.Should().BeEmpty();
    }

    #endregion
}
