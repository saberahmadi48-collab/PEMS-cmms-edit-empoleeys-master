using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for <see cref="PemsField"/>. Takes label, required flag and kind from the model metadata, the value from the model and
/// the first error from ModelState, so existing views migrate with a one-line change and keep model binding and validation.
/// </summary>
[HtmlTargetElement("pems-field", Attributes = "asp-for", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.Field)]
public sealed class PemsFieldTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("asp-for")] public ModelExpression For { get; set; } = default!;
    [HtmlAttributeName("kind")] public FieldKind? Kind { get; set; }
    [HtmlAttributeName("help")] public string? Help { get; set; }

    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var meta = For.Metadata;
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var kind = Kind ?? InferKind(meta.ModelType, meta.DataTypeName);
        var descriptor = new FieldDescriptor
        {
            Name = name,
            Label = TranslatableText.Literal(meta.DisplayName ?? meta.PropertyName ?? For.Name),
            Kind = kind,
            Required = meta.IsRequired,
            ReadOnly = meta.IsReadOnly,
            HelpText = TranslatableText.Literal(Help)
        };

        TranslatableText? error = null;
        string? attempted = null;
        if (ViewContext.ModelState.TryGetValue(name, out var entry))
        {
            // MVC has already localised the message (data annotations / validation attributes), so it enters as a language-neutral text.
            if (entry.Errors.Count > 0) error = TranslatableText.Literal(entry.Errors[0].ErrorMessage);
            attempted = entry.AttemptedValue;                       // what the user typed, like the built-in input tag helper
        }

        var html = await renderer.RenderAsync<PemsField>(new Dictionary<string, object?>
        {
            [nameof(PemsField.Descriptor)] = descriptor,
            [nameof(PemsField.Value)] = attempted ?? FormatValue(For.Model, kind),
            [nameof(PemsField.Error)] = error
        });

        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(html);
    }

    /// <summary>Values go through the invariant formats HTML inputs require (a date input only accepts yyyy-MM-dd).</summary>
    private static string? FormatValue(object? model, FieldKind kind) => model switch
    {
        null => null,
        DateTime d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => model.ToString()
    };

    private static FieldKind InferKind(Type type, string? dataType)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (dataType == "Password") return FieldKind.Password;
        if (dataType == "Currency") return FieldKind.Money;
        if (dataType == "Time" || t == typeof(TimeOnly)) return FieldKind.Time;
        if (t == typeof(bool)) return FieldKind.Checkbox;
        if (t == typeof(DateTime) || t == typeof(DateOnly)) return FieldKind.Date;
        if (t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double)) return FieldKind.Number;
        if (dataType == "MultilineText") return FieldKind.TextArea;
        return FieldKind.Text;
    }
}
