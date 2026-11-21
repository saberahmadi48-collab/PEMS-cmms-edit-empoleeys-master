using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Localization;

/// <summary>
/// Safety net for legacy Razor text which was written directly in a view instead of through
/// <c>IStringLocalizer</c>.  It only replaces a complete text/attribute value when that exact
/// value is an existing resource key; record data and unknown text are deliberately untouched.
/// New views must still use the localizer explicitly.
/// </summary>
public static partial class HtmlLocalization
{
    [GeneratedRegex(@">(?<text>[^<]+)<", RegexOptions.CultureInvariant)]
    private static partial Regex TextNode();

    [GeneratedRegex("""(?<before>\b(?:aria-label|placeholder|title|data-pems-confirm)\s*=\s*\")(?<text>[^\"]*)(?<after>\")""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LocalizableAttribute();

    public static string LocalizeLegacyHtml(string html, IStringLocalizer<SharedResource> localizer)
    {
        var attributes = LocalizableAttribute().Replace(html, m =>
            m.Groups["before"].Value + LocalizeEncoded(m.Groups["text"].Value, localizer) + m.Groups["after"].Value);

        return TextNode().Replace(attributes, m =>
            ">" + LocalizeEncoded(m.Groups["text"].Value, localizer) + "<");
    }

    private static string LocalizeEncoded(string encoded, IStringLocalizer<SharedResource> localizer)
    {
        var decoded = WebUtility.HtmlDecode(encoded);
        if (string.IsNullOrWhiteSpace(decoded)) return encoded;

        var localized = localizer[decoded];
        return localized.ResourceNotFound ? encoded : WebUtility.HtmlEncode(localized.Value);
    }
}
