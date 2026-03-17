using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WithLove.Data.Models;

/// <summary>
/// Represents a product category in the WithLove Gift Shop.
/// Read-only via API; modifications through admin operations (out of scope).
/// </summary>
[Table("Categories")]
public class Category
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Category name is required")]
    [MaxLength(100, ErrorMessage = "Category name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }

    [Required]
    [MaxLength(255)]
    public string HeroTitle { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? HeroSubtitle { get; set; }

    [MaxLength(2000)]
    [Url]
    public string? Image { get; set; }

    [MaxLength(2000)]
    [Url]
    public string? HeroImage { get; set; }

    public List<string> SubTypes { get; set; } = [];

    public List<string> Occasions { get; set; } = [];

    /// <summary>
    /// Date and time the category was last updated, in UTC.
    /// Used for If-Modified-Since conditional requests.
    /// </summary>
    [Required]
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SQL Server timestamp for optimistic concurrency control.
    /// Used in ETags for If-Match/If-None-Match conditional requests.
    /// Automatically managed by EF Core.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
