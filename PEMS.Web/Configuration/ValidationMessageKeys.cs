using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace PEMS.Web.Configuration;

/// <summary>
/// SCRUM-540: the default messages of the validation attributes ("The {0} field is required.") were English on a Persian page, because the
/// data-annotation localizer (Program.cs, SCRUM-477) translates only an attribute that has an <c>ErrorMessage</c>. This provider gives every
/// validation attribute without its own message the framework's default English text as its <c>ErrorMessage</c>; that text is the resource key,
/// so a Persian page shows the Persian value and an English page shows the same words as before. The implicit [Required] of non-nullable
/// properties is covered too (it is in the same metadata list). Attributes that name their own message or resource are left alone.
/// </summary>
public sealed class ValidationMessageKeys : IValidationMetadataProvider
{
    /// <summary>The resource key (the framework's default English message) of each attribute type; a key per StringLength shape.</summary>
    public static string? KeyFor(ValidationAttribute attribute) => attribute switch
    {
        RequiredAttribute => "The {0} field is required.",
        EmailAddressAttribute => "The {0} field is not a valid e-mail address.",
        PhoneAttribute => "The {0} field is not a valid phone number.",
        UrlAttribute => "The {0} field is not a valid fully-qualified http, https, or ftp URL.",
        StringLengthAttribute s when s.MinimumLength > 0 => "The field {0} must be a string with a minimum length of {2} and a maximum length of {1}.",
        StringLengthAttribute => "The field {0} must be a string with a maximum length of {1}.",
        MaxLengthAttribute => "The field {0} must be a string or array type with a maximum length of '{1}'.",
        MinLengthAttribute => "The field {0} must be a string or array type with a minimum length of '{1}'.",
        RangeAttribute => "The field {0} must be between {1} and {2}.",
        CompareAttribute => "'{0}' and '{1}' do not match.",
        RegularExpressionAttribute => "The field {0} must match the regular expression '{1}'.",
        _ => null
    };

    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (!string.IsNullOrEmpty(attribute.ErrorMessage) || !string.IsNullOrEmpty(attribute.ErrorMessageResourceName) || attribute.ErrorMessageResourceType != null)
                continue;
            if (KeyFor(attribute) is { } key) attribute.ErrorMessage = key;
        }
    }
}
