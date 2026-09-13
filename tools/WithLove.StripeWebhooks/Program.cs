using Stripe;
using WithLove.StripeWebhooks;

// Entry point. Everything below is preflight, dispatch and printing: the decisions live in
// EndpointPlanner (pure) and StripeWebhookProvisioner (Stripe-facing), both unit-tested offline.
//
// Two rules govern every line of output here:
//   1. A signing secret goes to --secret-out and nowhere else. stdout and stderr land in deploy
//      logs and CI logs.
//   2. The Stripe API key is never echoed, never in an error message, never in a usage string.

if (!CommandLineParser.TryParse(args, out var command, out var parseError))
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineParser.Usage);
    return ExitCode.UsageOrConfigurationError;
}

// Preflight before any Stripe call. The overwhelmingly likely failure is a missing key, and
// discovering that *after* mutating Stripe state is the difference between "re-run it" and "go
// delete an endpoint by hand".
var apiKey = Environment.GetEnvironmentVariable("Parameters__stripe_api_key")
             ?? Environment.GetEnvironmentVariable("STRIPE_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine(
        "No Stripe secret key. Set Parameters__stripe_api_key (as .secrets.env does) or "
        + "STRIPE_API_KEY in the environment. The key is never accepted as a command-line argument.");
    return ExitCode.UsageOrConfigurationError;
}

if (apiKey.Trim() != apiKey)
{
    // A key that round-trips through a shell with whitespace attached fails deep inside the SDK
    // with a message that does not mention whitespace. The value is deliberately not echoed.
    Console.Error.WriteLine(
        "The Stripe secret key has leading or trailing whitespace. Check the quoting of "
        + "Parameters__stripe_api_key in .secrets.env. The value itself is not shown.");
    return ExitCode.UsageOrConfigurationError;
}

IStripeClient stripeClient;
try
{
    stripeClient = new StripeClient(apiKey);
}
catch (Exception exception) when (exception is ArgumentException or StripeException)
{
    Console.Error.WriteLine(
        "The Stripe secret key was rejected by the SDK as malformed. Expected a key beginning with "
        + "'sk_' or 'rk_'. The value itself is not shown.");
    return ExitCode.UsageOrConfigurationError;
}

// Subscription and resource group are stamped into metadata when the ambient environment supplies
// them, purely so a human reading the Stripe Dashboard can tell which deployment an endpoint
// belongs to. They are NOT part of the identity match -- the CLI contract identifies an endpoint by
// --tag alone, so a tool invoked without .secrets.env sourced must still find the endpoint it owns.
var identity = new WebhookIdentity(
    command.Tag,
    Environment.GetEnvironmentVariable("Azure__SubscriptionId"),
    Environment.GetEnvironmentVariable("Azure__ResourceGroup"));

var provisioner = new StripeWebhookProvisioner(stripeClient);

ProvisionResult result;
try
{
    result = await provisioner.RunAsync(command, identity).ConfigureAwait(false);
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"Could not reach the Stripe API: {exception.Message}");
    return ExitCode.StripeApiError;
}

foreach (var line in result.Errors)
    Console.Error.WriteLine(line);

if (result.Status is not null)
    Console.WriteLine(result.Status);

return result.ExitCode;
