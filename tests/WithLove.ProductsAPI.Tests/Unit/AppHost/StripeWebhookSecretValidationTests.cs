namespace WithLove.ProductsAPI.Tests.Unit.AppHost;

using global::Aspire.Hosting;
using global::WithLove.AppHost.Extensions;

/// <summary>
/// Unit tests for the <c>validate-stripe-webhook-secret</c> publish pipeline step's validator.
/// </summary>
/// <remarks>
/// <para>
/// The validator is a pure function, so these tests call it directly rather than booting the
/// AppHost — they need no secrets and are tagged <c>Category=Unit</c> so CI runs them.
/// </para>
/// <para>
/// They live in this project because it is the only test project that references
/// <c>WithLove.AppHost</c>. A dedicated <c>WithLove.AppHost.Tests</c> project would be a tidier
/// home if more AppHost logic ever needs covering.
/// </para>
/// </remarks>
public class StripeWebhookSecretValidationTests
{
    /// <summary>The real parameter name, so message assertions match what a deploy actually prints.</summary>
    private const string ParameterName = "stripe-webhook-secret";

    /// <summary>A well-formed value: the 6-character prefix plus 32 characters, 38 total.</summary>
    private const string WellFormedSecret = "whsec_0123456789abcdef0123456789abcdef";

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void StripeWebhookSecretValidation_IsRequiredByBothPublishAndDeployPipelines()
    {
        WithLoveApplicationExtensions.StripeWebhookSecretValidationRequiredBy.Should().BeEquivalentTo(
            ["publish-prereq", "deploy-prereq"]);
    }

    /// <summary>
    /// Distinctive marker used to prove the malformed value never reaches the exception message.
    /// </summary>
    private const string Sentinel = "S3cretSentinelValueDoNotLog";

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    // Empty.
    [InlineData(null, "the value is empty")]
    [InlineData("", "the value is empty")]
    // Whitespace — the classic result of a copy/paste or a trailing newline in .secrets.env.
    [InlineData("   ", "leading or trailing whitespace")]
    [InlineData(" whsec_abc123 ", "leading or trailing whitespace")]
    [InlineData("whsec_abc123\n", "leading or trailing whitespace")]
    [InlineData("\twhsec_abc123", "leading or trailing whitespace")]
    // Straight quotes — a shell or .env file that kept its quoting.
    [InlineData("\"whsec_abc123\"", "wrapped in quote characters")]
    [InlineData("'whsec_abc123'", "wrapped in quote characters")]
    [InlineData("`whsec_abc123`", "wrapped in quote characters")]
    // Smart quotes: the real-world defect this validator exists for. A value wrapped this way was
    // found in the local secret store. Note the expected reason — a smart-quote-wrapped value also
    // fails the prefix check, but "wrapped in quotes" is the message that says what to fix, so the
    // quote check must stay ahead of the prefix check.
    [InlineData("“whsec_abc123”", "wrapped in quote characters")]
    [InlineData("‘whsec_abc123’", "wrapped in quote characters")]
    // Only one end wrapped still counts — a stray smart quote is just as fatal to signature checks.
    [InlineData("“whsec_abc123", "wrapped in quote characters")]
    [InlineData("whsec_abc123”", "wrapped in quote characters")]
    // Wrong prefix — an API key pasted into the webhook-secret slot.
    [InlineData("sk_test_abc123", "does not start with 'whsec_'")]
    [InlineData("whsec-abc123", "does not start with 'whsec_'")]
    [InlineData("WHSEC_abc123", "does not start with 'whsec_'")]
    // The bare prefix with nothing after it.
    [InlineData("whsec_", "only the 'whsec_' prefix")]
    public void ValidateStripeWebhookSecret_WithMalformedValue_ThrowsWithReason(
        string? value,
        string expectedReason)
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(ParameterName, value);

        // Assert
        act.Should().Throw<DistributedApplicationException>()
            .WithMessage($"*{expectedReason}*");
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    [InlineData(WellFormedSecret)]
    [InlineData("whsec_aBcD1234")]
    [InlineData("whsec_x")]
    public void ValidateStripeWebhookSecret_WithWellFormedValue_DoesNotThrow(string value)
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(ParameterName, value);

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// <c>whsec_placeholder</c> is the temporary bootstrap value that <c>just deploy</c> seeds
    /// for an Azure ACA deploy, because the Stripe event destination cannot be created until the
    /// shopSite endpoint exists. Rejecting it would break the automated first deploy.
    /// </summary>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void ValidateStripeWebhookSecret_WithDocumentedBootstrapPlaceholder_DoesNotThrow()
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(
            ParameterName,
            "whsec_placeholder");

        // Assert
        act.Should().NotThrow(
            "just deploy seeds whsec_placeholder until it can create the Stripe Event Destination");
    }

    /// <summary>
    /// Publish output lands in CI logs and issue reports, so the message reports only a length.
    /// </summary>
    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    [InlineData("sk_live_" + Sentinel)]
    [InlineData(" whsec_" + Sentinel + " ")]
    [InlineData("\"whsec_" + Sentinel + "\"")]
    [InlineData("“whsec_" + Sentinel + "”")]
    public void ValidateStripeWebhookSecret_WithMalformedValue_NeverEchoesTheValue(string value)
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(ParameterName, value);

        // Assert
        var message = act.Should().Throw<DistributedApplicationException>().Which.Message;

        message.Should().NotContain(Sentinel, "the rejected secret must never reach a CI log");
        message.Should().NotContain(value);
        message.Should().Contain(
            $"Observed length: {value.Length} characters",
            "a length is the most the message may disclose about the value");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void ValidateStripeWebhookSecret_WithNullValue_ReportsZeroLength()
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(ParameterName, null);

        // Assert
        act.Should().Throw<DistributedApplicationException>()
            .WithMessage("*Observed length: 0 characters*");
    }

    /// <summary>
    /// The remediation text is load-bearing: the repository recipe owns the bootstrap value, while
    /// direct deployment reads <c>.secrets.env</c> rather than the <c>aspire secret set</c>
    /// user-secret store. Pointing an operator at the wrong recovery action leaves the app unable
    /// to verify Stripe signatures.
    /// </summary>
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void ValidateStripeWebhookSecret_WhenRejecting_NamesParameterAndPointsAtSecretsEnv()
    {
        // Act
        var act = () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret(ParameterName, "sk_test_abc123");

        // Assert
        var message = act.Should().Throw<DistributedApplicationException>().Which.Message;

        message.Should().Contain($"Parameter '{ParameterName}'", "the operator must know which parameter failed");
        message.Should().Contain("Fix it in .secrets.env");
        message.Should().Contain("Parameters__stripe_webhook_secret=", "that is the exact line to edit");
        message.Should().Contain("remove a manually supplied value");
        message.Should().Contain("whsec_", "the message must state the expected shape");
    }
}
