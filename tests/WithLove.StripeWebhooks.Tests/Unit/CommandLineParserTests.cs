namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// The CLI is a contract with the justfile recipes that call this tool, so its parsing is pinned
/// here rather than left to be discovered at deploy time. Argument mistakes must fail before any
/// Stripe call: at that point nothing has been mutated and re-running is free.
/// </summary>
public class CommandLineParserTests
{
    private const string Url = "https://shopsite.example.azurecontainerapps.io/stripe/webhook";

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Ensure_WithAllOptions_Parses()
    {
        var parsed = CommandLineParser.TryParse(
            ["ensure", "--url", Url, "--tag", "azureprod", "--secret-out", "/tmp/whsec"],
            out var command,
            out var error);

        parsed.Should().BeTrue(error);
        command!.Verb.Should().Be(CommandVerb.Ensure);
        command.Url.Should().Be(Url);
        command.Tag.Should().Be("azureprod");
        command.SecretOut.Should().Be("/tmp/whsec");
        command.DryRun.Should().BeFalse();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Remove_NeedsOnlyTag()
    {
        var parsed = CommandLineParser.TryParse(["remove", "--tag", "staging"], out var command, out var error);

        parsed.Should().BeTrue(error);
        command!.Verb.Should().Be(CommandVerb.Remove);
        command.Tag.Should().Be("staging");
        command.Url.Should().BeNull();
        command.SecretOut.Should().BeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Recreate_ParsesLikeEnsure()
    {
        var parsed = CommandLineParser.TryParse(
            ["recreate", "--tag", "azureprod", "--url", Url, "--secret-out", "/tmp/whsec", "--dry-run"],
            out var command,
            out var error);

        parsed.Should().BeTrue(error);
        command!.Verb.Should().Be(CommandVerb.Recreate);
        command.DryRun.Should().BeTrue();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    // No command at all, and a command nobody implemented.
    [InlineData(new string[0], "No command")]
    [InlineData(new[] { "provision", "--tag", "azureprod" }, "Unknown command")]
    // --tag is the identity key; without it the tool cannot know which endpoint is its own.
    [InlineData(new[] { "remove" }, "--tag is required")]
    [InlineData(new[] { "ensure", "--url", Url, "--secret-out", "/tmp/s" }, "--tag is required")]
    // ensure and recreate produce or reconcile an endpoint, so both need somewhere to point it.
    [InlineData(new[] { "ensure", "--tag", "azureprod", "--secret-out", "/tmp/s" }, "--url is required")]
    [InlineData(new[] { "ensure", "--tag", "azureprod", "--url", Url }, "--secret-out is required")]
    // remove produces no secret and finds its target by tag, so neither option is meaningful.
    [InlineData(new[] { "remove", "--tag", "azureprod", "--url", Url }, "--url is not valid")]
    [InlineData(new[] { "remove", "--tag", "azureprod", "--secret-out", "/tmp/s" }, "--secret-out is not valid")]
    // Typos must not be silently ignored -- an ignored option is a wrong deployment.
    [InlineData(new[] { "ensure", "--tags", "azureprod" }, "Unknown option")]
    [InlineData(new[] { "ensure", "--tag" }, "needs a value")]
    [InlineData(new[] { "ensure", "--tag", "--url", Url }, "needs a value")]
    [InlineData(new[] { "ensure", "--tag", "a", "--tag", "b" }, "more than once")]
    [InlineData(new[] { "ensure", "--tag", "a", "--dry-run", "--dry-run" }, "more than once")]
    public void MalformedCommandLine_IsRejectedWithAReason(string[] args, string expectedFragment)
    {
        var parsed = CommandLineParser.TryParse(args, out var command, out var error);

        parsed.Should().BeFalse();
        command.Should().BeNull();
        error.Should().Contain(expectedFragment);
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData("http://shopsite.example.com/stripe/webhook")]
    [InlineData("shopsite.example.com/stripe/webhook")]
    // A bare path parses as an absolute *file* URI on Unix, which is why one message covers both
    // "no scheme" and "wrong scheme".
    [InlineData("/stripe/webhook")]
    [InlineData("ftp://shopsite.example.com/stripe/webhook")]
    public void UrlThatIsNotAbsoluteHttps_IsRejected(string url)
    {
        // Stripe will not deliver to plain http in live mode. Catching it here turns a silently
        // undeliverable endpoint into an argument error nobody can miss.
        var parsed = CommandLineParser.TryParse(
            ["ensure", "--url", url, "--tag", "azureprod", "--secret-out", "/tmp/s"],
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("absolute https URL");
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    [InlineData("azure prod")]
    [InlineData("azure/prod")]
    [InlineData("azure\"prod")]
    [InlineData("azureprod\n")]
    public void TagWithAwkwardCharacters_IsRejected(string tag)
    {
        // The tag becomes a Stripe metadata value and the sole identity key. A tag that does not
        // round-trip byte for byte is a tag that fails to match, which means a second endpoint gets
        // created instead of the first being reconciled.
        var parsed = CommandLineParser.TryParse(["remove", "--tag", tag], out _, out var error);

        parsed.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Usage_NeverSuggestsPassingTheApiKeyAsAnArgument()
    {
        // argv is world-readable on Linux CI agents. If the usage text ever grows a --api-key
        // option, this test is the thing that objects.
        CommandLineParser.Usage.Should().NotContain("--api-key");
        CommandLineParser.Usage.Should().Contain("Parameters__stripe_api_key");
    }
}
