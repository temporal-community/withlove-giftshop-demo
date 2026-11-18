namespace WithLove.StripeWebhooks;

/// <summary>
/// Shape check for a Stripe webhook signing secret, applied to the value this tool is about to
/// write to <c>--secret-out</c>.
/// </summary>
/// <remarks>
/// This standalone check must agree with the AppHost's publish-time validator. It checks shape,
/// not whether Stripe uses the value: even <c>whsec_placeholder</c> passes. Fingerprint checks
/// provide a separate drift signal.
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
