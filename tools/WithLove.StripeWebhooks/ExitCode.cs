namespace WithLove.StripeWebhooks;

/// <summary>
/// Process exit codes. These are a published contract with the <c>justfile</c> recipes that invoke
/// this tool — <c>just deploy</c> branches on <see cref="Created"/> vs <see cref="Reconciled"/> to
/// decide whether there is a new signing secret to install. Do not renumber them, and do not add a
/// code without updating the justfile and docs/azure-deployment.md at the same time.
/// </summary>
internal static class ExitCode
{
    /// <summary>Nothing needed doing, or a non-secret-bearing command succeeded (<c>remove</c>, <c>--dry-run</c>).</summary>
    public const int Success = 0;

    /// <summary>
    /// Bad arguments, missing or malformed Stripe API key. Nothing was mutated, and nothing had been
    /// looked up yet — this is the failure class that must happen before any Stripe state changes.
    /// </summary>
    public const int UsageOrConfigurationError = 1;

    /// <summary>
    /// The account is at the endpoint cap, or more than one endpoint claims our identity. The caller
    /// must look at the printed list and act; the tool will not guess which endpoint to remove.
    /// </summary>
    public const int Blocked = 2;

    /// <summary>
    /// An endpoint was created in Stripe but its signing secret could not be persisted to
    /// <c>--secret-out</c>. This is the one genuinely dangerous state in the design: Stripe has
    /// minted a secret that exists nowhere durable. stderr names the endpoint id and the
    /// <c>recreate</c> remediation. Never soften this to a warning.
    /// </summary>
    public const int CreatedButNotPersisted = 3;

    /// <summary>A Stripe API call failed. Whether anything was mutated is stated on stderr.</summary>
    public const int StripeApiError = 4;

    /// <summary>
    /// An endpoint was created and its signing secret was written to <c>--secret-out</c>.
    /// The caller must install that secret.
    /// </summary>
    public const int Created = 10;

    /// <summary>
    /// Our endpoint already existed and was reconciled in place. No secret was written, because a
    /// signing secret is readable exactly once — in the body of the create response — and there is
    /// no rotate API in either Stripe API version. The caller must keep the secret it already has,
    /// which stays correct across an FQDN change: an in-place url update preserves the endpoint's
    /// signing secret, confirmed by direct testing against Stripe on 2026-09-14.
    /// </summary>
    public const int Reconciled = 11;
}
