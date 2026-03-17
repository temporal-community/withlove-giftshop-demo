namespace WithLove.ProductsAPI.Tests.Traits;

/// <summary>
/// xUnit Trait constants for categorizing tests.
/// Usage: [Trait(TestTraits.Category, TestTraits.Integration)]
/// </summary>
public static class TestTraits
{
    public const string Category = "Category";
    public const string Integration = "Integration";
    public const string Unit = "Unit";

    // Feature-level categorization
    public const string Feature = "Feature";
    public const string Validation = "Validation";
    public const string Idempotency = "Idempotency";
    public const string Concurrency = "Concurrency";
    public const string Pagination = "Pagination";
    public const string Search = "Search";
    public const string SoftDelete = "SoftDelete";
    public const string ErrorHandling = "ErrorHandling";
    public const string Health = "Health";
    public const string Database = "Database";
    public const string Caching = "Caching";

    // Duration categorization for long-running tests
    public const string Duration = "Duration";
    public const string Long = "Long";

    // Unit test specific traits
    public const string ETag = "ETag";
    public const string ApiVersion = "ApiVersion";
    public const string ResponseHeaders = "ResponseHeaders";
}
