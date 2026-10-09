using Stripe;

namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// The planner is the branchy part of this tool and it runs only at deploy time, which is exactly
/// the code nobody exercises until it matters.
/// </summary>
public class EndpointPlannerTests
{
    private const string Url = "https://shopsite.example.azurecontainerapps.io/stripe/webhook";
    private const string OldUrl = "https://shopsite.oldsuffix.azurecontainerapps.io/stripe/webhook";

    private static readonly WebhookIdentity Identity = new("azureprod");

    private static WebhookEndpoint Ours(
        string id = "we_ours",
        string url = Url,
        string? description = null,
        IEnumerable<string>? events = null,
        string status = "enabled",
        string tag = "azureprod",
        IDictionary<string, string>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WebhookIdentity.ManagedKey] = WebhookIdentity.ManagedValue,
            [WebhookIdentity.EnvironmentKey] = tag,
        };

        if (extraMetadata is not null)
        {
            foreach (var pair in extraMetadata)
                metadata[pair.Key] = pair.Value;
        }

        return new WebhookEndpoint
        {
            Id = id,
            Url = url,
            Description = description ?? Identity.Description,
            EnabledEvents = [.. events ?? EndpointPlanner.EnabledEvents],
            Metadata = metadata,
            Status = status,
        };
    }

    private static WebhookEndpoint Theirs(string id = "we_theirs", string url = Url) => new()
    {
        Id = id,
        Url = url,
        Description = "somebody else's integration",
        EnabledEvents = ["invoice.paid"],
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal),
        Status = "enabled",
    };

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Create_WhenTheAccountHasNothingOfOurs()
    {
        var plan = EndpointPlanner.PlanEnsure([], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Create);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Create_EvenWhenAnUnmanagedEndpointSitsAtTheIdenticalUrl()
    {
        // Stripe accepts duplicate URLs -- two endpoints at the same URL get distinct ids and
        // distinct signing secrets -- so a URL match proves nothing about ownership. Adopting one
        // would mean reconciling, and later deleting, somebody else's integration.
        var plan = EndpointPlanner.PlanEnsure([Theirs(url: Url)], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Create);
        plan.Matches.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void AlreadyCurrent_MakesNoApiCall()
    {
        var plan = EndpointPlanner.PlanEnsure([Ours()], Identity, Url);

        plan.Kind.Should().Be(PlanKind.AlreadyCurrent);
        plan.Changes.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void AlreadyCurrent_IgnoresEventOrderingAndUnmanagedMetadata()
    {
        // Stripe does not promise to return enabled_events in the order they were sent, and the
        // fingerprint key is one we deliberately cannot recompute. Treating either as drift would
        // make every steady-state deploy issue a pointless update.
        var endpoint = Ours(
            events: ["checkout.session.expired", "checkout.session.completed"],
            extraMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [WebhookIdentity.FingerprintKey] = "3f9a1b2c4d5e",
                ["someone_elses_key"] = "value",
            });

        EndpointPlanner.PlanEnsure([endpoint], Identity, Url).Kind.Should().Be(PlanKind.AlreadyCurrent);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Reconcile_WhenTheFqdnChangedAcrossADestroyAndRedeploy()
    {
        // The Container Apps environment domain suffix is generated at provisioning time, so the
        // FQDN after a destroy/redeploy differs from the one before it. Metadata identity is what
        // survives that; the URL is what gets fixed up.
        var plan = EndpointPlanner.PlanEnsure([Ours(url: OldUrl)], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Reconcile);
        plan.Target!.Id.Should().Be("we_ours");
        plan.Changes.Should().ContainSingle().Which.Should().Contain("url");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Reconcile_WhenTheSubscribedEventsDrifted()
    {
        var plan = EndpointPlanner.PlanEnsure([Ours(events: ["checkout.session.completed"])], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Reconcile);
        plan.Changes.Should().ContainSingle().Which.Should().Contain("enabled_events");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Reconcile_WhenTheEndpointWasDisabledInTheDashboard()
    {
        // Somebody disables an endpoint to stop a delivery storm and forgets to re-enable it. A
        // deploy should heal that rather than report "already configured" about a dead endpoint.
        var plan = EndpointPlanner.PlanEnsure([Ours(status: "disabled")], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Reconcile);
        plan.Changes.Should().ContainSingle().Which.Should().Contain("disabled");
        EndpointPlanner.RequiresEnable(plan.Target!).Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Reconcile_WhenAHumanEditedTheDescription()
    {
        var plan = EndpointPlanner.PlanEnsure([Ours(description: "hand-edited")], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Reconcile);
        plan.Changes.Should().Contain("description");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Duplicates_RefuseToGuess()
    {
        // Each duplicate has a different signing secret and only one matches what is installed.
        // Picking one at random would work half the time, which is the worst possible failure rate.
        var plan = EndpointPlanner.PlanEnsure([Ours("we_a"), Ours("we_b")], Identity, Url);

        plan.Kind.Should().Be(PlanKind.Duplicates);
        plan.Matches.Select(endpoint => endpoint.Id).Should().BeEquivalentTo(["we_a", "we_b"]);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void CapReached_OnlyBlocksWhenWeWouldHaveToCreate()
    {
        var full = Enumerable.Range(0, EndpointPlanner.AccountEndpointCap)
            .Select(index => Theirs($"we_other_{index}"))
            .ToList();

        EndpointPlanner.PlanEnsure(full, Identity, Url).Kind.Should().Be(PlanKind.CapReached);

        // A full account that already contains ours needs no create, so the cap is irrelevant.
        var fullIncludingOurs = full.Skip(1).Append(Ours()).ToList();
        EndpointPlanner.PlanEnsure(fullIncludingOurs, Identity, Url).Kind.Should().Be(PlanKind.AlreadyCurrent);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void PlanRemove_SelectsOnlyOurOwnEndpoints()
    {
        var all = new List<WebhookEndpoint>
        {
            Theirs("we_theirs"),
            Ours("we_ours"),
            Ours("we_staging", tag: "staging"),
        };

        var plan = EndpointPlanner.PlanRemove(all, Identity);

        plan.Matches.Select(endpoint => endpoint.Id).Should().Equal("we_ours");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void PlanRemove_ReportsAbsentRatherThanFailing()
    {
        // `just destroy` must be re-runnable. "Nothing of ours is here" is the goal state.
        EndpointPlanner.PlanRemove([Theirs()], Identity).Kind.Should().Be(PlanKind.Absent);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void PlanRemove_TakesAllDuplicates()
    {
        // Unlike ensure, removal is not ambiguous: everything carrying our identity is ours, and
        // leaving one behind means an orphan counting against the cap on the next deploy.
        var plan = EndpointPlanner.PlanRemove([Ours("we_a"), Ours("we_b")], Identity);

        plan.Matches.Should().HaveCount(2);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void EnabledEvents_AreTheTwoThisApplicationHandles()
    {
        // Kept in step with docs/azure-deployment.md Step 4 and StripeEventHandler.
        EndpointPlanner.EnabledEvents.Should().Equal("checkout.session.completed", "checkout.session.expired");
    }
}
