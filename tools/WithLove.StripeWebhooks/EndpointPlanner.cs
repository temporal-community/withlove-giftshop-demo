using Stripe;

namespace WithLove.StripeWebhooks;

/// <summary>What the tool has decided to do, before it does any of it.</summary>
internal enum PlanKind
{
    /// <summary>No endpoint carries our identity. Create one and capture its signing secret.</summary>
    Create,

    /// <summary>Ours exists but some field drifted. Update it in place; no secret work.</summary>
    Reconcile,

    /// <summary>Ours exists and every reconciled field already matches. Make no API call at all.</summary>
    AlreadyCurrent,

    /// <summary>Ours does not exist, and the command did not ask to create it.</summary>
    Absent,

    /// <summary>More than one endpoint claims our identity. Refuse to guess.</summary>
    Duplicates,

    /// <summary>The account is at the endpoint cap and we would need to create. Refuse.</summary>
    CapReached,
}

/// <summary>The decision, plus everything needed to explain or execute it.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Target">Our endpoint, when exactly one was found.</param>
/// <param name="Matches">Every endpoint carrying our identity. More than one is a <see cref="PlanKind.Duplicates"/>.</param>
/// <param name="TotalEndpoints">Endpoints on the account, for the cap message.</param>
/// <param name="Changes">Human-readable list of the fields that drifted, empty when none did.</param>
internal sealed record EndpointPlan(
    PlanKind Kind,
    WebhookEndpoint? Target,
    IReadOnlyList<WebhookEndpoint> Matches,
    int TotalEndpoints,
    IReadOnlyList<string> Changes);

/// <summary>
/// Pure decision logic over a materialised endpoint list. Deliberately free of I/O: this is the
/// branchy part — match, cap, duplicate, create-vs-update — and deploy-time-only code is code
/// nobody exercises until it matters.
/// </summary>
internal static class EndpointPlanner
{
    /// <summary>
    /// Stripe's per-account webhook endpoint limit. Hitting it is not a Stripe error we can retry
    /// past; it means orphans have accumulated and a human must choose what to remove.
    /// </summary>
    public const int AccountEndpointCap = 16;

    /// <summary>The events this application actually handles. Kept in step with docs/azure-deployment.md Step 4.</summary>
    public static readonly IReadOnlyList<string> EnabledEvents =
    [
        "checkout.session.completed",
        "checkout.session.expired",
    ];

    public static EndpointPlan PlanEnsure(
        IReadOnlyList<WebhookEndpoint> allEndpoints,
        WebhookIdentity identity,
        string url)
    {
        ArgumentNullException.ThrowIfNull(allEndpoints);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var matches = allEndpoints.Where(endpoint => identity.Matches(endpoint.Metadata)).ToList();

        if (matches.Count > 1)
            return new EndpointPlan(PlanKind.Duplicates, null, matches, allEndpoints.Count, []);

        if (matches.Count == 0)
        {
            return allEndpoints.Count >= AccountEndpointCap
                ? new EndpointPlan(PlanKind.CapReached, null, matches, allEndpoints.Count, [])
                : new EndpointPlan(PlanKind.Create, null, matches, allEndpoints.Count, []);
        }

        var target = matches[0];
        var changes = DescribeDrift(target, identity, url);

        return new EndpointPlan(
            changes.Count == 0 ? PlanKind.AlreadyCurrent : PlanKind.Reconcile,
            target,
            matches,
            allEndpoints.Count,
            changes);
    }

    public static EndpointPlan PlanRemove(
        IReadOnlyList<WebhookEndpoint> allEndpoints,
        WebhookIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(allEndpoints);
        ArgumentNullException.ThrowIfNull(identity);

        var matches = allEndpoints.Where(endpoint => identity.Matches(endpoint.Metadata)).ToList();

        // Unlike ensure, duplicates are not an obstacle to removal: everything carrying our identity
        // is ours, and teardown wants all of it gone. Refusing here would leave orphans behind
        // precisely when the operator is trying to clean up.
        return matches.Count == 0
            ? new EndpointPlan(PlanKind.Absent, null, matches, allEndpoints.Count, [])
            : new EndpointPlan(PlanKind.Reconcile, matches[0], matches, allEndpoints.Count, []);
    }

    /// <summary>
    /// Lists the reconciled fields that differ from what we want. An empty result means no update
    /// call is made at all — steady-state deploys should be read-only against Stripe.
    /// </summary>
    private static List<string> DescribeDrift(WebhookEndpoint endpoint, WebhookIdentity identity, string url)
    {
        var changes = new List<string>();

        if (!string.Equals(endpoint.Url, url, StringComparison.Ordinal))
            changes.Add($"url ({endpoint.Url} -> {url})");

        var current = endpoint.EnabledEvents ?? [];
        if (!current.OrderBy(e => e, StringComparer.Ordinal)
                    .SequenceEqual(EnabledEvents.OrderBy(e => e, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            changes.Add($"enabled_events ({string.Join(',', current)} -> {string.Join(',', EnabledEvents)})");
        }

        if (!string.Equals(endpoint.Description, identity.Description, StringComparison.Ordinal))
            changes.Add("description");

        // Metadata is compared key by key against what BuildMetadata would produce, so a key we do
        // not manage (or a fingerprint we cannot recompute) never counts as drift and never
        // triggers a pointless update.
        var desired = identity.BuildMetadata(endpoint.Metadata);
        if (desired.Any(pair => !endpoint.Metadata!.TryGetValue(pair.Key, out var value)
                                || !string.Equals(value, pair.Value, StringComparison.Ordinal)))
        {
            changes.Add("metadata");
        }

        // A disabled endpoint delivers nothing. Re-enabling is part of "make reality match intent",
        // and it makes a deploy self-healing after somebody disables one in the Dashboard to stop a
        // delivery storm and forgets to turn it back on.
        if (string.Equals(endpoint.Status, "disabled", StringComparison.Ordinal))
            changes.Add("status (disabled -> enabled)");

        return changes;
    }

    /// <summary>True when the plan requires the endpoint to be re-enabled as part of the update.</summary>
    public static bool RequiresEnable(WebhookEndpoint endpoint)
        => string.Equals(endpoint.Status, "disabled", StringComparison.Ordinal);
}
