using WithLove.AppHost.Extensions;

namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// The Stripe webhook signing secret is shape-checked in two places by two different processes:
/// <see cref="WebhookSecretShape.Validate"/> runs inside this tool at deploy time, and
/// <c>WithLoveApplicationExtensions.ValidateStripeWebhookSecret</c> runs as the AppHost's
/// <c>validate-stripe-webhook-secret</c> pipeline step before any Bicep is written.
/// </summary>
/// <remarks>
/// Neither can call the other — the tool must not depend on the AppHost, and the AppHost must not
/// depend on a tool. So the rule is duplicated, and today the two case tables agree only because
/// they were written to agree. That is the same shape as the loyalty thresholds that were once
/// hardcoded in three places with nothing cross-checking them.
/// <para>
/// These tests are the cross-check. They do not assert that either validator is <i>correct</i> —
/// each project already covers that. They assert the two never diverge, so a value accepted by the
/// deploy pipeline cannot be rejected by the tool that consumes it, or the reverse. A divergence
/// would surface as a deploy that passes validation and then fails in a different process for a
/// reason the operator was just told was fine.
/// </para>
/// </remarks>
public class ValidatorAgreementTests
{
    /// <summary>
    /// Values spanning every branch of both reason tables, plus the bootstrap placeholder, which is
    /// load-bearing: <c>just deploy</c> seeds <c>whsec_placeholder</c> when the parameter is unset,
    /// so both validators must accept it or a first deploy cannot start.
    /// </summary>
    public static TheoryData<string?> Cases() =>
    [
        null,
        "",
        "   ",
        " whsec_abc123 ",
        "whsec_abc123 ",
        "\twhsec_abc123",
        "\"whsec_abc123\"",
        "'whsec_abc123'",
        "`whsec_abc123`",
        "“whsec_abc123”",
        "‘whsec_abc123’",
        "“whsec_abc123",
        "whsec_abc123”",
        "sk_test_abc123",
        "whsec-abc123",
        "WHSEC_abc123",
        "whsec_",
        "whsec_placeholder",
        "whsec_0123456789abcdef0123456789abcdef",
        "whsec_a",
    ];

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [MemberData(nameof(Cases))]
    public void BothValidators_AgreeOnAcceptance(string? value)
    {
        var toolRejected = WebhookSecretShape.Validate(value) is not null;

        var appHostRejected = false;
        try
        {
            WithLoveApplicationExtensions.ValidateStripeWebhookSecret("stripe-webhook-secret", value);
        }
        catch (Exception)
        {
            appHostRejected = true;
        }

        appHostRejected.Should().Be(
            toolRejected,
            "the tool and the AppHost pipeline step must classify every value identically; a value "
            + "one accepts and the other rejects means a deploy passes validation and then fails "
            + "elsewhere for a reason the operator was just told was acceptable");
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [MemberData(nameof(Cases))]
    public void BothValidators_AgreeOnReason(string? value)
    {
        var toolReason = WebhookSecretShape.Validate(value);
        if (toolReason is null)
            return;

        var appHostMessage = Record.Exception(
            () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret("stripe-webhook-secret", value))?.Message;

        // Not string equality: the AppHost wraps the reason in operator-facing remediation text and
        // the tool does not. The shared substring is the diagnosis itself, which is what must match —
        // an operator who reads one message and then the other must not be told two different things.
        appHostMessage.Should().NotBeNull();
        appHostMessage.Should().Contain(
            toolReason,
            "both validators must give the same diagnosis for the same value, even though only the "
            + "AppHost adds remediation guidance around it");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void BothValidators_AcceptTheBootstrapPlaceholder()
    {
        // `just deploy` writes whsec_placeholder when the parameter is unset, because Key Vault
        // secrets are created during provisioning while the shopSite FQDN only exists afterwards.
        // If either validator rejected it, a first deploy could never run.
        WebhookSecretShape.Validate("whsec_placeholder").Should().BeNull();
        Record.Exception(
            () => WithLoveApplicationExtensions.ValidateStripeWebhookSecret("stripe-webhook-secret", "whsec_placeholder"))
            .Should().BeNull();
    }
}
