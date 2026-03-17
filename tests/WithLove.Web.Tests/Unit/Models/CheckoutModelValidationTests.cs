using System.ComponentModel.DataAnnotations;

namespace WithLove.Web.Tests.Unit.Models;

public class CheckoutModelValidationTests
{
    private static CheckoutModel CreateValidModel() => new()
    {
        BillingEmail = "jane@example.com"
    };

    private static List<ValidationResult> Validate(CheckoutModel model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void ValidModel_PassesValidation()
    {
        var model = CreateValidModel();

        var results = Validate(model);

        results.Should().BeEmpty();
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    [InlineData("")]
    [InlineData(null)]
    public void MissingBillingEmail_FailsValidation(string? email)
    {
        var model = CreateValidModel();
        model.BillingEmail = email!;

        var results = Validate(model);

        results.Should().ContainSingle(r => r.MemberNames.Contains("BillingEmail"));
    }

    [Theory]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@missing.com")]
    public void InvalidEmail_FailsValidation(string email)
    {
        var model = CreateValidModel();
        model.BillingEmail = email;

        var results = Validate(model);

        results.Should().ContainSingle(r => r.MemberNames.Contains("BillingEmail"));
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void Message_IsOptional()
    {
        var model = CreateValidModel();
        model.Message = "";

        var results = Validate(model);

        results.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void RecipientFirstName_IsOptional()
    {
        var model = CreateValidModel();
        model.RecipientFirstName = "";

        var results = Validate(model);

        results.Should().BeEmpty();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.Validation)]
    public void AllFieldsMissing_ReturnsEmailError()
    {
        var model = new CheckoutModel();

        var results = Validate(model);

        results.Should().ContainSingle(r => r.MemberNames.Contains("BillingEmail"));
    }
}
