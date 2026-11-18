using System.Security.Cryptography;
using System.Text;

namespace WithLove.StripeWebhooks;

/// <summary>
/// How this tool recognises the endpoint it owns.
/// </summary>
/// <remarks>
/// Ownership uses the managed and environment metadata pair. URLs can change or be shared, and
/// descriptions can be edited, so neither identifies an endpoint. Subscription and resource group
/// metadata are informational only.
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
    /// Match only endpoints carrying both ownership metadata values.
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
    /// Stripe metadata updates replace the map. Preserve unknown keys and the fingerprint, which
    /// cannot be recomputed after the one-time signing secret has been returned.
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

        // Keep existing informational metadata when these values are unavailable.
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
/// Stripe returns the secret only at creation. The 48-bit digest allows later drift checks without
/// storing the secret in endpoint metadata.
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
