using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// SCRUM-530: <c>&lt;pems-file-preview src="@Url.Action("Preview", "Files", new { id })" title="@file.Name" empty-text="@Localizer["..."]" /&gt;</c>
/// shows a stored file inline (the <see cref="PemsFilePreview"/> component). Use the same-site <c>Files/Preview</c> URL, which keeps
/// the upload rules of SCRUM-202 (PDF, images and text inline under their own CSP, every other type downloaded).
/// </summary>
[HtmlTargetElement("pems-file-preview", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.FilePreview)]
public sealed class PemsFilePreviewTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("src")] public string? Src { get; set; }
    [HtmlAttributeName("title")] public string? Title { get; set; }
    [HtmlAttributeName("empty-text")] public string? EmptyText { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var id = output.Attributes.TryGetAttribute("id", out var a) ? a.Value?.ToString() : null;
        var parameters = new Dictionary<string, object?> { [nameof(PemsFilePreview.Src)] = Src, [nameof(PemsFilePreview.Id)] = id };
        if (!string.IsNullOrWhiteSpace(Title)) parameters[nameof(PemsFilePreview.Title)] = PEMS.UI.Abstractions.TranslatableText.Literal(Title);
        if (!string.IsNullOrWhiteSpace(EmptyText)) parameters[nameof(PemsFilePreview.EmptyText)] = PEMS.UI.Abstractions.TranslatableText.Literal(EmptyText);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsFilePreview>(parameters));
    }
}
