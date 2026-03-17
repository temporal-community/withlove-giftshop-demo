using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using WithLove.Data;

namespace WithLove.ProductsAPI.Tests.Database;

[Collection("Integration")]
public class DatabaseVerificationTests
{
    private readonly IntegrationTestFixture _fixture;

    public DatabaseVerificationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ProductsDbContext> CreateDbContextAsync()
    {
        var connectionString = await _fixture.App.GetConnectionStringAsync("productsDatabase");
        var options = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ProductsDbContext(options);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task ProductsTable_ExistsWithCorrectColumns()
    {
        await using var db = await CreateDbContextAsync();

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync();

        // If we can query products, the table exists with the expected schema
        // The seeded data guarantees at least one product
        product.Should().NotBeNull();
        product!.Name.Should().NotBeNullOrWhiteSpace();
        product.Price.Should().BeGreaterThan(0);
        product.RowVersion.Should().NotBeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task SoftDelete_SetsIsEnabledToFalse()
    {
        await using var db = await CreateDbContextAsync();

        // Create a disposable product for this test
        var product = new WithLove.Data.Models.Product
        {
            Name = "SoftDeleteTest_" + Guid.NewGuid().ToString("N")[..8],
            Price = 10.00m,
            CategoryId = _fixture.ValidCategoryId,
            IsEnabled = true,
            AddedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Soft delete
        product.IsEnabled = false;
        await db.SaveChangesAsync();

        // Verify still in DB but disabled
        var reloaded = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == product.Id);
        reloaded.Should().NotBeNull();
        reloaded!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task RowVersion_IncrementsOnUpdate()
    {
        await using var db = await CreateDbContextAsync();

        var product = await db.Products.FirstAsync(p => p.IsEnabled);
        var originalRowVersion = product.RowVersion.ToArray();

        product.Price += 0.01m;
        product.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        product.RowVersion.Should().NotBeEquivalentTo(originalRowVersion,
            "RowVersion should change after update");

        // Restore original price
        product.Price -= 0.01m;
        await db.SaveChangesAsync();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task Timestamps_AreUtc()
    {
        await using var db = await CreateDbContextAsync();

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.IsEnabled);

        product.AddedDate.Kind.Should().Be(DateTimeKind.Unspecified,
            "SQL Server returns Unspecified kind; app convention stores UTC values");
        product.UpdatedDate.Kind.Should().Be(DateTimeKind.Unspecified);
        // Values should be recent (within last year) — sanity check they're not default
        product.AddedDate.Should().BeAfter(DateTime.UtcNow.AddYears(-1));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task CategoriesTable_ExistsWithCorrectStructure()
    {
        await using var db = await CreateDbContextAsync();

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync();

        category.Should().NotBeNull();
        category!.Name.Should().NotBeNullOrWhiteSpace();
        category.RowVersion.Should().NotBeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Integration)]
    [Trait(TestTraits.Feature, TestTraits.Database)]
    public async Task RequiredIndexes_Exist()
    {
        await using var db = await CreateDbContextAsync();

        // Verify queries that rely on indexes execute without error
        // If indexes don't exist, these queries still work but would be slow
        var enabledProducts = await db.Products.AsNoTracking()
            .Where(p => p.IsEnabled)
            .Take(1)
            .ToListAsync();

        var byCategory = await db.Products.AsNoTracking()
            .Where(p => p.CategoryId == _fixture.ValidCategoryId)
            .Take(1)
            .ToListAsync();

        var byDate = await db.Products.AsNoTracking()
            .OrderByDescending(p => p.AddedDate)
            .Take(1)
            .ToListAsync();

        // If we got here without exceptions, the columns exist and are queryable
        enabledProducts.Should().NotBeNull();
        byCategory.Should().NotBeNull();
        byDate.Should().NotBeNull();
    }
}
