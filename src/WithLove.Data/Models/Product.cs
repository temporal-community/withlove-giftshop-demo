using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlTypes;

namespace WithLove.Data.Models;

/// <summary>
/// Represents a product in the WithLove Gift Shop catalog.
/// Uses soft delete pattern via IsEnabled flag and optimistic concurrency via RowVersion.
/// </summary>
[Table("Products")]
public class Product
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Product name is required")]
    [MaxLength(255, ErrorMessage = "Product name cannot exceed 255 characters")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(4000, ErrorMessage = "Description cannot exceed 4000 characters")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 999999.99, ErrorMessage = "Price must be between 0.01 and 999,999.99")]
    [Precision(18, 2)]
    public decimal Price { get; set; }

    [MaxLength(2000, ErrorMessage = "Image URL cannot exceed 2000 characters")]
    [Url(ErrorMessage = "ImageUrl must be a valid URL")]
    public string? ImageUrl { get; set; }

    [Required(ErrorMessage = "Category ID is required")]
    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    [MaxLength(30)]
    public string? StripePriceId { get; set; }

    [MaxLength(100)]
    public string? SubCategory { get; set; }

    public List<ProductMaterial> Materials { get; set; } = [];

    public List<ProductFeature> Features { get; set; } = [];

    [MaxLength(255)]
    public string? StoryTitle { get; set; }

    [MaxLength(4000)]
    public string? StoryDescription { get; set; }

    /// <summary>
    /// Soft delete flag. True = product is active and visible in API responses.
    /// False = product is logically deleted and excluded from all queries.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// UTC timestamp when product was created.
    /// Automatically set by database on insert.
    /// </summary>
    public DateTime AddedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when product was last modified.
    /// Automatically updated by database on update.
    /// </summary>
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Vector embedding for semantic search (1536 dimensions = text-embedding-3-small).
    /// Null until embedding is generated via EmbeddingService.
    /// </summary>
    [Column(TypeName = "vector(1536)")]
    public SqlVector<float>? Embedding { get; set; }

    /// <summary>
    /// SQL Server timestamp for optimistic concurrency control.
    /// Used in ETags for If-Match/If-None-Match conditional requests.
    /// Automatically managed by EF Core.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
