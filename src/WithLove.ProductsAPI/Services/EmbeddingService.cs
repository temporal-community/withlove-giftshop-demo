using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WithLove.Data;
using WithLove.Data.Models;

namespace WithLove.ProductsAPI.Services;

/// <summary>
/// Generates and stores vector embeddings for products using OpenAI text-embedding-3-small.
/// Embeddings are used for semantic (vector) search in the hybrid search pipeline.
/// </summary>
public class EmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ProductsDbContext dbContext,
    ILogger<EmbeddingService> logger)
{
    /// <summary>
    /// Builds a text representation of a product for embedding generation.
    /// Concatenates all searchable fields into a single string.
    /// </summary>
    public static string GenerateEmbeddingText(Product product)
    {
        var parts = new List<string> { product.Name };

        if (!string.IsNullOrWhiteSpace(product.Description))
            parts.Add(product.Description);

        if (product.Category?.Name is not null)
            parts.Add($"Category: {product.Category.Name}");

        if (!string.IsNullOrWhiteSpace(product.SubCategory))
            parts.Add($"Subcategory: {product.SubCategory}");

        if (product.Materials.Count > 0)
            parts.Add("Materials: " + string.Join(", ", product.Materials.Select(m => m.Name)));

        if (product.Features.Count > 0)
            parts.Add("Features: " + string.Join(", ", product.Features.Select(f => f.Title)));

        if (!string.IsNullOrWhiteSpace(product.StoryDescription))
            parts.Add(product.StoryDescription);

        return string.Join(". ", parts);
    }

    /// <summary>
    /// Generate and save embedding for a single product.
    /// </summary>
    public async Task EmbedProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        var text = GenerateEmbeddingText(product);

        var result = await embeddingGenerator.GenerateAsync(
            [text], cancellationToken: cancellationToken);

        var vector = result[0].Vector.ToArray();
        product.Embedding = new SqlVector<float>(vector);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Generated embedding for product {ProductId}: {ProductName}", product.Id, product.Name);
    }

    /// <summary>
    /// Backfill embeddings for all products that don't have one yet.
    /// Called on startup to seed existing products.
    /// </summary>
    public async Task EmbedAllProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await dbContext.Products
            .Include(p => p.Category)
            .Where(p => p.IsEnabled && p.Embedding == null)
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            logger.LogInformation("All products already have embeddings");
            return;
        }

        logger.LogInformation("Generating embeddings for {Count} products", products.Count);

        // Batch embed for efficiency
        var texts = products.Select(GenerateEmbeddingText).ToList();
        var results = await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);

        for (int i = 0; i < products.Count; i++)
        {
            var vector = results[i].Vector.ToArray();
            products[i].Embedding = new SqlVector<float>(vector);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Generated embeddings for {Count} products", products.Count);
    }

    /// <summary>
    /// Generate an embedding vector for a search query string.
    /// Used at search time to compare against product embeddings.
    /// </summary>
    public async Task<SqlVector<float>> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default)
    {
        var result = await embeddingGenerator.GenerateAsync(
            [query], cancellationToken: cancellationToken);

        return new SqlVector<float>(result[0].Vector.ToArray());
    }
}
