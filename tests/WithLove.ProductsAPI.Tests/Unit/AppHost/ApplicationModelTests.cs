using Aspire.Hosting;
using Aspire.Hosting.Dotnet;
using WithLove.AppHost.Extensions;

namespace WithLove.ProductsAPI.Tests.Unit.AppHost;

public class ApplicationModelTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    public void LocalModel_RegistersAllApplicationProjectsByPath()
    {
        var appHostDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../../src/WithLove.AppHost"));
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            ProjectDirectory = appHostDirectory,
            DisableDashboard = true,
        });

        builder.AddWithLoveApplication(isPublishMode: false, isTestMode: false);

        var projects = builder.Resources.OfType<DotnetProjectResource>().ToDictionary(resource => resource.Name);
        projects.Keys.Should().BeEquivalentTo("productsApi", "workflowServer", "shopSite");
        projects["productsApi"].WorkingDirectory.Should().Be(Path.GetFullPath(Path.Combine(appHostDirectory, "../WithLove.ProductsAPI")));
        projects["workflowServer"].WorkingDirectory.Should().Be(Path.GetFullPath(Path.Combine(appHostDirectory, "../WithLove.WorkflowServer")));
        projects["shopSite"].WorkingDirectory.Should().Be(Path.GetFullPath(Path.Combine(appHostDirectory, "../WithLove.Web")));
    }
}
