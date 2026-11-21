using System.Globalization;
using PEMS.UI.Abstractions;
using PEMS.Web.Api;

namespace PEMS.Web.Definitions;

/// <summary>
/// SCRUM-380: one shape of a field error for the API, the form post and the import: <see cref="ApiFieldError"/> (pointer, code, messageKey, message in the user's language, params). The API and the validator already
/// build it; this adapter gives the other two the same members, so a client or a form component renders one thing. Stable constraint codes are the catalog's; an action that refuses with free text has the code <c>refused</c>.
/// </summary>
public static class ErrorShape
{
    public const string RefusedCode = "refused";
    public const string RefusedKey = "Err_Refused";

    /// <summary>The refusal of an action as field errors: one per refused field (pointer <c>/field</c>) and one for the whole form (pointer empty).</summary>
    public static IReadOnlyList<ApiFieldError> FromOutcome(ActionOutcome outcome, string culture)
    {
        var info = CultureInfo.GetCultureInfo(culture);
        var errors = new List<ApiFieldError>();
        if (outcome.FormError is { } form) errors.Add(new ApiFieldError("", RefusedCode, RefusedKey, form.Resolve(info)));
        foreach (var (field, text) in outcome.FieldErrors ?? new Dictionary<string, TranslatableText>())
            errors.Add(new ApiFieldError("/" + field, RefusedCode, RefusedKey, text.Resolve(info)));
        return errors;
    }

    /// <summary>A problem of one cell of an import file: pointer <c>/rows/{row}/{field}</c> (row numbers count from 1 as in the file).</summary>
    public static ApiFieldError ImportCell(int row, string field, string code, string messageKey, string message)
        => new($"/rows/{row}/{field}", code, messageKey, message);
}
