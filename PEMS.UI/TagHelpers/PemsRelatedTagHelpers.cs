using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using PEMS.UI.Components;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 RelatedList: the child collection of a detail page (tasks of a job plan, lines of a receipt, rules of a table):
// a titled section with a count and an empty text, the child table, an add form under it, a delete form per row and, for
// tables edited in place, a hidden form per row whose inputs point at it with form="...". The page keeps its own columns,
// fields, actions and permissions. Styles: _content/PEMS.UI/pems-list.css (lp-rel-*). Usage: docs/design/LIST_PAGE_COMPONENT.md.

internal static class PemsRelated
{
    /// <summary>The antiforgery field of the current request, the same one the form tag helper adds (none outside a request).</summary>
    public static AntiforgeryField? Antiforgery(ViewContext? viewContext)
    {
        var http = viewContext?.HttpContext;
        if (http?.RequestServices?.GetService(typeof(IAntiforgery)) is not IAntiforgery antiforgery) return null;
        var tokens = antiforgery.GetAndStoreTokens(http);
        if (string.IsNullOrEmpty(tokens.FormFieldName) || string.IsNullOrEmpty(tokens.RequestToken)) return null;
        return new AntiforgeryField(tokens.FormFieldName, tokens.RequestToken);
    }
}

/// <summary>
/// The section of one child collection: icon, title and count in the header, the page's table (child content) and, when
/// <c>empty</c>, the empty text. <c>id</c> lets other parts of the page link to it.
/// </summary>
[HtmlTargetElement("pems-related-list")]
[PemsFacade(UiKeys.RelatedList)]
public sealed class PemsRelatedListTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string? Id { get; set; }
    [HtmlAttributeName("icon")] public string? Icon { get; set; }
    [HtmlAttributeName("title")] public string Title { get; set; } = "";
    [HtmlAttributeName("count")] public int? Count { get; set; }
    [HtmlAttributeName("empty")] public bool Empty { get; set; }
    [HtmlAttributeName("empty-text")] public string? EmptyText { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        var options = new RelatedListOptions
        {
            Id = Id, HeadingId = (string.IsNullOrEmpty(Id) ? "rel" + context.UniqueId : Id) + "Title", Icon = Icon, Title = Title, Count = Count,
            Empty = Empty, EmptyText = EmptyText, CssClass = css, Attributes = attrs
        };
        await ListRender.Replace<PemsRelatedList>(renderer, output, options, await ListRender.Inner(output));
    }
}

/// <summary>The child table (thead and tbody are the page's child content), scrolling sideways on narrow screens.</summary>
[HtmlTargetElement("pems-related-table")]
[PemsFacade(UiKeys.RelatedTable)]
public sealed class PemsRelatedTableTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string? Id { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // a page may put its own data-* hooks on the tag; they stay on the table
        var data = output.Attributes.Where(a => a.Name.StartsWith("data-", StringComparison.Ordinal)).ToDictionary(a => a.Name, a => a.Value?.ToString() ?? "");
        await ListRender.Replace<PemsRelatedTable>(renderer, output, new RelatedTableOptions { Id = Id, DataAttributes = data.Count == 0 ? null : data }, await ListRender.Inner(output));
    }
}

/// <summary>
/// The add form under the table: the parent key as a hidden field, the page's fields (child content) in one wrapping row and
/// a submit button (<c>submit-label</c>; icon only when <c>icon-only</c>).
/// </summary>
[HtmlTargetElement("pems-related-add")]
[PemsFacade(UiKeys.RelatedAdd)]
public sealed class PemsRelatedAddTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }
    [HtmlAttributeName("action")] public string Action { get; set; } = "";
    [HtmlAttributeName("parent-name")] public string? ParentName { get; set; }
    [HtmlAttributeName("parent-value")] public string? ParentValue { get; set; }
    [HtmlAttributeName("submit-label")] public string SubmitLabel { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => await ListRender.Replace<PemsRelatedAdd>(renderer, output,
            new RelatedAddOptions { Action = Action, ParentName = ParentName, ParentValue = ParentValue, SubmitLabel = SubmitLabel, Antiforgery = PemsRelated.Antiforgery(ViewContext) },
            await ListRender.Inner(output));
}

/// <summary>
/// The delete action of one row: a small form posting the row key (and the parent key, to come back to the parent), with an
/// icon button whose label is its tool tip; <c>confirm</c> asks first.
/// </summary>
[HtmlTargetElement("pems-related-remove", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.RelatedRemove)]
public sealed class PemsRelatedRemoveTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }
    [HtmlAttributeName("action")] public string Action { get; set; } = "";
    [HtmlAttributeName("key-name")] public string KeyName { get; set; } = "";
    [HtmlAttributeName("key-value")] public string? KeyValue { get; set; }
    [HtmlAttributeName("parent-name")] public string? ParentName { get; set; }
    [HtmlAttributeName("parent-value")] public string? ParentValue { get; set; }
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("confirm")] public string? Confirm { get; set; }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsRelatedRemove>(renderer, output, new RelatedRemoveOptions
        {
            Action = Action, KeyName = KeyName, KeyValue = KeyValue, ParentName = ParentName, ParentValue = ParentValue, Label = Label, Confirm = Confirm,
            Antiforgery = PemsRelated.Antiforgery(ViewContext)
        });
}

/// <summary>
/// In-place editing of one row: a hidden form (id = <c>id</c>) with the row's hidden keys (child content); the inputs in the
/// row's cells carry <c>form="id"</c>, and <c>&lt;pems-related-save form-id="id"&gt;</c> submits it.
/// </summary>
[HtmlTargetElement("pems-related-row-form")]
[PemsFacade(UiKeys.RelatedRowForm)]
public sealed class PemsRelatedRowFormTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("action")] public string Action { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => await ListRender.Replace<PemsRelatedRowForm>(renderer, output,
            new RelatedRowFormOptions { Id = Id, Action = Action, Antiforgery = PemsRelated.Antiforgery(ViewContext) }, await ListRender.Inner(output));
}

/// <summary>The save button of a row edited in place (submits the row's form named by <c>form-id</c>).</summary>
[HtmlTargetElement("pems-related-save", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.RelatedSave)]
public sealed class PemsRelatedSaveTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("form-id")] public string FormId { get; set; } = "";
    [HtmlAttributeName("label")] public string Label { get; set; } = "";

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsRelatedSave>(renderer, output, new RelatedSaveOptions { FormId = FormId, Label = Label });
}
