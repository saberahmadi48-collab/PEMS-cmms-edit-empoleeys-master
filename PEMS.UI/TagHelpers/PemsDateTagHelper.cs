using PEMS.UI.Rendering;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// A date field that follows the page calendar (SCRUM-225, #206). The value posted is always the Gregorian date-only ISO text <c>yyyy-MM-dd</c>, so model binding and storage
/// do not change; only what the person sees and picks differs.
/// <list type="bullet">
/// <item><c>calendar="gregorian"</c>, or <c>auto</c> on a page that is not Persian: the native date input, exactly what the pages wrote by hand (<c>form-control</c> classes, name, value, min, max).</item>
/// <item><c>calendar="jalali"</c>, or <c>auto</c> on a Persian page: a text input showing the Jalali date (<c>1403/12/30</c>) that opens the vendored Jalali picker
/// (<c>pems-date.js</c>), plus a hidden input with the posted name holding the ISO value. If the script or the library does not load, the script turns the pair back into the native
/// date input, so a date can always be entered.</item>
/// </list>
/// Every other attribute (id, data-*, aria-*, placeholder, required, disabled, readonly, title) goes to the visible input. Dates in <c>value</c>, <c>min</c> and <c>max</c> are ISO text.
/// </summary>
[HtmlTargetElement("pems-date")]
[PemsFacade(UiKeys.Date)]
public sealed class PemsDateTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("name")] public string? Name { get; set; }
    /// <summary>ISO date <c>yyyy-MM-dd</c> (a time part after it is ignored); empty for no date.</summary>
    [HtmlAttributeName("value")] public string? Value { get; set; }
    [HtmlAttributeName("min")] public string? Min { get; set; }
    [HtmlAttributeName("max")] public string? Max { get; set; }
    /// <summary>small or large; empty for the default size.</summary>
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    /// <summary>auto (default: Jalali on a Persian page), jalali or gregorian.</summary>
    [HtmlAttributeName("calendar")] public string Calendar { get; set; } = "auto";
    /// <summary>A model property (<c>asp-for="StartDate"</c>): name, id, the ISO value and the validation attributes come from the built-in generator, as for any input.</summary>
    [HtmlAttributeName("asp-for")] public ModelExpression? For { get; set; }
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }
    /// <summary>The state attributes a page decides with a condition: written as the attribute when true, left out when false (a plain bool attribute value would print required="False").</summary>
    [HtmlAttributeName("required")] public bool Required { get; set; }
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }
    [HtmlAttributeName("readonly")] public bool ReadOnly { get; set; }
    /// <summary>SCRUM-543: a date and a time. Posts <c>yyyy-MM-ddTHH:mm</c>, the text the native datetime-local input posts, so binding does not change;
    /// on a Persian page the person sees <c>1403/12/30 14:30</c> and picks it with the same Jalali picker. <c>min</c> and <c>max</c> are not used in this mode.
    /// False (the default) keeps the date-only output exactly as before.</summary>
    [HtmlAttributeName("time")] public bool Time { get; set; }

    private static readonly PersianCalendar Persian = new();
    private static readonly string[] DateTimeFormats =
        ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];

    /// <summary>SCRUM-543: the ISO date-time text <c>yyyy-MM-ddTHH:mm</c> of a date or date-time given as ISO text, or empty.</summary>
    public static string ToIsoDateTimeText(string? text) => ParseIsoDateTime(text)?.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) ?? "";

    /// <summary>SCRUM-543: the Jalali text <c>yyyy/MM/dd HH:mm</c> of an ISO date-time, or empty when the text is not a date.</summary>
    public static string ToJalaliDateTimeText(string? text)
    {
        var d = ParseIsoDateTime(text);
        return d is null ? "" : $"{Persian.GetYear(d.Value):D4}/{Persian.GetMonth(d.Value):D2}/{Persian.GetDayOfMonth(d.Value):D2} {d.Value.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    private static DateTime? ParseIsoDateTime(string? text)
        => !string.IsNullOrWhiteSpace(text) && DateTime.TryParseExact(text.Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>The Jalali text <c>yyyy/MM/dd</c> of an ISO date, or empty when the text is not a date.</summary>
    public static string ToJalaliText(string? iso)
    {
        var date = ParseIso(iso);
        return date is null ? "" : $"{Persian.GetYear(date.Value):D4}/{Persian.GetMonth(date.Value):D2}/{Persian.GetDayOfMonth(date.Value):D2}";
    }

    /// <summary>The ISO text of a date given as ISO text (normalised), or empty.</summary>
    public static string ToIsoText(string? iso) => ParseIso(iso)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static DateTime? ParseIso(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var head = text.Trim();
        if (head.Length > 10) head = head[..10];
        return DateTime.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private bool UseJalali() => Calendar.Trim().ToLowerInvariant() switch
    {
        "jalali" => true,
        "gregorian" => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fa", StringComparison.OrdinalIgnoreCase),
    };

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (For is not null && ViewContext is not null)
        {
            var box = generator.GenerateTextBox(ViewContext, For.ModelExplorer, For.Name, null, Time ? "{0:yyyy-MM-ddTHH:mm}" : "{0:yyyy-MM-dd}", null);
            Name ??= box.Attributes.GetValueOrDefault("name");
            Value ??= box.Attributes.GetValueOrDefault("value");
            foreach (var attribute in box.Attributes)
                if (attribute.Key is not ("name" or "value" or "type" or "class") && !output.Attributes.ContainsName(attribute.Key))
                    output.Attributes.Add(attribute.Key, attribute.Value);
        }
        if (Required) output.Attributes.SetAttribute("required", "required");
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");
        if (ReadOnly) output.Attributes.SetAttribute("readonly", "readonly");
        var css = ControlState.Css(ControlTagHelper.Options(ControlKind.Text, Scale, Css));
        var iso = ToIsoText(Value);
        var jalali = UseJalali();

        output.TagName = "input";
        output.TagMode = TagMode.SelfClosing;
        var id = output.Attributes.TryGetAttribute("id", out var idAttribute) ? idAttribute.Value?.ToString() : null;

        if (Time)
        {
            await ProcessDateTime(output, css, id, jalali);
            return;
        }

        if (!jalali)
        {
            ControlTagHelper.PutClassFirst(output, css);
            ControlTagHelper.Restore(output, ("type", "date"), ("name", Name), ("value", iso.Length > 0 ? iso : null), ("min", ToIsoText(Min) is { Length: > 0 } min ? min : null), ("max", ToIsoText(Max) is { Length: > 0 } max ? max : null));
            await Render(output, null);
            return;
        }

        // Jalali: the visible text input keeps the id and every other attribute; the hidden input carries the name and the ISO value.
        var hiddenId = (string.IsNullOrEmpty(id) ? "pdt" + Guid.NewGuid().ToString("N")[..8] : id) + "-iso";
        ControlTagHelper.PutClassFirst(output, css);
        ControlTagHelper.Restore(output, ("type", "text"), ("dir", "ltr"), ("inputmode", "numeric"), ("autocomplete", "off"));
        if (!output.Attributes.ContainsName("placeholder")) output.Attributes.Add("placeholder", "1403/01/01");
        output.Attributes.Add("data-jdp", "");
        output.Attributes.Add("data-jdp-target-value-input", "#" + hiddenId);
        output.Attributes.Add("data-jdp-target-value-type", "gregorian");
        var minText = ToJalaliText(Min);
        var maxText = ToJalaliText(Max);
        // the ISO limits stay on the element too: the script restores the native date input from them if the picker cannot start
        if (minText.Length > 0) { output.Attributes.Add("data-jdp-min-date", minText); output.Attributes.Add("data-pems-date-min", ToIsoText(Min)); }
        if (maxText.Length > 0) { output.Attributes.Add("data-jdp-max-date", maxText); output.Attributes.Add("data-pems-date-max", ToIsoText(Max)); }
        var text = ToJalaliText(iso);
        if (text.Length > 0) output.Attributes.Add("value", text);

        var hidden = new DateHidden
        {
            Id = hiddenId, Name = string.IsNullOrEmpty(Name) ? null : Name, Value = iso, Disabled = output.Attributes.ContainsName("disabled"),
            Form = output.Attributes.TryGetAttribute("form", out var form) && form.Value is not null ? form.Value.ToString() : null
        };
        await Render(output, hidden);
    }

    /// <summary>
    /// SCRUM-543: date and time. Gregorian: the native <c>datetime-local</c> input the pages wrote by hand. Jalali: a text input showing <c>yyyy/MM/dd HH:mm</c> with the picker's
    /// time panel (<c>pems-date.js</c> turns the time on for inputs marked <c>data-pems-date-time</c>), and the hidden input with the posted name and the ISO date-time. The script,
    /// not the picker, writes the hidden value (<c>data-pems-date-target</c>), so the posted text is always <c>yyyy-MM-ddTHH:mm</c>.
    /// </summary>
    private async Task ProcessDateTime(TagHelperOutput output, string css, string? id, bool jalali)
    {
        var iso = ToIsoDateTimeText(Value);
        ControlTagHelper.PutClassFirst(output, css);
        if (!jalali)
        {
            ControlTagHelper.Restore(output, ("type", "datetime-local"), ("name", Name), ("value", iso.Length > 0 ? iso : null));
            await Render(output, null);
            return;
        }

        var hiddenId = (string.IsNullOrEmpty(id) ? "pdt" + Guid.NewGuid().ToString("N")[..8] : id) + "-iso";
        ControlTagHelper.Restore(output, ("type", "text"), ("dir", "ltr"), ("autocomplete", "off"));
        if (!output.Attributes.ContainsName("placeholder")) output.Attributes.Add("placeholder", "1403/01/01 08:00");
        output.Attributes.Add("data-jdp", "");
        output.Attributes.Add("data-pems-date-time", "");
        output.Attributes.Add("data-pems-date-target", "#" + hiddenId);
        var text = ToJalaliDateTimeText(iso);
        if (text.Length > 0) output.Attributes.Add("value", text);

        var hidden = new DateHidden
        {
            Id = hiddenId, Name = string.IsNullOrEmpty(Name) ? null : Name, Value = iso, Disabled = output.Attributes.ContainsName("disabled"),
            Form = output.Attributes.TryGetAttribute("form", out var form) && form.Value is not null ? form.Value.ToString() : null
        };
        await Render(output, hidden);
    }

    /// <summary>The visible input keeps every attribute the helper resolved on the tag, in order; the component writes it and, for Jalali, the hidden input.</summary>
    private Task Render(TagHelperOutput output, DateHidden? hidden)
        => ListRender.Replace<PemsDate>(renderer, output, new DateOptions { Attributes = BlockTagHelper.PageAttributes(output), Hidden = hidden });
}
