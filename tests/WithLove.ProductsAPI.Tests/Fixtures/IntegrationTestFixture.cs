using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WithLove.Data;
using Xunit.Abstractions;

namespace WithLove.ProductsAPI.Tests.Fixtures;

/// <summary>
/// Shared AppHost fixture for all ProductsAPI integration tests.
/// Implements IAsyncLifetime to create AppHost once and share across all tests.
///
/// This fixture:
/// - Creates the full Aspire application stack (ProductsAPI, Redis, SQL Server, etc.)
/// - Configures logging with xUnit output integration
/// - Provides HttpClient factory for testing the ProductsAPI
/// - Manages AppHost lifecycle (initialization and disposal)
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Parameterless constructor required by xUnit fixture dependency injection.
    /// Note: ITestOutputHelper cannot be injected into fixtures, only test classes.
    /// </summary>
    public IntegrationTestFixture()
    {
    }

    /// <summary>
    /// The running DistributedApplication instance.
    /// </summary>
    public DistributedApplication App => _app ?? throw new InvalidOperationException(
        "AppHost not initialized. Ensure InitializeAsync has been called.");

    /// <summary>
    /// A valid category ID from the test database, available after InitializeAsync completes.
    /// Use this instead of hardcoded IDs to handle persistent container state across runs.
    /// </summary>
    public int ValidCategoryId { get; private set; }

    /// <summary>
    /// A valid product ID from the test database, available after InitializeAsync completes.
    /// </summary>
    public int ValidProductId { get; private set; }

    /// <summary>
    /// Initialize the AppHost and start all services.
    /// Called once per test class using IClassFixture.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Create the AppHost from the Aspire project (test mode skips Temporal/Web/WorkflowServer)
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.WithLove_AppHost>(args: ["TESTING=true"], cancellationToken: CancellationToken.None);

        // Configure logging
        appHost.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);

            // Configure log filters to reduce noise
            logging.AddFilter("Default", LogLevel.Information);
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            logging.AddFilter("Aspire.Hosting.Dcp", LogLevel.Warning);
            logging.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        });

        // Configure HTTP client defaults with standard resilience
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        // Build the AppHost
        _app = await appHost.BuildAsync().WaitAsync(DefaultTimeout);

        // Start the AppHost (launches containers, etc.)
        // Uses a longer timeout because SQL Server can take 60+ seconds to initialize
        // from scratch (first run or after container deletion).
        await _app.StartAsync().WaitAsync(StartupTimeout);

        // Wait for SQL Server container to be healthy before creating the database.
        // This must happen before waiting for productsApi because the API's WaitFor(productsDb)
        // dependency won't resolve until the database exists (important on persistent containers
        // where a previous test run may have dropped the database).
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("sqlServer", CancellationToken.None)
            .WaitAsync(DefaultTimeout);

        // Create database schema if it doesn't exist (no migrations — EnsureCreated builds
        // the schema directly from the EF Core model, matching DatabaseActivities.cs)
        var connectionString = await _app.GetConnectionStringAsync("productsDatabase");
        if (!string.IsNullOrEmpty(connectionString))
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProductsDbContext>();
            optionsBuilder.UseSqlServer(connectionString);
            using var migrateDbContext = new ProductsDbContext(optionsBuilder.Options);
            await migrateDbContext.Database.EnsureCreatedAsync();

            // Seed test data if not already present
            if (!await migrateDbContext.Categories.AnyAsync())
            {
                migrateDbContext.Categories.AddRange(
                    new WithLove.Data.Models.Category
                    {
                        Name = "Test Category 1",
                        Description = "A test category for products",
                        UpdatedDate = DateTime.UtcNow
                    },
                    new WithLove.Data.Models.Category
                    {
                        Name = "Test Category 2",
                        Description = "Another test category",
                        UpdatedDate = DateTime.UtcNow
                    }
                );

                await migrateDbContext.SaveChangesAsync();
            }

            // Seed test products if not already present
            if (!await migrateDbContext.Products.AnyAsync())
            {
                var categoryId = (await migrateDbContext.Categories.OrderBy(c => c.Id).FirstAsync()).Id;
                migrateDbContext.Products.AddRange(
                    new WithLove.Data.Models.Product
                    {
                        Name = "Rose Bouquet",
                        Description = "A beautiful rose bouquet",
                        Price = 49.99m,
                        CategoryId = categoryId,
                        IsEnabled = true,
                        AddedDate = DateTime.UtcNow.AddDays(-5),
                        UpdatedDate = DateTime.UtcNow.AddDays(-5)
                    },
                    new WithLove.Data.Models.Product
                    {
                        Name = "Lavender Bundle",
                        Description = "Fresh lavender arrangement",
                        Price = 35.00m,
                        CategoryId = categoryId,
                        IsEnabled = true,
                        AddedDate = DateTime.UtcNow.AddDays(-3),
                        UpdatedDate = DateTime.UtcNow.AddDays(-3)
                    },
                    new WithLove.Data.Models.Product
                    {
                        Name = "Sunflower Set",
                        Description = "Bright sunflower collection",
                        Price = 42.50m,
                        CategoryId = categoryId,
                        IsEnabled = true,
                        AddedDate = DateTime.UtcNow.AddDays(-1),
                        UpdatedDate = DateTime.UtcNow.AddDays(-1)
                    }
                );
                await migrateDbContext.SaveChangesAsync();
            }

            // Capture valid IDs for use in tests (persistent container may have non-1 IDs)
            var firstCategory = await migrateDbContext.Categories
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync();
            ValidCategoryId = firstCategory?.Id ?? 0;

            var firstProduct = await migrateDbContext.Products
                .Where(p => p.IsEnabled)
                .OrderBy(p => p.Id)
                .FirstOrDefaultAsync();
            ValidProductId = firstProduct?.Id ?? 0;
        }

        // Now wait for the ProductsAPI resource to report as healthy.
        // The database exists at this point so the API's WaitFor(productsDb) chain resolves.
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("productsApi", CancellationToken.None)
            .WaitAsync(DefaultTimeout);
    }

    /// <summary>
    /// Dispose the AppHost and clean up all resources.
    /// Called after all tests in the class complete.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Create an HttpClient configured for the ProductsAPI resource.
    /// </summary>
    public HttpClient CreateProductsApiClient()
    {
        return App.CreateHttpClient("productsApi");
    }

    /// <summary>
    /// Helper to create an HTTP request with required API headers.
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, PUT, DELETE, etc.)</param>
    /// <param name="uri">API endpoint URI (e.g., "/api/products")</param>
    /// <param name="apiVersion">X-WITHLOVE-API-VERSION header value (default: 2026-02-25)</param>
    /// <param name="idempotencyKey">Optional Idempotency-Key header for POST requests</param>
    /// <param name="ifNoneMatch">Optional If-None-Match header (ETag) for conditional GET requests</param>
    /// <param name="ifMatch">Optional If-Match header (ETag) for conditional PUT requests</param>
    /// <returns>HttpRequestMessage with proper headers configured</returns>
    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri,
        string apiVersion = "2026-02-25",
        string? idempotencyKey = null,
        string? ifNoneMatch = null,
        string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, uri);

        // Always include API version header
        request.Headers.Add("X-WITHLOVE-API-VERSION", apiVersion);

        // Add optional headers if provided
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            request.Headers.Add("If-None-Match", ifNoneMatch);
        }

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.Add("If-Match", ifMatch);
        }

        return request;
    }
}
