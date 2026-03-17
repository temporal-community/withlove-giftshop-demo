using System.Text.Json;
using WithLove.Web.Models;

namespace WithLove.Web.Services;

/// <summary>
/// Utility methods to map ProductsAPI JSON responses to Web models.
/// Handles DTO-to-model conversions including nested objects and category ID resolution.
/// </summary>
public static class DtoMappers
{
    /// <summary>
    /// Maps ProductResponse JSON to Product Web model.
    /// Resolves CategoryName to CategoryId using the provided mapping.
    /// </summary>
    public static Product MapProductResponse(
        JsonElement productJson,
        Dictionary<string, int> categoryNameToIdMap)
    {
        var categoryName = GetStringProperty(productJson, "categoryName") ?? "";
        var categoryId = 0;

        // Resolve category name to ID
        if (!string.IsNullOrEmpty(categoryName) && categoryNameToIdMap.TryGetValue(categoryName, out var id))
        {
            categoryId = id;
        }

        return new Product
        {
            Id = GetIntProperty(productJson, "id"),
            Name = GetStringProperty(productJson, "name") ?? "",
            Description = GetStringProperty(productJson, "description") ?? "",
            Price = GetDecimalProperty(productJson, "price"),
            StripePriceId = GetStringProperty(productJson, "stripePriceId") ?? "",
            CategoryId = categoryId,
            SubCategory = GetStringProperty(productJson, "subCategory"),
            ImageUrl = GetStringProperty(productJson, "imageUrl"),
            Materials = MapMaterials(productJson),
            Features = MapFeatures(productJson),
            StoryTitle = GetStringProperty(productJson, "storyTitle"),
            StoryDescription = GetStringProperty(productJson, "storyDescription")
        };
    }

    /// <summary>
    /// Maps CategoryResponse JSON to Category Web model.
    /// </summary>
    public static Category MapCategoryResponse(JsonElement categoryJson)
    {
        return new Category
        {
            Id = GetIntProperty(categoryJson, "id"),
            Name = GetStringProperty(categoryJson, "name") ?? "",
            Description = GetStringProperty(categoryJson, "description"),
            HeroTitle = GetStringProperty(categoryJson, "heroTitle") ?? "",
            HeroSubtitle = GetStringProperty(categoryJson, "heroSubtitle") ?? "",
            Image = GetStringProperty(categoryJson, "image") ?? "",
            HeroImage = GetStringProperty(categoryJson, "heroImage") ?? "",
            SubTypes = GetStringArray(categoryJson, "subTypes"),
            Occasions = GetStringArray(categoryJson, "occasions")
        };
    }

    /// <summary>
    /// Maps array of ProductResponse JSON to list of Product models.
    /// </summary>
    public static List<Product> MapProductsResponse(
        JsonElement productsJson,
        Dictionary<string, int> categoryNameToIdMap)
    {
        var products = new List<Product>();

        if (productsJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in productsJson.EnumerateArray())
            {
                products.Add(MapProductResponse(item, categoryNameToIdMap));
            }
        }

        return products;
    }

    /// <summary>
    /// Maps array of CategoryResponse JSON to list of Category models.
    /// </summary>
    public static List<Category> MapCategoriesResponse(JsonElement categoriesJson)
    {
        var categories = new List<Category>();

        if (categoriesJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in categoriesJson.EnumerateArray())
            {
                categories.Add(MapCategoryResponse(item));
            }
        }

        return categories;
    }

    /// <summary>
    /// Maps ProductMaterialResponse JSON to ProductMaterial models.
    /// </summary>
    private static List<ProductMaterial> MapMaterials(JsonElement productJson)
    {
        var materials = new List<ProductMaterial>();

        if (productJson.TryGetProperty("materials", out var materialsElement) &&
            materialsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var mat in materialsElement.EnumerateArray())
            {
                var icon = GetStringProperty(mat, "icon") ?? "";
                var name = GetStringProperty(mat, "name") ?? "";
                materials.Add(new ProductMaterial(icon, name));
            }
        }

        return materials;
    }

    /// <summary>
    /// Maps ProductFeatureResponse JSON to ProductFeature models.
    /// </summary>
    private static List<ProductFeature> MapFeatures(JsonElement productJson)
    {
        var features = new List<ProductFeature>();

        if (productJson.TryGetProperty("features", out var featuresElement) &&
            featuresElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var feat in featuresElement.EnumerateArray())
            {
                var icon = GetStringProperty(feat, "icon") ?? "";
                var title = GetStringProperty(feat, "title") ?? "";
                var description = GetStringProperty(feat, "description") ?? "";
                features.Add(new ProductFeature(icon, title, description));
            }
        }

        return features;
    }

    // Helper methods for JSON property extraction
    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            return property.GetString();
        }
        return null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number)
        {
            return property.GetInt32();
        }
        return 0;
    }

    private static decimal GetDecimalProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number)
        {
            return property.GetDecimal();
        }
        return 0m;
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        var result = new List<string>();

        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.EnumerateArray())
            {
                var value = item.GetString();
                if (value != null)
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }
}
