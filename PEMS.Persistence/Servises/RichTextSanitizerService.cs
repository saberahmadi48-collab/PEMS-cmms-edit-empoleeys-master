using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using PEMS.Application.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// پیاده‌سازی پاک‌سازی HTML با HtmlSanitizer (کتابخانه‌ی Ganss.Xss) - تگ‌ها و ویژگی‌های
/// موردنیاز ادیتور متن غنی (Bold/Italic/Underline/Font/Size/Color/Alignment/RTL-LTR/Lists/
/// Table/Link) مجاز هستند؛ هر اسکریپت یا رویداد جاوااسکریپت حذف می‌شود.
/// </summary>
public class RichTextSanitizerService : IRichTextSanitizerService
{
    private readonly HtmlSanitizer _sanitizer;

    public RichTextSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "p", "br", "b", "strong", "i", "em", "u", "s", "strike", "span", "div",
            "ul", "ol", "li", "a", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote",
            "table", "thead", "tbody", "tr", "td", "th", "font", "hr", "sub", "sup", "img"
        })
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "style", "class", "href", "target", "rel", "dir", "align", "color", "size", "face", "colspan", "rowspan", "src", "alt", "width", "height" })
        {
            _sanitizer.AllowedAttributes.Add(attr);
        }

        // SCRUM-59: links and images only to http(s), mailto or a relative URL of PEMS (no javascript:, data:, vbscript:)
        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            _sanitizer.AllowedSchemes.Add(scheme);
        }

        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement element) return;

            // SCRUM-59: only the editor's own classes (ql-align-right, ql-direction-rtl, ql-indent-1, ql-font-tahoma, ...). Any other class
            // could pull page CSS into stored content (for example a full-screen overlay that imitates a login form).
            var classes = element.GetAttribute("class");
            if (classes is not null)
            {
                var kept = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsEditorClass).ToArray();
                if (kept.Length == 0) element.RemoveAttribute("class");
                else element.SetAttribute("class", string.Join(' ', kept));
            }

            // a link that opens a new window must not get a handle on PEMS (reverse tabnabbing)
            if (element.LocalName == "a" && element.HasAttribute("target"))
            {
                element.SetAttribute("rel", "noopener noreferrer");
            }
        };

        _sanitizer.AllowedCssProperties.Clear();
        foreach (var css in new[]
        {
            "color", "background-color", "font-size", "font-family", "font-weight", "font-style",
            "text-align", "text-decoration", "direction", "margin", "padding"
        })
        {
            _sanitizer.AllowedCssProperties.Add(css);
        }
    }

    private static readonly Regex EditorClass = new("^ql-[a-z0-9-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsEditorClass(string name) => EditorClass.IsMatch(name);

    public string Sanitize(string? html)
    {
        return string.IsNullOrWhiteSpace(html) ? string.Empty : _sanitizer.Sanitize(html);
    }

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var noTags = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        noTags = Regex.Replace(noTags, "</p>|</div>|</li>", "\n", RegexOptions.IgnoreCase);
        noTags = Regex.Replace(noTags, "<[^>]+>", string.Empty);
        noTags = System.Net.WebUtility.HtmlDecode(noTags);

        return noTags.Trim();
    }
}
