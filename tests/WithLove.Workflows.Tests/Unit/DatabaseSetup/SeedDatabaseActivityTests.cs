using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Stripe;
using System.Net;
using WithLove.Data;
using WithLove.Workflows.Activities;

namespace WithLove.Workflows.Tests.Unit.DatabaseSetup;

/// <summary>
/// Tests for <see cref="DatabaseActivities.SeedDatabaseAsync"/>.
/// Validates the reconciliation loop that makes seeding idempotent and crash-safe:
/// products already linked to Stripe are skipped; partially-seeded rows (present in
/// DB but missing StripePriceId) are resumed; running twice produces no duplicates.
/// </summary>
public class SeedDatabaseActivityTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Creates a fresh InMemory database for each test (unique name avoids cross-test bleed).</summary>
    private static ProductsDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<ProductsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DatabaseActivities BuildActivities(ProductsDbContext db, FakeStripeHttpClient fakeStripe)
    {
        var stripeClient = new StripeClient(new StripeClientOptions
        {
            // sk_test_ prefix satisfies the SDK's key-format guard.
            ApiKey     = "sk_test_fake_for_artemis_tests",
            HttpClient = fakeStripe,
        });
        var fakeEmbeddings = A.Fake<IEmbeddingGenerator<string, Embedding<float>>>();
        return new DatabaseActivities(db, stripeClient, fakeEmbeddings);
    }

    /// <summary>
    /// Populates the InMemory database with the canonical seed categories so the activity
    /// skips category-seeding and can load its categoryByName lookup from Local tracking.
    /// </summary>
    private static async Task SeedCategoriesAsync(ProductsDbContext db)
    {
        db.Categories.AddRange(SeedData.GetSeedCategories());
        await db.SaveChangesAsync();
    }

    // ─── Test 1: Skip already-complete products ───────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.DatabaseSetup)]
    public async Task SeedDatabase_SkipsProducts_WithStripePriceIdAlreadySet()
    {
        // Arrange ──────────────────────────────────────────────────────────────
        await using var db = CreateInMemoryDb();
        await SeedCategoriesAsync(db);

        // Build and persist all seed products as if a previous full run completed.
        var categoryByName = db.Categories.Local.ToDictionary(c => c.Name);
        var allProducts    = SeedData.GetSeedProducts(categoryByName);

        foreach (var p in allProducts)
            p.StripePriceId = "price_existing_fully_seeded";

        db.Products.AddRange(allProducts);
        await db.SaveChangesAsync();

        var fakeStripe  = new FakeStripeHttpClient();
        var activities  = BuildActivities(db, fakeStripe);

        // Act ──────────────────────────────────────────────────────────────────
        var env    = new ActivityEnvironment();
        var result = await env.RunAsync(() => activities.SeedDatabaseAsync());

        // Assert ───────────────────────────────────────────────────────────────
        result.CategoriesSeeded.Should().Be(0, "categories were pre-seeded");
        result.ProductsSeeded.Should().Be(0,
            "the reconciliation loop must skip every product that already has a StripePriceId");
        fakeStripe.TotalCalls.Should().Be(0,
            "no Stripe API call should be made when every product is already complete");
    }

    // ─── Test 2: Resume from a partially-seeded product ──────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.DatabaseSetup)]
    public async Task SeedDatabase_UpdatesStripePriceId_ForPartiallySeededProduct()
    {
        // Arrange ──────────────────────────────────────────────────────────────
        await using var db = CreateInMemoryDb();
        await SeedCategoriesAsync(db);

        var categoryByName = db.Categories.Local.ToDictionary(c => c.Name);
        var allProducts    = SeedData.GetSeedProducts(categoryByName);

        // Mark all products as fully seeded EXCEPT the first one, which exists in the
        // database but has no Stripe link — simulating a crash after DB insert but
        // before the StripePriceId was written back.
        var targetName = allProducts[0].Name;

        foreach (var p in allProducts.Skip(1))
            p.StripePriceId = "price_existing_skip_me";
        // allProducts[0].StripePriceId remains null (default)

        db.Products.AddRange(allProducts);
        await db.SaveChangesAsync();

        var fakeStripe = new FakeStripeHttpClient();
        var activities = BuildActivities(db, fakeStripe);

        // Act ──────────────────────────────────────────────────────────────────
        var env    = new ActivityEnvironment();
        var result = await env.RunAsync(() => activities.SeedDatabaseAsync());

        // Assert ───────────────────────────────────────────────────────────────
        result.ProductsSeeded.Should().Be(1,
            "only the partially-seeded product (no StripePriceId) should be processed");

        var updatedProduct = await db.Products.FirstAsync(p => p.Name == targetName);
        updatedProduct.StripePriceId.Should().NotBeNullOrEmpty(
            "the activity must write the Stripe price ID back to the database row");
    }

    // ─── Test 3: No duplicates when run a second time ─────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.DatabaseSetup)]
    public async Task SeedDatabase_DoesNotDuplicate_WhenRunTwice()
    {
        // Arrange ──────────────────────────────────────────────────────────────
        await using var db = CreateInMemoryDb();
        await SeedCategoriesAsync(db);
        // No products pre-seeded — first run will seed everything from scratch.

        var fakeStripe = new FakeStripeHttpClient();
        var activities = BuildActivities(db, fakeStripe);
        var env        = new ActivityEnvironment();

        // Act ──────────────────────────────────────────────────────────────────
        var firstResult         = await env.RunAsync(() => activities.SeedDatabaseAsync());
        var stripeCallsAfterRun1 = fakeStripe.TotalCalls;

        // Second execution — simulates a container restart after a successful run.
        var secondResult         = await env.RunAsync(() => activities.SeedDatabaseAsync());
        var stripeCallsAfterRun2 = fakeStripe.TotalCalls;

        // Assert ───────────────────────────────────────────────────────────────
        firstResult.ProductsSeeded.Should().BeGreaterThan(0,
            "the first run must seed all products");

        secondResult.ProductsSeeded.Should().Be(0,
            "the second run must skip all products that now have StripePriceId set");

        stripeCallsAfterRun2.Should().Be(stripeCallsAfterRun1,
            "the second run must make zero additional Stripe API calls");
    }

    // ─── Stripe HTTP fake ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IHttpClient"/> implementation that intercepts Stripe API calls
    /// and returns pre-canned JSON, keeping tests fully offline and deterministic.
    /// Tracks total call count so tests can verify no calls were made on a second run.
    /// </summary>
    private sealed class FakeStripeHttpClient : IHttpClient
    {
        private int _totalCalls;

        /// <summary>Running count of requests handled (thread-safe increment).</summary>
        public int TotalCalls => _totalCalls;

        public Task<StripeResponse> MakeRequestAsync(
            StripeRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _totalCalls);

            var path = request.Uri.AbsolutePath;
            string json;

            if (path == "/v1/products" && request.Method == HttpMethod.Post)
            {
                // ProductService.CreateAsync → POST /v1/products
                json = """
                    {
                        "id": "prod_test_001",
                        "object": "product",
                        "active": true,
                        "livemode": false,
                        "created": 1700000000,
                        "updated": 1700000000,
                        "metadata": {},
                        "images": []
                    }
                    """;
            }
            else if (path == "/v1/prices" && request.Method == HttpMethod.Post)
            {
                // PriceService.CreateAsync → POST /v1/prices
                json = """
                    {
                        "id": "price_test_001",
                        "object": "price",
                        "active": true,
                        "currency": "usd",
                        "livemode": false,
                        "created": 1700000000,
                        "metadata": {},
                        "product": "prod_test_001",
                        "unit_amount": 1000,
                        "type": "one_time",
                        "billing_scheme": "per_unit"
                    }
                    """;
            }
            else if (path.StartsWith("/v1/products/", StringComparison.Ordinal)
                     && request.Method == HttpMethod.Post)
            {
                // ProductService.UpdateAsync → POST /v1/products/{id}
                var productId = path["/v1/products/".Length..];
                json = $$"""
                    {
                        "id": "{{productId}}",
                        "object": "product",
                        "active": true,
                        "livemode": false,
                        "created": 1700000000,
                        "updated": 1700000001,
                        "metadata": {},
                        "images": []
                    }
                    """;
            }
            else
            {
                throw new InvalidOperationException(
                    $"FakeStripeHttpClient received an unexpected request: {request.Method} {request.Uri}");
            }

            // StripeResponse requires HttpResponseHeaders; get them from a throwaway message.
            var headers = new HttpResponseMessage().Headers;
            headers.TryAddWithoutValidation("Request-Id", "req_test_artemis_123");

            return Task.FromResult(new StripeResponse(HttpStatusCode.OK, headers, json));
        }

        public Task<StripeStreamedResponse> MakeStreamingRequestAsync(
            StripeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "Streaming requests are not used by SeedDatabaseAsync and are not faked.");
    }
}
