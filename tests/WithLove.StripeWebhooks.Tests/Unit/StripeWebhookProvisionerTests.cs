using System.IO;
using System.Net.Http;
using Stripe;

// Stripe.File is a webhook-unrelated API resource; every File in this file means System.IO.File.
using File = System.IO.File;
using WithLove.StripeWebhooks.Tests.Fakes;

namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// End-to-end behaviour of each verb, against an in-memory Stripe account.
/// </summary>
/// <remarks>
/// Nothing here touches the network. The sandbox Stripe account is shared and is kept at zero
/// endpoints; a test that creates a real one leaves debris that the 16-endpoint cap eventually
/// turns into somebody else's failing deploy.
/// </remarks>
public class StripeWebhookProvisionerTests : IDisposable
{
    private const string Url = "https://shopsite.example.azurecontainerapps.io/stripe/webhook";
    private const string NewUrl = "https://shopsite.newsuffix.azurecontainerapps.io/stripe/webhook";
    private const string Tag = "azureprod";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"withlove-stripe-webhooks-{Guid.NewGuid():N}");

    private readonly FakeStripeAccount _stripe = new();
    private readonly WebhookIdentity _identity = new(Tag, "sub-1234", "rg-aspire-withlove");

    private string SecretOut => Path.Combine(_directory, "stripe-webhook-secret");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private Task<ProvisionResult> RunAsync(CommandVerb verb, string? url = Url, bool dryRun = false)
    {
        var provisioner = new StripeWebhookProvisioner(_stripe.CreateClient());
        var command = new ToolCommand(
            verb,
            Tag,
            verb == CommandVerb.Remove ? null : url,
            verb == CommandVerb.Remove ? null : SecretOut,
            dryRun);

        return provisioner.RunAsync(command, _identity);
    }

    private Dictionary<string, string> OurMetadata(string tag = Tag) => new(StringComparer.Ordinal)
    {
        [WebhookIdentity.ManagedKey] = WebhookIdentity.ManagedValue,
        [WebhookIdentity.EnvironmentKey] = tag,
    };

    // ─── ensure: create ───────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_OnACleanAccount_CreatesTheEndpointAndWritesTheSecret()
    {
        var result = await RunAsync(CommandVerb.Ensure);

        // Exit 10 is the caller's signal that there is a new secret to install. Exit 11 would mean
        // "keep what you have", and getting that wrong here breaks webhook signature verification.
        result.ExitCode.Should().Be(ExitCode.Created);
        result.Errors.Should().BeEmpty();

        var created = _stripe.Endpoints.Should().ContainSingle().Subject;
        created.Url.Should().Be(Url);
        created.Events.Should().BeEquivalentTo(["checkout.session.completed", "checkout.session.expired"]);
        created.Metadata[WebhookIdentity.ManagedKey].Should().Be("true");
        created.Metadata[WebhookIdentity.EnvironmentKey].Should().Be(Tag);
        created.Metadata[WebhookIdentity.SubscriptionKey].Should().Be("sub-1234");
        created.Metadata[WebhookIdentity.ResourceGroupKey].Should().Be("rg-aspire-withlove");

        File.ReadAllText(SecretOut).Trim().Should().Be(created.Secret);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_SendsAnExplicitApiVersionMatchingTheSdkPin()
    {
        // This is the whole reason the tool is .NET rather than curl and jq. Omitting api_version
        // makes Stripe stamp the endpoint with the ACCOUNT's default version, and WithLove.Web's
        // webhook handler throws on every delivery whose api_version differs from
        // StripeConfiguration.ApiVersion (ThrowOnWebhookApiVersionMismatch defaults to true). A
        // hard-coded string would rot silently at the next Stripe.net upgrade; this assertion
        // compares against the same symbol the handler does.
        await RunAsync(CommandVerb.Ensure);

        var createBody = _stripe.Bodies[0];
        createBody["api_version"].Should().Be(StripeConfiguration.ApiVersion);
        _stripe.Endpoints[0].ApiVersion.Should().Be(StripeConfiguration.ApiVersion);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_StampsADriftFingerprintOfTheSecretItJustCaptured()
    {
        await RunAsync(CommandVerb.Ensure);

        var created = _stripe.Endpoints[0];
        created.Metadata[WebhookIdentity.FingerprintKey]
            .Should().Be(SecretFingerprint.Compute(created.Secret));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_NeverPutsTheSecretInAnythingDestinedForALog()
    {
        // stdout and stderr land in deploy output and CI logs. The file is the only channel.
        var result = await RunAsync(CommandVerb.Ensure);

        var secret = _stripe.Endpoints[0].Secret;
        result.Status.Should().NotContain(secret);
        result.Status.Should().NotContain("whsec_");
        result.Errors.Should().NotContain(line => line.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_WhenTheFingerprintStampFails_StillReportsSuccess()
    {
        // The secret is already safely on disk by then. Failing the deploy over a drift-detection
        // nicety would mean the caller never installs a secret that was created perfectly well.
        _stripe.FailWhen = (method, path) => method == HttpMethod.Post && path.Contains("/we_created_", StringComparison.Ordinal);

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.Created);
        result.Errors.Should().ContainSingle().Which.Should().Contain("fingerprint");
        File.Exists(SecretOut).Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_WhenTheSecretCannotBePersisted_FailsLoudlyAndNamesTheEndpoint()
    {
        // The one genuinely dangerous state in the design: Stripe has minted a secret that exists
        // nowhere durable, and there is no API that will ever return it again. Softening this to a
        // warning would leave a deploy looking green with an unusable endpoint behind it.
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(SecretOut); // a directory where the file should go

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.CreatedButNotPersisted);
        result.Errors[0].Should().Contain(_stripe.Endpoints[0].Id);
        result.Errors.Should().Contain(line => line.Contains("recreate", StringComparison.Ordinal));
    }

    // ─── ensure: reconcile ────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_RunTwice_IsIdempotentAndMakesNoSecondMutation()
    {
        await RunAsync(CommandVerb.Ensure);
        var secretAfterFirstRun = File.ReadAllText(SecretOut);
        var mutationsAfterFirstRun = _stripe.Bodies.Count;

        var second = await RunAsync(CommandVerb.Ensure);

        // Exit 11 tells the caller to keep the secret it already has -- which is the only option,
        // since a signing secret is readable exactly once and there is no rotate API.
        second.ExitCode.Should().Be(ExitCode.Reconciled);
        second.Status.Should().Contain("already configured");
        _stripe.Endpoints.Should().ContainSingle();
        _stripe.Bodies.Should().HaveCount(mutationsAfterFirstRun, "a steady-state deploy must not write to Stripe");
        File.ReadAllText(SecretOut).Should().Be(secretAfterFirstRun);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_AfterADestroyAndRedeploy_MovesTheUrlAndKeepsTheSecret()
    {
        // The Container Apps domain suffix is regenerated per environment, so the FQDN changes
        // across a destroy/redeploy. Metadata identity survives it; matching on URL would not, and
        // would orphan the old endpoint while minting a secret nobody asked for.
        await RunAsync(CommandVerb.Ensure);
        var secretBefore = File.ReadAllText(SecretOut);
        var idBefore = _stripe.Endpoints[0].Id;

        var result = await RunAsync(CommandVerb.Ensure, url: NewUrl);

        result.ExitCode.Should().Be(ExitCode.Reconciled);
        result.Status.Should().Contain("no secret written");
        _stripe.Endpoints.Should().ContainSingle();
        _stripe.Endpoints[0].Id.Should().Be(idBefore);
        _stripe.Endpoints[0].Url.Should().Be(NewUrl);
        File.ReadAllText(SecretOut).Should().Be(secretBefore);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_Reconciling_PreservesTheFingerprintItCannotRecompute()
    {
        // A Stripe metadata update replaces the whole map, and the fingerprint is derived from a
        // secret that can never be read again. A non-merging reconcile would destroy it silently.
        await RunAsync(CommandVerb.Ensure);
        var fingerprint = _stripe.Endpoints[0].Metadata[WebhookIdentity.FingerprintKey];

        await RunAsync(CommandVerb.Ensure, url: NewUrl);

        _stripe.Endpoints[0].Metadata[WebhookIdentity.FingerprintKey].Should().Be(fingerprint);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_ReEnablesAnEndpointSomebodyDisabled()
    {
        _stripe.Seed(Url, OurMetadata(), _identity.Description, status: "disabled");

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.Reconciled);
        _stripe.Endpoints[0].Status.Should().Be("enabled");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_NeverAdoptsAnEndpointItDidNotCreate()
    {
        // Stripe accepts duplicate URLs, so an endpoint at our exact URL may still belong to
        // somebody else. Adopting it would mean reconciling it now and deleting it at teardown.
        var theirs = _stripe.Seed(Url, description: "somebody else's integration");

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.Created);
        _stripe.Endpoints.Should().HaveCount(2);
        _stripe.Endpoints.Should().Contain(endpoint => endpoint.Id == theirs.Id);
    }

    // ─── ensure: blocked ──────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_WithDuplicates_RefusesToGuessAndMutatesNothing()
    {
        _stripe.Seed(Url, OurMetadata());
        _stripe.Seed(Url, OurMetadata());

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.Blocked);
        result.Status.Should().BeNull();
        result.Errors.Should().Contain(line => line.Contains("recreate", StringComparison.Ordinal));
        _stripe.Bodies.Should().BeEmpty();
        File.Exists(SecretOut).Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_AtTheAccountCap_FailsWithoutTouchingAnyoneElsesEndpoints()
    {
        // Never auto-delete an unmanaged endpoint: the account may serve other integrations, and a
        // full account is a human decision, not a retryable error.
        for (var index = 0; index < EndpointPlanner.AccountEndpointCap; index++)
            _stripe.Seed($"https://other-{index}.example.com/hook");

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.Blocked);
        result.Errors[0].Should().Contain("16");
        _stripe.Endpoints.Should().HaveCount(EndpointPlanner.AccountEndpointCap);
        _stripe.Bodies.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Ensure_WhenStripeIsUnreachable_ReportsThatNothingChanged()
    {
        _stripe.FailWhen = (_, _) => true;

        var result = await RunAsync(CommandVerb.Ensure);

        result.ExitCode.Should().Be(ExitCode.StripeApiError);
        result.Errors.Should().Contain(line => line.Contains("No Stripe state was changed", StringComparison.Ordinal));
    }

    // ─── remove ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Remove_WithNothingToDelete_SucceedsQuietly()
    {
        // `just destroy` must be re-runnable. Erroring the second time would fail a teardown the
        // operator is probably running because something is already broken.
        var result = await RunAsync(CommandVerb.Remove);

        result.ExitCode.Should().Be(ExitCode.Success);
        result.Status.Should().Contain("nothing to delete");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Remove_DeletesOnlyOurOwnTaggedEndpoint()
    {
        var theirs = _stripe.Seed("https://other.example.com/hook");
        var staging = _stripe.Seed(Url, OurMetadata("staging"));
        var ours = _stripe.Seed(Url, OurMetadata());

        var result = await RunAsync(CommandVerb.Remove);

        result.ExitCode.Should().Be(ExitCode.Success);
        result.Status.Should().Contain(ours.Id);
        _stripe.Endpoints.Select(endpoint => endpoint.Id)
            .Should().BeEquivalentTo([theirs.Id, staging.Id]);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Remove_DeletesEveryDuplicateCarryingOurTag()
    {
        _stripe.Seed(Url, OurMetadata());
        _stripe.Seed(Url, OurMetadata());

        await RunAsync(CommandVerb.Remove);

        _stripe.Endpoints.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Remove_WhenDeletionFails_NamesTheOrphanAndTheManualCommand()
    {
        // A missed delete is an orphan counting against the cap on the next deploy. Swallowing it
        // would hide that until the cap message, which names endpoints but not the reason.
        var ours = _stripe.Seed(Url, OurMetadata());
        _stripe.FailWhen = (method, _) => method == HttpMethod.Delete;

        var result = await RunAsync(CommandVerb.Remove);

        result.ExitCode.Should().Be(ExitCode.StripeApiError);
        result.Errors[0].Should().Contain(ours.Id);
        result.Errors.Should().Contain(line => line.Contains("stripe webhook_endpoints delete", StringComparison.Ordinal));
    }

    // ─── recreate ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Recreate_ReplacesOurEndpointAndMintsANewSecret()
    {
        await RunAsync(CommandVerb.Ensure);
        var originalId = _stripe.Endpoints[0].Id;
        var originalSecret = File.ReadAllText(SecretOut);

        var result = await RunAsync(CommandVerb.Recreate);

        result.ExitCode.Should().Be(ExitCode.Created);
        result.Status.Should().Contain(originalId, "the deploy log should say what was replaced");
        _stripe.Endpoints.Should().ContainSingle().Which.Id.Should().NotBe(originalId);
        File.ReadAllText(SecretOut).Should().NotBe(originalSecret);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Recreate_ResolvesDuplicatesThatEnsureRefusesToTouch()
    {
        // recreate is the escape hatch. Collapsing several claimants into one fresh endpoint is
        // exactly the repair that ensure declines to perform on its own.
        _stripe.Seed(Url, OurMetadata());
        _stripe.Seed(Url, OurMetadata());

        var result = await RunAsync(CommandVerb.Recreate);

        result.ExitCode.Should().Be(ExitCode.Created);
        _stripe.Endpoints.Should().ContainSingle();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task Recreate_OnAnAccountWithNothingOfOurs_JustCreates()
    {
        var result = await RunAsync(CommandVerb.Recreate);

        result.ExitCode.Should().Be(ExitCode.Created);
        _stripe.Endpoints.Should().ContainSingle();
    }

    // ─── dry run ──────────────────────────────────────────────────────────────

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    // The verb travels as a string because xUnit test methods must be public, and CommandVerb is
    // internal to the tool.
    [InlineData("ensure")]
    [InlineData("recreate")]
    [InlineData("remove")]
    public async Task DryRun_MutatesNothingAndWritesNoSecret(string verbName)
    {
        var verb = verbName switch
        {
            "ensure" => CommandVerb.Ensure,
            "recreate" => CommandVerb.Recreate,
            _ => CommandVerb.Remove,
        };

        _stripe.Seed(Url, OurMetadata());

        var result = await RunAsync(verb, dryRun: true);

        result.ExitCode.Should().Be(ExitCode.Success);
        result.Status.Should().StartWith("would ");
        _stripe.Bodies.Should().BeEmpty();
        _stripe.Endpoints.Should().ContainSingle();
        File.Exists(SecretOut).Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public async Task DryRun_OnACleanAccount_NamesTheApiVersionItWouldStamp()
    {
        var result = await RunAsync(CommandVerb.Ensure, dryRun: true);

        result.Status.Should().Contain(StripeConfiguration.ApiVersion);
        result.Status.Should().Contain(Url);
    }
}
