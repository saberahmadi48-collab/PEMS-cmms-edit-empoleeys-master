using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Mvc.Rendering;
using PEMS.UI.Components;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 TreeView: a hierarchy (folders, positions, menu items) as a WAI-ARIA tree: role="tree" / "treeitem" / "group", a
// toggle per branch, keyboard (arrows, Home, End, Enter) and the collapsed branches remembered per tree (pems-tree.js, loaded by
// the layout). A branch that holds the current item is never collapsed. Styles: pems-ui.css (pems-tree*), RTL from the same markup.
// Usage: docs/design/LIST_PAGE_COMPONENT.md (TreeView).

/// <summary>What a tree passes to its items (shared through TagHelperContext.Items).</summary>
public sealed class PemsTreeContext
{
    public string ToggleLabel { get; init; } = "";
}

/// <summary>What an item's children tell it: it has children; its label markup.</summary>
public sealed class PemsTreeItemContext
{
    public bool HasChildren { get; set; }
    public string? LabelHtml { get; set; }
}

/// <summary>
/// The tree: <c>label</c> is its accessible name, <c>toggle-label</c> the text of the expand / collapse buttons ("Expand or
/// collapse: Engineering"), <c>remember-key</c> the browser storage key of the collapsed branches (none: not remembered).
/// Child content: <c>pems-tree-item</c>s. A class or id on the tag is kept.
/// </summary>
[HtmlTargetElement("pems-tree")]
[PemsFacade(UiKeys.Tree)]
public sealed class PemsTreeTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("toggle-label")] public string ToggleLabel { get; set; } = "";
    [HtmlAttributeName("remember-key")] public string? RememberKey { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        context.Items[typeof(PemsTreeContext)] = new PemsTreeContext { ToggleLabel = ToggleLabel };
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTree>(renderer, output, new TreeOptions { Label = Label, RememberKey = RememberKey, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// One node: <c>label</c> (also the toggle's name), <c>key</c> (remembers its collapsed state), <c>href</c> (a link) or plain
/// text, <c>current</c> (the page it opens; never collapsed), <c>count</c> (a number after the label), <c>expanded</c> (default
/// open), <c>link-class</c> (the page's own class on the link or text). A <c>pems-tree-label</c> child replaces the label text
/// with the page's own markup; nested <c>pems-tree-item</c>s are its children.
/// </summary>
[HtmlTargetElement("pems-tree-item")]
[PemsFacade(UiKeys.TreeItem)]
public sealed class PemsTreeItemTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("key")] public string? Key { get; set; }
    [HtmlAttributeName("href")] public string? Href { get; set; }
    [HtmlAttributeName("current")] public bool Current { get; set; }
    [HtmlAttributeName("count")] public int? Count { get; set; }
    [HtmlAttributeName("expanded")] public bool Expanded { get; set; } = true;
    [HtmlAttributeName("link-class")] public string? LinkClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // tell the parent item it has a child, then give the children their own context
        if (context.Items.TryGetValue(typeof(PemsTreeItemContext), out var parentObj) && parentObj is PemsTreeItemContext parent) parent.HasChildren = true;
        var tree = context.Items.TryGetValue(typeof(PemsTreeContext), out var t) ? t as PemsTreeContext : null;
        var mine = new PemsTreeItemContext();
        context.Items[typeof(PemsTreeItemContext)] = mine;
        var children = (await output.GetChildContentAsync()).GetContent();

        var (css, attrs) = ListRender.Page(output);
        var options = new TreeItemOptions
        {
            Label = Label, Key = Key, Href = Href, Current = Current, CountText = Count?.ToString(System.Globalization.CultureInfo.CurrentCulture), Expanded = Expanded,
            HasChildren = mine.HasChildren, LinkClass = LinkClass, ToggleName = string.IsNullOrEmpty(tree?.ToggleLabel) ? Label : tree!.ToggleLabel + ": " + Label,
            CssClass = css, Attributes = attrs
        };
        var parameters = new Dictionary<string, object?> { ["Options"] = options };
        if (mine.HasChildren) parameters["ChildContent"] = BlockTagHelper.Markup(children);
        if (mine.LabelHtml != null) parameters["LabelContent"] = BlockTagHelper.Markup(mine.LabelHtml);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsTreeItem>(parameters));
    }
}

/// <summary>The page's own markup for the label of the enclosing <c>pems-tree-item</c> (a colour dot, an icon, a badge). It writes no markup of its own: the item's component renders it.</summary>
[HtmlTargetElement("pems-tree-label", ParentTag = "pems-tree-item")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTreeLabelTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var html = (await output.GetChildContentAsync()).GetContent();
        if (context.Items.TryGetValue(typeof(PemsTreeItemContext), out var o) && o is PemsTreeItemContext item) item.LabelHtml = html;
        output.SuppressOutput();
    }
}
