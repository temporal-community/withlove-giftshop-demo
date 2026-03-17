namespace WithLove.Web.Tests.Unit.Services;

public class AnonymousCartSessionTests
{
    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void CartId_DefaultsToEmpty()
    {
        var session = new AnonymousCartSession();

        session.CartId.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Initialize_SetsCartId()
    {
        var session = new AnonymousCartSession();

        session.Initialize("abc123");

        session.CartId.Should().Be("abc123");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void Initialize_OverwritesPreviousValue()
    {
        var session = new AnonymousCartSession();
        session.Initialize("first");

        session.Initialize("second");

        session.CartId.Should().Be("second");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void CartId_PublicSetter_SetsValue()
    {
        var session = new AnonymousCartSession();

        session.CartId = "direct-set";

        session.CartId.Should().Be("direct-set");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Cart)]
    public void CartId_HasPersistentStateAttribute()
    {
        var property = typeof(AnonymousCartSession).GetProperty(nameof(AnonymousCartSession.CartId));

        property.Should().NotBeNull();
        property!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.PersistentStateAttribute), false)
            .Should().ContainSingle();
    }
}
