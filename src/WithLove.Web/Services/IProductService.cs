using WithLove.Web.Models;

namespace WithLove.Web.Services;

public interface IProductService
{
    Task<List<Product>> GetProductsAsync();
    Task<List<Product>> GetFeaturedProductsAsync();
    Task<List<Product>> GetSmallLuxuriesAsync();
    Task<List<Category>> GetCategoriesAsync();
    Task<Category?> GetCategoryAsync(int categoryId);
    Task<List<Product>> GetProductsByCategoryAsync(int categoryId);
    Task<Product?> GetProductAsync(int productId);
    Task<List<Product>> GetRecommendationsAsync(int productId);
    Task<List<Product>> SearchProductsAsync(string query, CancellationToken cancellationToken = default);
    Task<List<GiftEnhancement>> GetGiftEnhancementsAsync();
}
