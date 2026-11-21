using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;

namespace PEMS.UI.TagHelpers;

/// <summary>Shared by the three control facades: the classes in front of the tag, and the bound attributes put back when the built-in helper did not run.</summary>
internal static class ControlTagHelper
{
    public static ControlOptions Options(ControlKind kind, string? scale, string? css) => new()
    {
        Kind = kind,
        Size = scale?.ToLowerInvariant() switch { "small" => Size.Small, "large" => Size.Large, _ => Size.Medium },
        CssClass = css
    };

    /// <summary>
    /// The control classes go first, where the pages wrote them. A class the built-in helper added (input-validation-error) stays after them.
    /// </summary>
    public static void PutClassFirst(TagHelperOutput output, string css)
    {
        var existing = output.Attributes.TryGetAttribute("class", out var attribute) ? attribute.Value?.ToString() : null;
        output.Attributes.RemoveAll("class");
        output.Attributes.Insert(0, new TagHelperAttribute("class", string.IsNullOrWhiteSpace(existing) ? css : css + " " + existing.Trim()));
    }

    public static void Restore(TagHelperOutput output, params (string Name, string? Value)[] bound)
    {
        foreach (var (name, value) in bound)
            if (value is not null) output.Attributes.Add(new TagHelperAttribute(name, value));
    }
}

/// <summary>
/// MVC facade for the bare input classes: <c>&lt;pems-input name="qty" type="number" scale="small" css="w-auto" /&gt;</c> becomes an <c>input</c> with
/// <c>form-control form-control-sm w-auto</c> (<c>kind="check"</c>: <c>form-check-input</c>). It keeps every other attribute the page wrote.
/// With <c>asp-for</c> it derives from the built-in input helper, so model binding, ids, values and validation attributes are the same as before;
/// the registered <c>input</c> component writes the element.
/// </summary>
[HtmlTargetElement("pems-input")]
[PemsFacade(UiKeys.Input)]
public sealed class PemsInputTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : InputTagHelper(generator)
{
    /// <summary>text (default) or check.</summary>
    [HtmlAttributeName("kind")] public string Kind { get; set; } = "text";
    /// <summary>small or large; empty for the default size.</summary>
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    /// <summary>The state attributes a page decides with a condition (<c>checked="@x"</c>): written as the attribute when true, left out when false (a plain bool attribute value would print checked="False").</summary>
    [HtmlAttributeName("checked")] public bool Checked { get; set; }
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }
    [HtmlAttributeName("readonly")] public bool ReadOnly { get; set; }
    [HtmlAttributeName("required")] public bool Required { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (For is not null) base.Process(context, output);
        else ControlTagHelper.Restore(output, ("name", Name), ("type", InputTypeName), ("value", Value), ("form", FormName));
        if (Checked) output.Attributes.SetAttribute("checked", "checked");
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");
        if (ReadOnly) output.Attributes.SetAttribute("readonly", "readonly");
        if (Required) output.Attributes.SetAttribute("required", "required");
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsInput>(renderer, output, new InputOptions { Kind = Kind, Scale = Scale, Css = Css, PageClass = css, Attributes = attrs });
    }
}

/// <summary>MVC facade for the bare select classes: <c>&lt;pems-select name="status" scale="small"&gt;options&lt;/pems-select&gt;</c>; <c>asp-for</c> and <c>asp-items</c> work as on a select.</summary>
[HtmlTargetElement("pems-select")]
[PemsFacade(UiKeys.Select)]
public sealed class PemsSelectTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : SelectTagHelper(generator)
{
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    /// <summary>Written as the attribute when true, left out when false (a plain bool attribute value would print disabled="False").</summary>
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (For is not null) base.Process(context, output);
        else ControlTagHelper.Restore(output, ("name", Name));
        if (Disabled) output.Attributes.SetAttribute("disabled", "disabled");
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsSelect>(renderer, output, new SelectOptions { Scale = Scale, Css = Css, PageClass = css, Attributes = attrs }, await ListRender.InnerAfterFramework(output));
    }
}

/// <summary>MVC facade for the bare textarea classes: <c>&lt;pems-textarea name="note" rows="3"&gt;text&lt;/pems-textarea&gt;</c>.</summary>
[HtmlTargetElement("pems-textarea")]
[PemsFacade(UiKeys.Textarea)]
public sealed class PemsTextAreaTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : TextAreaTagHelper(generator)
{
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (For is not null) base.Process(context, output);
        else ControlTagHelper.Restore(output, ("name", Name));
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTextarea>(renderer, output, new TextareaOptions { Scale = Scale, Css = Css, PageClass = css, Attributes = attrs }, await ListRender.InnerAfterFramework(output));
    }
}

/// <summary>
/// MVC facade for the form label: <c>&lt;pems-label css="small" for="qty"&gt;Quantity&lt;/pems-label&gt;</c> becomes <c>&lt;label class="form-label small" for="qty"&gt;</c>.
/// With <c>asp-for</c> it derives from the built-in label helper, so the text comes from the model metadata when the tag has no content.
/// </summary>
[HtmlTargetElement("pems-label")]
[PemsFacade(UiKeys.FormLabel)]
public sealed class PemsLabelTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : LabelTagHelper(generator)
{
    [HtmlAttributeName("css")] public string? Css { get; set; }
    /// <summary>label (default), span or legend.</summary>
    [HtmlAttributeName("as")] public string As { get; set; } = "label";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (For is not null) await base.ProcessAsync(context, output);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsFormLabel>(renderer, output, new FormLabelOptions { As = As, Css = Css, PageClass = css, Attributes = attrs }, await ListRender.InnerAfterFramework(output));
    }
}

/// <summary>
/// MVC facade for the validation summary box: <c>&lt;pems-validation-summary asp-validation-summary="All" css="py-2 px-3 small"&gt;</c> writes the
/// <c>alert alert-danger</c> classes in front of the ones the built-in helper adds (validation-summary-errors / -valid) and keeps every other attribute.
/// </summary>
[HtmlTargetElement("pems-validation-summary")]
[PemsFacade(UiKeys.ValidationSummary)]
public sealed class PemsValidationSummaryTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : ValidationSummaryTagHelper(generator)
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        base.Process(context, output);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsValidationSummary>(renderer, output, new ValidationSummaryOptions { Css = Css, PageClass = css, Attributes = attrs }, await ListRender.InnerAfterFramework(output));
    }
}
