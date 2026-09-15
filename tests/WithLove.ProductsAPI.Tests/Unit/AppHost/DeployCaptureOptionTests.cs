namespace WithLove.ProductsAPI.Tests.Unit.AppHost;

using System.IO;

/// <summary>
/// Pins the <c>just deploy --capture</c> privacy opt-in at the deployment boundary.
/// </summary>
public class DeployCaptureOptionTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WithLoveShop.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the tests must be able to locate the repository root");
        return directory!.FullName;
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void DeployCaptureOption_OverridesTheSourcedConfiguration()
    {
        var justfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "justfile"));

        justfile.Should().Contain(
            "[arg(\"capture\", long=\"capture\", value=\"true\")]\ndeploy environment=\"azureprod\" reset_state=\"false\" capture=\"false\":",
            "just deploy must parse --capture as an option rather than as the deployment environment");
        justfile.Should().Contain(
            "source .secrets.env\n\n    # Apply the command-line privacy opt-in after loading local deployment settings, so\n    # `just deploy --capture` cannot be accidentally overridden by .secrets.env.\n    if [[ \"{{capture}}\" == \"true\" ]]; then\n        export Telemetry__CaptureAiContent=true\n    fi",
            "the explicit command-line opt-in must take precedence over .secrets.env");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void DeployCleanCaptureOption_ForwardsTheOptIn()
    {
        var justfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "justfile"));

        justfile.Should().Contain(
            "[arg(\"capture\", long=\"capture\", value=\"true\")]\ndeploy-clean environment=\"azureprod\" capture=\"false\":\n    just deploy \"{{environment}}\" true \"{{capture}}\"",
            "deploy-clean must preserve the same explicit capture option when it delegates to deploy");
    }
}
