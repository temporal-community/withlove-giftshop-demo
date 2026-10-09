namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// Identity decides two things that are dangerous to get wrong: which endpoint the tool reconciles,
/// and which endpoint it is willing to delete. Both answers must be "only the one we created".
/// </summary>
public class WebhookIdentityTests
{
    private static Dictionary<string, string> OurMetadata(string tag = "azureprod") => new(StringComparer.Ordinal)
    {
        [WebhookIdentity.ManagedKey] = WebhookIdentity.ManagedValue,
        [WebhookIdentity.EnvironmentKey] = tag,
    };

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Matches_OurOwnMetadata()
    {
        new WebhookIdentity("azureprod").Matches(OurMetadata()).Should().BeTrue();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData("staging")]
    [InlineData("AZUREPROD")]
    [InlineData("azureprod ")]
    public void DoesNotMatch_ADifferentEnvironmentTag(string otherTag)
    {
        // Two environments share one Stripe account. Matching across them would mean `just destroy`
        // on staging deletes production's endpoint.
        new WebhookIdentity("azureprod").Matches(OurMetadata(otherTag)).Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void DoesNotMatch_AnEndpointWeDidNotCreate()
    {
        var identity = new WebhookIdentity("azureprod");

        // No metadata at all: somebody else's integration, or a Dashboard-created endpoint.
        identity.Matches(null).Should().BeFalse();
        identity.Matches(new Dictionary<string, string>(StringComparer.Ordinal)).Should().BeFalse();

        // Right environment tag but not marked managed -- a human copying our convention by hand.
        identity.Matches(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WebhookIdentity.EnvironmentKey] = "azureprod",
        }).Should().BeFalse();

        // Marked managed but for no environment.
        identity.Matches(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WebhookIdentity.ManagedKey] = WebhookIdentity.ManagedValue,
        }).Should().BeFalse();

        // Managed flag present but not "true".
        var falsey = OurMetadata();
        falsey[WebhookIdentity.ManagedKey] = "false";
        identity.Matches(falsey).Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void BuildMetadata_StampsIdentityAndAmbientAzureContext()
    {
        var identity = new WebhookIdentity("azureprod", "sub-1234", "rg-aspire-withlove");

        var metadata = identity.BuildMetadata(null);

        metadata[WebhookIdentity.ManagedKey].Should().Be(WebhookIdentity.ManagedValue);
        metadata[WebhookIdentity.EnvironmentKey].Should().Be("azureprod");
        metadata[WebhookIdentity.SubscriptionKey].Should().Be("sub-1234");
        metadata[WebhookIdentity.ResourceGroupKey].Should().Be("rg-aspire-withlove");
        metadata.Should().NotContainKey(WebhookIdentity.FingerprintKey);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void BuildMetadata_PreservesTheFingerprintAndAnyUnknownKeys()
    {
        // A Stripe metadata update REPLACES the whole map. The fingerprint is derived from a
        // signing secret that is readable exactly once, so a rebuild-from-scratch reconcile would
        // permanently destroy the only drift signal the design has. This test is that guarantee.
        var existing = OurMetadata();
        existing[WebhookIdentity.FingerprintKey] = "3f9a1b2c4d5e";
        existing["someone_elses_key"] = "leave me alone";

        var metadata = new WebhookIdentity("azureprod").BuildMetadata(existing);

        metadata[WebhookIdentity.FingerprintKey].Should().Be("3f9a1b2c4d5e");
        metadata["someone_elses_key"].Should().Be("leave me alone");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void BuildMetadata_DoesNotBlankAzureContextWhenTheEnvironmentIsNotSourced()
    {
        // `just ensure-stripe-webhook` sources .secrets.env; a bare `dotnet run` might not. Losing
        // the stamped subscription because of how the tool happened to be invoked is pure damage.
        var existing = OurMetadata();
        existing[WebhookIdentity.SubscriptionKey] = "sub-1234";

        var metadata = new WebhookIdentity("azureprod").BuildMetadata(existing);

        metadata[WebhookIdentity.SubscriptionKey].Should().Be("sub-1234");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void BuildMetadata_StampsTheFingerprintWhenGivenOne()
    {
        var metadata = new WebhookIdentity("azureprod").BuildMetadata(null, "abc123def456");

        metadata[WebhookIdentity.FingerprintKey].Should().Be("abc123def456");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Fingerprint_IsTwelveLowercaseHexCharsAndDistinguishesSecrets()
    {
        var first = SecretFingerprint.Compute("whsec_aaaaaaaaaaaaaaaaaaaaaaaa");
        var second = SecretFingerprint.Compute("whsec_bbbbbbbbbbbbbbbbbbbbbbbb");

        first.Should().HaveLength(12);
        first.Should().MatchRegex("^[0-9a-f]{12}$");
        first.Should().NotBe(second);
        SecretFingerprint.Compute("whsec_aaaaaaaaaaaaaaaaaaaaaaaa").Should().Be(first);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Fingerprint_DoesNotContainTheSecret()
    {
        const string secret = "whsec_verydistinctivevalue";

        SecretFingerprint.Compute(secret).Should().NotContain("verydistinctive");
    }
}
