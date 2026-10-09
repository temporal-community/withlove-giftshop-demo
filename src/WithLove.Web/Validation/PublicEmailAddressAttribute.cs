using System.ComponentModel.DataAnnotations;

namespace WithLove.Web.Validation;

/// <summary>
/// Validates an email address intended for delivery over the public internet.
/// </summary>
/// <remarks>
/// <see cref="EmailAddressAttribute"/> accepts single-label domains such as
/// <c>user@localhost</c>. Registration sends transactional email and persists the address for
/// downstream services, so require a qualified domain with a non-empty top-level label.
/// </remarks>
public sealed class PublicEmailAddressAttribute : ValidationAttribute
{
    private static readonly EmailAddressAttribute BasicEmailAddress = new();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
            return ValidationResult.Success;

        if (!BasicEmailAddress.IsValid(value))
            return new ValidationResult(ErrorMessage);

        if (value is not string email)
            return new ValidationResult(ErrorMessage);

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 1 || atIndex == email.Length - 1)
            return new ValidationResult(ErrorMessage);

        var domain = email[(atIndex + 1)..];
        var labels = domain.Split('.');
        return labels.Length >= 2
            && labels.All(label => label.Length > 0)
            && labels[^1].Length >= 2
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage);
    }
}
