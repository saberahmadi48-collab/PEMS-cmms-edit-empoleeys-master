using Microsoft.AspNetCore.Http;
using PEMS.Application.Interfaces;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

/// <summary>
/// The server step between a posted form and anything that stores or shows its values (SCRUM-59). Every form built from a
/// <see cref="FormDefinition"/> (form builder, BPMS forms) passes its posted values through here first:
/// <list type="bullet">
/// <item>only the fields the definition declares are kept (a posted extra name is dropped, not stored);</item>
/// <item>a <see cref="FieldKind.RichText"/> value is untrusted HTML and is cleaned by <see cref="IRichTextSanitizerService"/>; an editor
/// value with no text and no image becomes empty, so a required check sees it as missing;</item>
/// <item>every other value is returned exactly as posted (formatting and validation are separate steps, see DataEntryUnaffectedTests).</item>
/// </list>
/// </summary>
public sealed class FormValueSanitizer(IRichTextSanitizerService richText)
{
    public IReadOnlyDictionary<string, string?> Sanitize(FormDefinition form, IReadOnlyDictionary<string, string?> posted)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(posted);

        var lookup = new Dictionary<string, string?>(posted, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in form.Fields)
        {
            if (!lookup.TryGetValue(field.Name, out var value)) continue;
            values[field.Name] = field.Kind == FieldKind.RichText ? CleanRichText(value) : value;
        }
        return values;
    }

    /// <summary>The same for an MVC post: a multi-value entry keeps its first value (a single field posts one value).</summary>
    public IReadOnlyDictionary<string, string?> Sanitize(FormDefinition form, IFormCollection posted)
    {
        ArgumentNullException.ThrowIfNull(posted);
        var flat = posted.ToDictionary(p => p.Key, p => (string?)p.Value.FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
        return Sanitize(form, flat);
    }

    private string CleanRichText(string? html)
    {
        var clean = richText.Sanitize(html);
        if (clean.Length == 0) return clean;
        // "<p><br></p>" is what an empty editor posts
        var hasText = richText.ToPlainText(clean).Length > 0;
        return hasText || clean.Contains("<img", StringComparison.OrdinalIgnoreCase) ? clean : string.Empty;
    }
}
