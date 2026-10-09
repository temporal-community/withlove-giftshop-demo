namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// The shared case table for the Stripe webhook signing secret shape check.
/// </summary>
/// <remarks>
/// <para>
/// <b>This table is one half of a pair.</b> Its twin is
/// <c>tests/WithLove.ProductsAPI.Tests/Unit/AppHost/StripeWebhookSecretValidationTests.cs</c>,
/// which exercises <c>WithLoveApplicationExtensions.ValidateStripeWebhookSecret</c> — the AppHost
/// pipeline step guarding the same value on the publish graph. The two implementations exist
/// separately because a standalone tool must not take a dependency on the AppHost, but a
/// divergence between two shape checks for one value is a defect that surfaces years later. The
/// cases below are deliberately the same cases, in the same order, with the same expected reasons.
/// Change one file and you must change the other.
/// </para>
/// <para>
/// Note what is <i>not</i> here: any case asserting that a real-but-dead secret is rejected. This
/// is a shape gate. <c>whsec_placeholder</c> passes by design, and so does the secret of an
/// endpoint deleted last month. Detecting those is <c>SecretFingerprint</c>'s job.
/// </para>
/// </remarks>
public class WebhookSecretShapeTests
{
    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData(null, "the value is empty")]
    [InlineData("", "the value is empty")]
    [InlineData("   ", "leading or trailing whitespace")]
    [InlineData(" whsec_abc123 ", "leading or trailing whitespace")]
    [InlineData("whsec_abc123\n", "leading or trailing whitespace")]
    [InlineData("\twhsec_abc123", "leading or trailing whitespace")]
    [InlineData("\"whsec_abc123\"", "wrapped in quote characters")]
    [InlineData("'whsec_abc123'", "wrapped in quote characters")]
    [InlineData("`whsec_abc123`", "wrapped in quote characters")]
    [InlineData("“whsec_abc123”", "wrapped in quote characters")]
    [InlineData("‘whsec_abc123’", "wrapped in quote characters")]
    [InlineData("“whsec_abc123", "wrapped in quote characters")]
    [InlineData("whsec_abc123”", "wrapped in quote characters")]
    [InlineData("sk_test_abc123", "does not start with 'whsec_'")]
    [InlineData("whsec-abc123", "does not start with 'whsec_'")]
    [InlineData("WHSEC_abc123", "does not start with 'whsec_'")]
    [InlineData("whsec_", "only the 'whsec_' prefix")]
    public void MalformedValue_IsRejectedWithTheSameReasonTheAppHostGives(string? value, string expectedReason)
    {
        WebhookSecretShape.Validate(value).Should().Contain(expectedReason);
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData("whsec_aBcD1234")]
    [InlineData("whsec_x")]
    [InlineData("whsec_1a2b3c4d5e6f7g8h9i0j1k2l3m4n5o6p")]
    public void WellFormedValue_IsAccepted(string value)
    {
        WebhookSecretShape.Validate(value).Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void DocumentedBootstrapPlaceholder_IsAccepted()
    {
        // `just deploy` seeds whsec_placeholder so the first deploy can resolve the parameter before
        // the FQDN it needs exists. If this check ever rejected it, the bootstrap deploy would fail
        // and the circular dependency would have no exit.
        WebhookSecretShape.Validate("whsec_placeholder").Should().BeNull();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData("sk_live_CanaryValue")]
    [InlineData(" whsec_CanaryValue ")]
    [InlineData("\"whsec_CanaryValue\"")]
    [InlineData("“whsec_CanaryValue”")]
    public void RejectionReason_NeverEchoesTheValue(string value)
    {
        // These reasons reach deploy logs and issue reports.
        WebhookSecretShape.Validate(value).Should().NotContain("CanaryValue");
    }
}
