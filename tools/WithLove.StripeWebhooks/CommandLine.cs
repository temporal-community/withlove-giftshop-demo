using System.Diagnostics.CodeAnalysis;

namespace WithLove.StripeWebhooks;

/// <summary>The verb the operator asked for.</summary>
internal enum CommandVerb
{
    /// <summary>Create the endpoint if ours is absent; otherwise reconcile it in place.</summary>
    Ensure,

    /// <summary>Delete our endpoint. Safe to re-run when there is nothing to delete.</summary>
    Remove,

    /// <summary>Delete ours if present, then create a fresh one. Mints a new signing secret.</summary>
    Recreate,
}

/// <summary>A fully validated command line. Construction is only possible through the parser.</summary>
internal sealed record ToolCommand(
    CommandVerb Verb,
    string Tag,
    string? Url,
    string? SecretOut,
    bool DryRun);

/// <summary>
/// Argument parsing, kept pure so the whole surface is unit-testable without a process launch.
/// </summary>
internal static class CommandLineParser
{
    public const string Usage = """
        Usage:
          ensure    --url <https://host/stripe/webhook> --tag <environment> --secret-out <path> [--dry-run]
          remove    --tag <environment> [--dry-run]
          recreate  --url <https://host/stripe/webhook> --tag <environment> --secret-out <path> [--dry-run]

        The Stripe secret key is read from the Parameters__stripe_api_key environment variable
        (STRIPE_API_KEY is accepted as a fallback). It is never accepted as an argument, because
        argv is world-readable on Linux CI agents.

        Signing secrets are written only to --secret-out, as a single line, mode 0600. Nothing
        secret is ever written to stdout or stderr.

        Exit codes: 0 nothing to do / removed · 10 created, secret written to --secret-out ·
                    11 already existed, reconciled, no secret written · 1 usage or config error ·
                    2 blocked (endpoint cap or duplicate identity) · 3 created but secret NOT
                    persisted · 4 Stripe API error.
        """;

    /// <summary>
    /// The tag becomes a Stripe metadata value and the sole identity key, so it is restricted to
    /// characters that survive shells, URLs and the Stripe Dashboard's own display unharmed. A tag
    /// that round-trips differently than it was typed is a tag that silently fails to match, which
    /// would create a second endpoint rather than reconcile the first.
    /// </summary>
    private const int MaxTagLength = 64;

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ToolCommand? command,
        [NotNullWhen(false)] out string? error)
    {
        command = null;
        error = null;

        if (args.Count == 0)
        {
            error = "No command given.";
            return false;
        }

        CommandVerb verb;
        switch (args[0])
        {
            case "ensure":
                verb = CommandVerb.Ensure;
                break;
            case "remove":
                verb = CommandVerb.Remove;
                break;
            case "recreate":
                verb = CommandVerb.Recreate;
                break;
            default:
                error = $"Unknown command '{args[0]}'. Expected ensure, remove or recreate.";
                return false;
        }

        string? url = null;
        string? tag = null;
        string? secretOut = null;
        var dryRun = false;

        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i];
            switch (name)
            {
                case "--dry-run":
                    if (dryRun)
                    {
                        error = "--dry-run was given more than once.";
                        return false;
                    }

                    dryRun = true;
                    continue;

                case "--url":
                case "--tag":
                case "--secret-out":
                    break;

                default:
                    error = $"Unknown option '{name}'.";
                    return false;
            }

            if (i + 1 >= args.Count)
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }

            var value = args[++i];

            // A value that looks like a flag is nearly always a missing argument rather than a
            // deliberate one, and swallowing it would silently run against the wrong endpoint.
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{name}' needs a value, but was followed by '{value}'.";
                return false;
            }

            var alreadySet = name switch
            {
                "--url" => url is not null,
                "--tag" => tag is not null,
                _ => secretOut is not null,
            };

            if (alreadySet)
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }

            if (name == "--url")
                url = value;
            else if (name == "--tag")
                tag = value;
            else
                secretOut = value;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            error = "--tag is required. It is the identity of this deployment's endpoint, so two "
                  + "environments can share one Stripe account without colliding.";
            return false;
        }

        if (tag.Length > MaxTagLength)
        {
            error = $"--tag is longer than {MaxTagLength} characters.";
            return false;
        }

        if (!tag.All(IsTagCharacter))
        {
            error = $"--tag '{tag}' contains characters outside [A-Za-z0-9._-].";
            return false;
        }

        if (verb is CommandVerb.Ensure or CommandVerb.Recreate)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                error = $"--url is required for '{args[0]}'.";
                return false;
            }

            if (!TryValidateUrl(url, out var urlError))
            {
                error = urlError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(secretOut))
            {
                error = $"--secret-out is required for '{args[0]}'. The signing secret is readable "
                      + "exactly once, in the create response, so there must be somewhere to put it.";
                return false;
            }
        }
        else
        {
            if (url is not null)
            {
                error = "--url is not valid for 'remove'; the endpoint is found by its metadata tag.";
                return false;
            }

            if (secretOut is not null)
            {
                error = "--secret-out is not valid for 'remove'; no secret is produced.";
                return false;
            }
        }

        command = new ToolCommand(verb, tag, url, secretOut, dryRun);
        return true;
    }

    private static bool IsTagCharacter(char candidate)
        => char.IsAsciiLetterOrDigit(candidate) || candidate is '.' or '_' or '-';

    private static bool TryValidateUrl(string url, [NotNullWhen(false)] out string? error)
    {
        // One message for both failures on purpose. A bare path like "/stripe/webhook" parses as an
        // absolute *file* URI on Unix, so splitting this into "not absolute" and "not https" would
        // report "must use https" about a value whose real problem is that it has no host.
        //
        // Stripe will not deliver to plain http in live mode either, and the URL this tool is
        // handed comes from `az containerapp show`, which always yields https. Rejecting anything
        // else here turns a silently-undeliverable endpoint into an argument error.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = $"--url '{url}' must be an absolute https URL, "
                  + "for example https://shopsite.<suffix>.azurecontainerapps.io/stripe/webhook.";
            return false;
        }

        error = null;
        return true;
    }
}
