using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WithLove.Data.Models;

namespace WithLove.Data;

/// <summary>
/// Entity Framework Core DbContext for WithLove Products API.
/// Configures Product and Category entities with:
/// - Soft delete pattern via IsEnabled flag
/// - Optimistic concurrency control via SQL Server rowVersion timestamps
/// - Automatic UTC timestamp management (AddedDate, UpdatedDate)
/// - Performance indexes on frequently-queried columns
/// - camelCase JSON serialization
/// </summary>
public class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : IdentityDbContext<ShopUser>(options)
{
    public virtual DbSet<Product> Products { get; set; } = null!;
    public virtual  DbSet<Category> Categories { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShopUser>(e =>
        {
            e.Property(p => p.FullName)
                .HasMaxLength(100);
            e.Property(p => p.StripeCustomerId)
                .HasMaxLength(100);
        });

        // Configure Product entity
        var productBuilder = modelBuilder.Entity<Product>();

        // Primary key
        productBuilder.HasKey(p => p.Id);

        // Concurrency control: RowVersion is a timestamp token
        productBuilder
            .Property(p => p.RowVersion)
            .IsRowVersion();

        // IsEnabled has database default value of 1 (true)
        productBuilder
            .Property(p => p.IsEnabled)
            .HasDefaultValue(true);

        // AddedDate and UpdatedDate are stored as UTC
        productBuilder
            .Property(p => p.AddedDate)
            .HasDefaultValueSql("GETUTCDATE()")
            .ValueGeneratedOnAdd();

        productBuilder
            .Property(p => p.UpdatedDate)
            .HasDefaultValueSql("GETUTCDATE()")
            .ValueGeneratedOnAddOrUpdate();

        // Currency precision: 18 digits total, 2 decimal places (handled by [Precision] attribute)
        productBuilder
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        // Configure indexes for performance
        // Index on IsEnabled for filtering active products
        productBuilder
            .HasIndex(p => p.IsEnabled)
            .HasDatabaseName("IX_Product_IsEnabled");

        // Index on AddedDate DESC for sorting by newest first
        productBuilder
            .HasIndex(p => p.AddedDate)
            .HasDatabaseName("IX_Product_AddedDate");

        // Index on CategoryId for filtering by category
        productBuilder
            .HasIndex(p => p.CategoryId)
            .HasDatabaseName("IX_Product_CategoryId");

        // Vector column for semantic search (1536 dims = text-embedding-3-small)
        productBuilder.Property(p => p.Embedding)
            .HasColumnType("vector(1536)");

        // JSON columns for owned entity collections
        modelBuilder.Entity<Product>().OwnsMany(p => p.Materials, b => b.ToJson());
        modelBuilder.Entity<Product>().OwnsMany(p => p.Features, b => b.ToJson());

        // FK relationship: Product → Category
        productBuilder
            .HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Category entity
        var categoryBuilder = modelBuilder.Entity<Category>();

        // Primary key
        categoryBuilder.HasKey(c => c.Id);

        // Concurrency control: RowVersion is a timestamp token
        categoryBuilder
            .Property(c => c.RowVersion)
            .IsRowVersion();

        // Indexes for category queries
        categoryBuilder
            .HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("IX_Category_Name_Unique");

        // Primitive JSON collections for Category
        categoryBuilder.PrimitiveCollection(c => c.SubTypes).HasColumnType("nvarchar(max)");
        categoryBuilder.PrimitiveCollection(c => c.Occasions).HasColumnType("nvarchar(max)");
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Automatically updates UpdatedDate to UTC now for modified entities.
    /// Only updates timestamp for entities that have been modified, not added.
    /// </summary>
    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Product or Category)
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity is Product product)
            {
                product.UpdatedDate = DateTime.UtcNow;
            }
        }
    }
}
