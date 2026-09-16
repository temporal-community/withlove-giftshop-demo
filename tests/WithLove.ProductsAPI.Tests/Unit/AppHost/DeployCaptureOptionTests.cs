namespace WithLove.ProductsAPI.Tests.Unit.AppHost;

using System.IO;

/// <summary>
/// Pins the <c>just deploy</c> telemetry overrides at the deployment boundary.
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
    public void DeployTelemetryOptions_OverrideTheSourcedConfiguration()
    {
        var justfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "justfile"));

        justfile.Should().Contain(
            "[arg(\"capture\", long=\"capture\", value=\"true\")]\n[arg(\"all_traces\", long=\"all-traces\", value=\"true\")]\n[arg(\"trace_destination\", long=\"trace-destination\")]\ndeploy environment=\"azureprod\" reset_state=\"false\" capture=\"false\" all_traces=\"false\" trace_destination=\"\":",
            "just deploy must parse its telemetry options rather than treating them as deployment environments");
        justfile.Should().Contain(
            "source .secrets.env\n\n    # Apply the command-line privacy opt-in after loading local deployment settings, so\n    # `just deploy --capture` cannot be accidentally overridden by .secrets.env.\n    if [[ \"{{capture}}\" == \"true\" ]]; then\n        export Telemetry__CaptureAiContent=true\n    fi",
            "the explicit command-line opt-in must take precedence over .secrets.env");
        justfile.Should().Contain(
            "if [[ -n \"{{trace_destination}}\" ]]; then\n        export Trace__Destination=\"{{trace_destination}}\"\n    fi\n    if [[ \"{{all_traces}}\" == \"true\" ]]; then\n        export Trace__AiOnly=false\n    fi",
            "the destination and trace-scope flags must override sourced deployment configuration");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void DeployCleanTelemetryOptions_ForwardTheOverrides()
    {
        var justfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "justfile"));

        justfile.Should().Contain(
            "[arg(\"capture\", long=\"capture\", value=\"true\")]\n[arg(\"all_traces\", long=\"all-traces\", value=\"true\")]\n[arg(\"trace_destination\", long=\"trace-destination\")]\ndeploy-clean environment=\"azureprod\" capture=\"false\" all_traces=\"false\" trace_destination=\"\":\n    just deploy \"{{environment}}\" true \"{{capture}}\" \"{{all_traces}}\" \"{{trace_destination}}\"",
            "deploy-clean must preserve every explicit telemetry override when it delegates to deploy");
    }
}
