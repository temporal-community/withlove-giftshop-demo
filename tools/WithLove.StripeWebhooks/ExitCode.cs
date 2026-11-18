namespace WithLove.StripeWebhooks;

/// <summary>
/// Exit codes consumed by deployment scripts. Keep callers and documentation in sync.
/// </summary>
internal static class ExitCode
{
    /// <summary>Nothing needed doing, or a non-secret-bearing command succeeded (<c>remove</c>, <c>--dry-run</c>).</summary>
    public const int Success = 0;

    /// <summary>
    /// Invalid arguments or API key; no Stripe state was changed.
    /// </summary>
    public const int UsageOrConfigurationError = 1;

    /// <summary>
    /// Endpoint cap or duplicate identity requires operator action.
    /// </summary>
    public const int Blocked = 2;

    /// <summary>
    /// An endpoint was created but its one-time signing secret could not be saved; recreate it.
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
    /// An existing endpoint was reconciled. No new secret was issued; retain the existing one.
    /// </summary>
    public const int Reconciled = 11;
}
