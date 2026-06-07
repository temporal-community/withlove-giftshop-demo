using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Stripe;
using Temporalio.Activities;
using WithLove.Data;

namespace WithLove.Workflows.Activities;

public class DatabaseActivities(
    ProductsDbContext dbContext,
    StripeClient stripeClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    [Activity]
    public async Task<MigrationResult> ApplyMigrationsAsync()
    {
        var logger = ActivityExecutionContext.Current.Logger;
        var created = await dbContext.Database.EnsureCreatedAsync();

        if (created)
        {
            logger.LogInformation("Database schema created successfully");
            return new MigrationResult(1, "Database schema created successfully");
        }

        logger.LogInformation("Database already exists, no changes made");
        return new MigrationResult(0, "Database already exists");
    }

    [Activity]
    public async Task ApplySchemaUpgradesAsync()
    {
        var logger = ActivityExecutionContext.Current.Logger;

        // Create full-text catalog if it doesn't exist
        await dbContext.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'WithLoveCatalog')
            BEGIN
                CREATE FULLTEXT CATALOG WithLoveCatalog AS DEFAULT;
            END
            """);

        // Create full-text index on Products (Name, Description) if it doesn't exist
        await dbContext.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.fulltext_indexes
                WHERE object_id = OBJECT_ID('Products')
            )
            BEGIN
                CREATE FULLTEXT INDEX ON Products(Name, Description)
                    KEY INDEX PK_Products ON WithLoveCatalog
                    WITH CHANGE_TRACKING AUTO;
            END
            """);
        logger.LogInformation("Ensured full-text catalog and index exist on Products table");

        // Index for loyalty point lookup: ResolveUserIdByStripeCustomerIdAsync runs on every order
        await dbContext.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_AspNetUsers_StripeCustomerId'
                AND object_id = OBJECT_ID('AspNetUsers')
            )
            BEGIN
                CREATE INDEX IX_AspNetUsers_StripeCustomerId
                ON AspNetUsers (StripeCustomerId)
                WHERE StripeCustomerId IS NOT NULL
            END");

        logger.LogInformation("Ensured index IX_AspNetUsers_StripeCustomerId exists on AspNetUsers table");
    }

    [Activity]
    public async Task<SeedResult> SeedDatabaseAsync()
    {
        var categoriesSeeded = 0;
        var productsSeeded = 0;
        var logger = ActivityExecutionContext.Current.Logger;
        
        if (!await dbContext.Categories.AnyAsync())
        {
            var categories = SeedData.GetSeedCategories();
            dbContext.Categories.AddRange(categories);
            await dbContext.SaveChangesAsync();
            
            categoriesSeeded = categories.Count;
            logger.LogInformation("Seeded {Count} categories", categoriesSeeded);
        }
        else
        {
            logger.LogInformation("Categories already exist, skipping category seeding");
        }

        if (!await dbContext.Products.AnyAsync())
        {
            // Ensure categories are tracked — they may already be in Local from the
            // seeding above, or we need to load them if they existed before this run.
            if (!dbContext.Categories.Local.Any())
                await dbContext.Categories.LoadAsync();

            var categoryByName = dbContext.Categories.Local.ToDictionary(c => c.Name);
            var products = SeedData.GetSeedProducts(categoryByName);

            // Create a matching Stripe product + price for each seed product and
            // capture the price ID before persisting so it's saved in one round-trip.
            var stripeProductService = stripeClient.V1.Products;
            var stripePriceService = stripeClient.V1.Prices;

            foreach (var product in products)
            {
                var stripeProduct = await stripeProductService.CreateAsync(new ProductCreateOptions
                {
                    Name = product.Name,
                    Description = product.Description,
                    Images = product.ImageUrl is not null ? [product.ImageUrl] : null,
                });

                var stripePrice = await stripePriceService.CreateAsync(new PriceCreateOptions
                {
                    Product = stripeProduct.Id,
                    Currency = "usd",
                    UnitAmount = (long)(product.Price * 100),
                });
                
                var productOptions = new ProductUpdateOptions
                {
                    DefaultPrice = stripePrice.Id,
                };
                
                // Set the default price on the Stripe product
                await stripeProductService.UpdateAsync(stripeProduct.Id, productOptions);

                product.StripePriceId = stripePrice.Id;

                logger.LogInformation(
                    "Created Stripe product {StripeProductId} / price {StripePriceId} for '{ProductName}'",
                    stripeProduct.Id, stripePrice.Id, product.Name);
            }

            dbContext.Products.AddRange(products);
            await dbContext.SaveChangesAsync();
            productsSeeded = products.Count;
            logger.LogInformation("Seeded {Count} products", productsSeeded);
        }
        else
        {
            logger.LogInformation("Products already exist, skipping product seeding");
        }

        return new SeedResult(categoriesSeeded, productsSeeded);
    }

    [Activity]
    public async Task<EmbeddingResult> GenerateEmbeddingsAsync()
    {
        var logger = ActivityExecutionContext.Current.Logger;

        var products = await dbContext.Products
            .Include(p => p.Category)
            .Where(p => p.IsEnabled && p.Embedding == null)
            .ToListAsync();

        if (products.Count == 0)
        {
            logger.LogInformation("All products already have embeddings");
            return new EmbeddingResult(0);
        }

        logger.LogInformation("Generating embeddings for {Count} products", products.Count);

        var texts = products.Select(GenerateEmbeddingText).ToList();
        var results = await embeddingGenerator.GenerateAsync(texts);

        for (int i = 0; i < products.Count; i++)
        {
            products[i].Embedding = new SqlVector<float>(results[i].Vector.ToArray());
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Generated embeddings for {Count} products", products.Count);

        return new EmbeddingResult(products.Count);
    }

    private static string GenerateEmbeddingText(WithLove.Data.Models.Product product)
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
}

public record MigrationResult(int AppliedCount, string Message);
public record SeedResult(int CategoriesSeeded, int ProductsSeeded);
public record EmbeddingResult(int ProductsEmbedded);
public record DatabaseSetupResult(MigrationResult Migration, SeedResult Seed, EmbeddingResult? Embedding);
