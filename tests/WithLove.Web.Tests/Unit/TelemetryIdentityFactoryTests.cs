namespace WithLove.Web.Tests.Unit;

public class TelemetryIdentityFactoryTests
{
    [Fact]
    public void Create_UsesStablePublicSampleKeyWithoutConfiguration()
    {
        var first = TelemetryIdentityFactory.Create().ForUser("user-42");
        var second = TelemetryIdentityFactory.Create().ForUser("user-42");

        first.Should().StartWith("hmac-demo-v1-");
        second.Should().Be(first);
        first.Should().NotContain("user-42");
    }

}
