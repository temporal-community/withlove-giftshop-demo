namespace WithLove.StripeWebhooks;

/// <summary>
/// Shape check for a Stripe webhook signing secret, applied to the value this tool is about to
/// write to <c>--secret-out</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is deliberately a second implementation of
/// <c>WithLoveApplicationExtensions.ValidateStripeWebhookSecret</c>, and it is not redundant.</b>
/// That one is an AppHost pipeline step guarding a parameter on the publish graph; this one guards
/// a file write in a standalone tool that must not take a dependency on the AppHost. The two must
/// agree case for case — <c>WebhookSecretShapeTests</c> holds the shared table, and its twin lives
/// in <c>tests/WithLove.ProductsAPI.Tests/Unit/AppHost/StripeWebhookSecretValidationTests.cs</c>. A
/// divergence between two shape checks for one value is a defect that surfaces years later.
/// </para>
/// <para>
/// <b>It is a shape gate, not a semantic gate, and it cannot be made into one.</b>
/// <c>whsec_placeholder</c> passes every check here by design, and so does a real secret belonging
/// to an endpoint that was deleted last month. Knowing whether the value is the one Stripe is
/// actually signing with is what <see cref="SecretFingerprint"/> is for. The two are complementary;
/// neither subsumes the other.
/// </para>
/// </remarks>
internal static class WebhookSecretShape
{
    public const string Prefix = "whsec_";

    /// <summary>
    /// Returns a human-readable reason the value is not shaped like a signing secret, or
    /// <see langword="null"/> when it is fine. The value itself is never part of the reason —
    /// these strings reach deploy logs.
    /// </summary>
    public static string? Validate(string? value)
        // Ordered most-specific first: a smart-quote-wrapped value also fails the prefix check, but
        // "wrapped in quotes" is the message that tells someone what to fix.
        => value switch
        {
            null or "" => "the value is empty",
            _ when value.Trim() != value => "the value has leading or trailing whitespace",
            _ when IsQuoteWrapped(value) => "the value is wrapped in quote characters (straight or smart quotes)",
            _ when !value.StartsWith(Prefix, StringComparison.Ordinal) => $"the value does not start with '{Prefix}'",
            _ when value.Length == Prefix.Length => $"the value is only the '{Prefix}' prefix",
            _ => null,
        };

    private static bool IsQuoteWrapped(string value)
        // U+2018/U+2019/U+201C/U+201D are the smart quotes editors and chat clients substitute for
        // straight quotes; a pasted secret carrying them is longer than the real secret and will
        // never verify.
        => value.Length > 0 && (IsQuote(value[0]) || IsQuote(value[^1]));

    private static bool IsQuote(char candidate)
        => candidate is '"' or '\'' or '`' or '‘' or '’' or '“' or '”';
}
