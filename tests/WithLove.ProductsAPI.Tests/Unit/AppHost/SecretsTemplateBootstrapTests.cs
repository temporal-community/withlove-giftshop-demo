namespace WithLove.ProductsAPI.Tests.Unit.AppHost;

using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Guards the coupling between <c>.secrets.env.example</c> and the placeholder-seeding branch in
/// <c>just deploy</c>.
/// </summary>
/// <remarks>
/// <para>
/// The deploy recipe seeds <c>whsec_placeholder</c> only when the variable is empty:
/// <c>if [[ -z "${Parameters__stripe_webhook_secret:-}" ]]</c>. The documented setup path is
/// <c>cp .secrets.env.example .secrets.env</c>, so the template decides whether that branch ever
/// runs. Nothing else connects the two, and they live in different languages in different files.
/// </para>
/// <para>
/// This has already gone wrong once. The template previously shipped
/// <c>export Parameters__stripe_webhook_secret="&lt;your-stripe-webhook-secret&gt;"</c> — a non-empty
/// value — so on the documented path the guard never fired and that literal string became the
/// parameter. On <c>aspire publish</c> the validator rejected it loudly; on <c>aspire deploy</c>
/// the validator was absent, so it reached Key Vault unchecked and was only corrected afterwards
/// by the post-deploy step. If that step failed, the deployment reported success with an unusable
/// webhook secret installed.
/// </para>
/// <para>
/// These are file-content assertions on purpose: they need no shell, no <c>just</c>, and no Azure,
/// so they run in CI where a bash harness would not.
/// </para>
/// </remarks>
public class SecretsTemplateBootstrapTests
{
    private const string VariableName = "Parameters__stripe_webhook_secret";

    /// <summary>
    /// Walks up from the test assembly to the repository root. The tests assert on files that are
    /// repository configuration rather than build output, so they are not copied next to the DLL.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WithLoveShop.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the tests must be able to locate the repository root");
        return directory!.FullName;
    }

    private static string SecretsTemplate() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), ".secrets.env.example"));

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void Template_DoesNotAssignTheWebhookSecret()
    {
        // Matches an assignment with or without `export`, ignoring commented lines. A comment
        // mentioning the variable is fine and is in fact how the template documents it.
        var assignment = new Regex(
            $@"^[ \t]*(export[ \t]+)?{Regex.Escape(VariableName)}[ \t]*=",
            RegexOptions.Multiline);

        assignment.IsMatch(SecretsTemplate()).Should().BeFalse(
            "the deploy recipe seeds whsec_placeholder only when this variable is empty, so any "
            + "assignment in the template — even an obviously-fake one like <your-secret> — "
            + "silently disables the seeding branch and sends that literal to Key Vault");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void Template_TellsTheOperatorNotToSetItManually()
    {
        // Absence alone is ambiguous: a reader cannot tell a deliberate omission from an oversight,
        // and the natural repair for a missing variable is to add it back — which reintroduces the
        // defect. The template must say why it is absent.
        SecretsTemplate().Should().Contain(
            VariableName,
            "the template must still mention the variable so its absence reads as deliberate rather "
            + "than as an omission someone should helpfully correct");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void SeedingBranch_StillGuardsOnEmptiness()
    {
        // The other half of the coupling. If the guard is ever loosened or removed, the template's
        // deliberate silence stops being safe and the parameter resolves empty instead.
        var justfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "justfile"));

        justfile.Should().Contain(
            $@"if [[ -z ""${{{VariableName}:-}}"" ]]; then",
            "deploy must still seed the placeholder when the variable is empty; the template omits "
            + "the assignment precisely so this branch runs");
    }
}
