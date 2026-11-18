using Stripe;
using WithLove.StripeWebhooks;

// Keep signing secrets and API keys out of deploy and CI logs.

if (!CommandLineParser.TryParse(args, out var command, out var parseError))
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineParser.Usage);
    return ExitCode.UsageOrConfigurationError;
}

// Validate the API key before changing Stripe state.
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

// Subscription and resource group are informational metadata; the tag identifies the endpoint.
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
