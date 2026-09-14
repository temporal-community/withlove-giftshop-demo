using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using WithLove.Web.Validation;

namespace WithLove.Web.Tests.Unit.Validation;

public class PublicEmailAddressAttributeTests
{
    private static ValidationResult? Validate(string? email) =>
        new PublicEmailAddressAttribute().GetValidationResult(
            email,
            new ValidationContext(new object()));

    [Theory]
    [InlineData("cecil@test")]
    [InlineData("cecil@localhost")]
    [InlineData("cecil@example.")]
    [InlineData("cecil@example.c")]
    public void SingleLabelOrIncompleteDomain_FailsValidation(string email)
    {
        Validate(email).Should().NotBe(ValidationResult.Success);
    }

    [Theory]
    [InlineData("cecil@example.com")]
    [InlineData("cecil@test.com")]
    [InlineData("cecil+orders@example.co.uk")]
    public void QualifiedDomain_PassesValidation(string email)
    {
        Validate(email).Should().Be(ValidationResult.Success);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingValue_IsLeftToRequiredValidator(string? email)
    {
        Validate(email).Should().Be(ValidationResult.Success);
    }
}
