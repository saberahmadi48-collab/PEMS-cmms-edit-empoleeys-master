using System.Globalization;
using PEMS.Application.Validation;
using PEMS.UI.Abstractions;
using PEMS.Web.Api;

namespace PEMS.Web.Definitions;

/// <summary>
/// SCRUM-373 and SCRUM-380: the built-in texts of the constraint codes (<see cref="ConstraintCodes"/>) in both languages, and the one mapping of violations to the RFC 9457 field-error list the
/// REST API, forms and imports share (pointer, code, message key, parameters, localised message). A definition may override a text per field (<c>messages</c>); these are the defaults.
/// </summary>
public static class ValidationMessages
{
    private static readonly IReadOnlyDictionary<string, (string En, string Fa)> Texts = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        [ConstraintCodes.Required] = ("This field is required.", "این فیلد الزامی است."),
        [ConstraintCodes.MaxLength] = ("At most {max} characters.", "حداکثر {max} نویسه مجاز است."),
        [ConstraintCodes.MinLength] = ("At least {min} characters.", "حداقل {min} نویسه لازم است."),
        [ConstraintCodes.NotANumber] = ("Enter a number.", "یک عدد وارد کنید."),
        [ConstraintCodes.Min] = ("The smallest allowed value is {min}.", "کمترین مقدار مجاز {min} است."),
        [ConstraintCodes.Max] = ("The largest allowed value is {max}.", "بیشترین مقدار مجاز {max} است."),
        [ConstraintCodes.Scale] = ("At most {scale} digits after the decimal point.", "حداکثر {scale} رقم اعشار مجاز است."),
        [ConstraintCodes.NotADate] = ("Enter a valid date.", "یک تاریخ معتبر وارد کنید."),
        [ConstraintCodes.DateBefore] = ("The date cannot be before {min}.", "تاریخ نمی‌تواند پیش از {min} باشد."),
        [ConstraintCodes.DateAfter] = ("The date cannot be after {max}.", "تاریخ نمی‌تواند پس از {max} باشد."),
        [ConstraintCodes.NotATime] = ("Enter a valid time.", "یک ساعت معتبر وارد کنید."),
        [ConstraintCodes.NotAllowed] = ("Choose one of the listed values.", "یکی از مقدارهای فهرست را انتخاب کنید."),
        [ConstraintCodes.Pattern] = ("The value is not in the expected format.", "قالب مقدار درست نیست."),
        [ConstraintCodes.MinItems] = ("Enter at least {min} items.", "حداقل {min} مورد وارد کنید."),
        [ConstraintCodes.MaxItems] = ("Enter at most {max} items.", "حداکثر {max} مورد وارد کنید."),
        [ConstraintCodes.FileTooLarge] = ("The file is larger than {max} MB.", "حجم فایل بیشتر از {max} مگابایت است."),
        [ConstraintCodes.FileType] = ("This type of file is not allowed.", "این نوع فایل مجاز نیست."),
        [ConstraintCodes.FileCount] = ("Too many files (at most {max}).", "تعداد فایل‌ها بیش از حد است (حداکثر {max})."),
    };

    public static bool Has(string code) => Texts.ContainsKey(code);

    public static TranslatableText Text(string code, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        var (en, fa) = Texts[code];
        return TranslatableText.Of(("en", Fill(en, parameters)), ("fa", Fill(fa, parameters)));
    }

    private static string Fill(string template, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null) return template;
        foreach (var (key, value) in parameters) template = template.Replace("{" + key + "}", Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return template;
    }

    /// <summary>The violations as the field-error list of an RFC 9457 problem (422), with the message in <paramref name="culture"/>; advisory violations are not errors and are left out.</summary>
    public static IReadOnlyList<ApiFieldError> ToApiErrors(IEnumerable<Violation> violations, string culture)
        => violations.Where(v => !v.Advisory).Select(v => new ApiFieldError(v.Pointer, v.Code, v.MessageKey, Text(v.Code, v.Params).Resolve(CultureInfo.GetCultureInfo(culture)), v.Params.ToDictionary(p => p.Key, p => p.Value))).ToList();
}
