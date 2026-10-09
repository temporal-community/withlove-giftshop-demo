using System.Security.Cryptography;
using System.Text;

namespace WithLove.StripeWebhooks;

/// <summary>
/// How this tool recognises the endpoint it owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity is metadata, never URL.</b> Stripe accepts duplicate URLs — two endpoints at the
/// identical URL get distinct ids and distinct signing secrets — so a URL match proves nothing
/// about ownership. Worse, the URL is not stable: the Container Apps environment domain suffix is
/// generated at provisioning time, so a destroy/redeploy cycle changes the FQDN. URL matching would
/// therefore find no match after every teardown, create a fresh endpoint, and orphan the previous
/// one, until the 16-endpoint account cap is reached with no way to tell which orphan belonged to
/// which dead deployment.
/// </para>
/// <para>
/// <b>Identity is also never the description.</b> The description is free text a human can edit in
/// the Dashboard, and this tool is allowed to delete what it matches.
/// </para>
/// <para>
/// The identity key is the pair (<see cref="ManagedKey"/> = <see cref="ManagedValue"/>,
/// <see cref="EnvironmentKey"/> = the <c>--tag</c> argument). The tag is what lets, say,
/// <c>azureprod</c> and <c>staging</c> coexist on one Stripe account. Subscription and resource
/// group are stamped when the ambient environment supplies them, but are deliberately
/// <i>not</i> matched on — see <see cref="BuildMetadata"/>.
/// </para>
/// </remarks>
internal sealed class WebhookIdentity
{
    /// <summary>The only key teardown is permitted to act on.</summary>
    public const string ManagedKey = "withlove_managed";

    /// <summary>The only value of <see cref="ManagedKey"/> this tool treats as ours.</summary>
    public const string ManagedValue = "true";

    /// <summary>Separates one deployment environment from another on a shared Stripe account.</summary>
    public const string EnvironmentKey = "withlove_environment";

    /// <summary>Truncated SHA-256 of the signing secret; see <see cref="SecretFingerprint"/>.</summary>
    public const string FingerprintKey = "withlove_secret_fingerprint";

    /// <summary>Informational only. Not part of the match.</summary>
    public const string SubscriptionKey = "withlove_subscription";

    /// <summary>Informational only. Not part of the match.</summary>
    public const string ResourceGroupKey = "withlove_resource_group";

    public WebhookIdentity(string tag, string? subscription = null, string? resourceGroup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        Tag = tag;
        Subscription = string.IsNullOrWhiteSpace(subscription) ? null : subscription;
        ResourceGroup = string.IsNullOrWhiteSpace(resourceGroup) ? null : resourceGroup;
    }

    public string Tag { get; }

    public string? Subscription { get; }

    public string? ResourceGroup { get; }

    /// <summary>
    /// Human-facing label shown in the Stripe Dashboard. Reconciled on every run so an edited one
    /// is restored, but never matched on.
    /// </summary>
    public string Description => $"WithLove {Tag} — managed by `just deploy` (do not edit)";

    /// <summary>
    /// True when <paramref name="metadata"/> carries both halves of the identity key. A missing
    /// metadata map, a missing key, or a differing value all mean "not ours", which is the safe
    /// answer: the tool then creates its own endpoint rather than adopting — and later deleting —
    /// an endpoint that belongs to somebody else's integration.
    /// </summary>
    public bool Matches(IDictionary<string, string>? metadata)
        => metadata is not null
           && metadata.TryGetValue(ManagedKey, out var managed)
           && string.Equals(managed, ManagedValue, StringComparison.Ordinal)
           && metadata.TryGetValue(EnvironmentKey, out var environment)
           && string.Equals(environment, Tag, StringComparison.Ordinal);

    /// <summary>
    /// Builds the metadata map to send to Stripe, merged over <paramref name="existing"/>.
    /// </summary>
    /// <remarks>
    /// The merge is not a nicety. A Stripe metadata update <i>replaces</i> the whole map, and one of
    /// the keys in it — <see cref="FingerprintKey"/> — cannot be recomputed, because it is derived
    /// from a signing secret that is readable exactly once. A reconcile that rebuilt the map from
    /// scratch would silently destroy the only drift signal the design has. Unknown keys are
    /// preserved for the same reason: something else may have put them there.
    /// </remarks>
    /// <param name="existing">Metadata currently on the endpoint, or <see langword="null"/> on create.</param>
    /// <param name="fingerprint">Fingerprint to stamp, or <see langword="null"/> to keep whatever is there.</param>
    public Dictionary<string, string> BuildMetadata(
        IDictionary<string, string>? existing,
        string? fingerprint = null)
    {
        var metadata = existing is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(existing, StringComparer.Ordinal);

        metadata[ManagedKey] = ManagedValue;
        metadata[EnvironmentKey] = Tag;

        // Only write these when we actually know them. Overwriting a previously-stamped
        // subscription with "" because this invocation happened not to have .secrets.env sourced
        // would throw away information for no gain.
        if (Subscription is not null)
            metadata[SubscriptionKey] = Subscription;

        if (ResourceGroup is not null)
            metadata[ResourceGroupKey] = ResourceGroup;

        if (fingerprint is not null)
            metadata[FingerprintKey] = fingerprint;

        return metadata;
    }
}

/// <summary>
/// Truncated hash of a signing secret, stored in endpoint metadata at create time.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a signing secret is observable exactly once — in the body of the create
/// response. Neither <c>GET /v1/webhook_endpoints/{id}</c> nor the v2 retrieve will return it, and
/// there is no rotate API in either version. So without a fingerprint there is no way at all to
/// answer "is the secret we hold the one Stripe is signing with?", and drift is detectable only by
/// noticing failed deliveries days later.
/// </para>
/// <para>
/// <b>This is not a secret and must not be "tidied away" as one.</b> It is 48 bits of a SHA-256
/// digest of a high-entropy value, exposed only to someone who already has read access to the
/// Stripe account's metadata. It is not invertible and it does not shorten any attack on the secret
/// itself.
/// </para>
/// </remarks>
internal static class SecretFingerprint
{
    /// <summary>Hex characters retained. 12 hex chars = 48 bits, ample to distinguish two secrets.</summary>
    private const int Length = 12;

    public static string Compute(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(digest)[..Length];
    }
}
