namespace WithLove.StripeWebhooks.Tests.Traits;

/// <summary>
/// Shared xUnit trait constants. <c>Category</c> is a contract with
/// <c>.github/workflows/build.yml</c>, which partitions the suite into <c>Category=Unit</c> and
/// <c>Category!=Unit</c>. Every test in this project is a unit test — nothing here touches the
/// network, and nothing here may ever call the live Stripe API.
/// </summary>
public static class TestTraits
{
    public const string Category = "Category";
    public const string Unit = "Unit";

    public const string Feature = "Feature";
    public const string StripeWebhooks = "StripeWebhooks";
}
