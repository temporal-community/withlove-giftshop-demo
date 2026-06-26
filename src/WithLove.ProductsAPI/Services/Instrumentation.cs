using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WithLove.ProductsAPI.Services;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "productsApi";
    internal const string ActivitySourceVersion = "1.0.0";

    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public Counter<long> SearchRequests { get; }
    public Histogram<long> SearchResultCount { get; }
    public Counter<long> CacheRequests { get; }
    public Counter<long> SearchFallbacks { get; }
    public Counter<long> VectorFailures { get; }
    public Counter<long> CategoryRequests { get; }

    public Instrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, ActivitySourceVersion);
        Meter = new Meter(ActivitySourceName, ActivitySourceVersion);

        SearchRequests = Meter.CreateCounter<long>(
            "product.search.requests",
            description: "Number of product search requests, tagged by strategy");

        SearchResultCount = Meter.CreateHistogram<long>(
            "product.search.result_count",
            description: "Distribution of results returned per search");

        CacheRequests = Meter.CreateCounter<long>(
            "product.cache.requests",
            description: "Number of product cache requests, tagged by outcome");

        SearchFallbacks = Meter.CreateCounter<long>(
            "product.search.fallback",
            description: "FTS unavailable — degraded to LIKE search");

        VectorFailures = Meter.CreateCounter<long>(
            "product.search.vector_failure",
            description: "Embedding generation failed — vector leg skipped");

        CategoryRequests = Meter.CreateCounter<long>(
            "product.category.requests",
            description: "Product list requests by category");
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
