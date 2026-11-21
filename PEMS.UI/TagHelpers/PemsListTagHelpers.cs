using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Mvc.Rendering;
using PEMS.UI.Components;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PEMS.UI.TagHelpers;

// SCRUM-337: the list page (frame, toolbar, filters dropdown, KPI row, chips, scrolling table, empty state, footer) with the
// look of the correspondence and Engineering Documents lists. These tag helpers own the structure, the classes, the ARIA and the
// data-* hooks of pems-list.js; the page keeps its own columns, filters, rows, actions, permissions and paging.
// Styles: _content/PEMS.UI/pems-list.css. Script: _content/PEMS.UI/pems-list.js. Usage: docs/design/LIST_PAGE_COMPONENT.md.

internal static class PemsList
{
    public const string TextsKey = "PEMS.UI.PemsListTexts";

    public static PemsListTexts Texts(TagHelperContext context)
        => context.Items.TryGetValue(TextsKey, out var t) && t is PemsListTexts texts
            ? texts
            : throw new InvalidOperationException("List-page tags must be inside <pems-list-page texts=\"...\">.");

    public static TagBuilder Icon(string bootstrapIcon)
    {
        var i = new TagBuilder("i");
        i.AddCssClass("bi " + bootstrapIcon);
        i.Attributes["aria-hidden"] = "true";
        return i;
    }

    public static TagBuilder Hidden(string text)
    {
        var s = new TagBuilder("span");
        s.AddCssClass("visually-hidden");
        s.InnerHtml.Append(text);
        return s;
    }

    /// <summary>An icon-only tool button or link, with its label as tool tip and for screen readers.</summary>
    public static TagBuilder IconTool(string tag, string icon, string label)
    {
        var b = new TagBuilder(tag);
        b.AddCssClass("lp-icon-btn");
        if (tag == "button") b.Attributes["type"] = "button";
        b.Attributes["title"] = label;
        b.InnerHtml.AppendHtml(Icon(icon));
        b.InnerHtml.AppendHtml(Hidden(label));
        return b;
    }

    public static void Become(TagHelperOutput output, string tag, string cssClass)
    {
        output.TagName = tag;
        output.TagMode = TagMode.StartTagAndEndTag;
        // a class written on the tag by the page is kept after the component's own classes
        var own = output.Attributes.TryGetAttribute("class", out var existing) ? " " + existing.Value : "";
        output.Attributes.SetAttribute("class", cssClass + own);
    }
}

/// <summary>What the list tag helpers share: render the component for a tag and replace the tag with it.</summary>
internal static class ListRender
{
    public static async Task Replace<TComponent>(IPemsComponentRenderer renderer, TagHelperOutput output, object options, string? inner = null) where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        var parameters = new Dictionary<string, object?> { ["Options"] = options };
        if (inner is not null) parameters["ChildContent"] = BlockTagHelper.Markup(inner);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<TComponent>(parameters));
    }

    /// <summary>The class and the other attributes the page wrote on the tag, kept on the component's element.</summary>
    public static (string? Css, IReadOnlyDictionary<string, object>? Attributes) Page(TagHelperOutput output)
        => (BlockTagHelper.PageClass(output), BlockTagHelper.PageAttributes(output, "class"));

    public static async Task<string> Inner(TagHelperOutput output) => (await output.GetChildContentAsync()).GetContent();

    /// <summary>
    /// The inner markup of a tag a built-in ASP.NET Core helper has already processed: what it put before the content, the content (its own, or the page's
    /// when it set none) and what it put after. The pieces are taken out of the output so the component can write them inside its element.
    /// </summary>
    public static async Task<string> InnerAfterFramework(TagHelperOutput output)
    {
        using var writer = new StringWriter();
        output.PreContent.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        if (output.Content.IsModified) output.Content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        else (await output.GetChildContentAsync()).WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        output.PostContent.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        output.PreContent.Clear();
        output.PostContent.Clear();
        return writer.ToString();
    }
}

/// <summary>Frame of the whole page: fills the window, the table scrolls inside. Sets the texts for every child tag.</summary>
[HtmlTargetElement("pems-list-page")]
[PemsFacade(UiKeys.ListPage)]
public sealed class PemsListPageTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("texts")] public PemsListTexts Texts { get; set; } = default!;

    public override void Init(TagHelperContext context) => context.Items[PemsList.TextsKey] = Texts;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListPage>(renderer, output, new ListFrameOptions { CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The card that holds toolbar, KPI row, chips and grid.</summary>
[HtmlTargetElement("pems-list-panel", ParentTag = "pems-list-page")]
[PemsFacade(UiKeys.ListPanel)]
public sealed class PemsListPanelTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListPanel>(renderer, output, new ListFrameOptions { CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The one toolbar: title and count, then the page's search form and tools (child content).</summary>
[HtmlTargetElement("pems-list-toolbar")]
[PemsFacade(UiKeys.ListToolbar)]
public sealed class PemsListToolbarTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("heading")] public string Heading { get; set; } = "";
    [HtmlAttributeName("heading-title")] public string? HeadingTitle { get; set; }
    [HtmlAttributeName("total")] public int? Total { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListToolbar>(renderer, output,
            new ListToolbarOptions { Heading = Heading, HeadingTitle = HeadingTitle, Total = Total, TotalLabel = texts.Total, CssClass = css, Attributes = attrs },
            await ListRender.Inner(output));
    }
}

/// <summary>
/// The search form that opens over the title. Child content = hidden inputs that keep the active filters.
/// With <c>search-type</c> the input is the shared autocomplete; picking a result opens it, Enter submits the form.
/// </summary>
[HtmlTargetElement("pems-list-search")]
[PemsFacade(UiKeys.ListSearch)]
public sealed class PemsListSearchTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("action")] public string Action { get; set; } = "";
    [HtmlAttributeName("input-id")] public string InputId { get; set; } = "";
    [HtmlAttributeName("name")] public string Name { get; set; } = "search";
    [HtmlAttributeName("value")] public string? Value { get; set; }
    [HtmlAttributeName("placeholder")] public string? Placeholder { get; set; }
    [HtmlAttributeName("search-type")] public string? SearchType { get; set; }
    /// <summary>true: the search filters the rows already on the page as the user types (a short list without server paging); the form is not submitted.</summary>
    [HtmlAttributeName("live-filter")] public bool LiveFilter { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListSearch>(renderer, output, new ListSearchOptions
        {
            Id = Id, Action = Action, InputId = InputId, Name = Name, Value = Value, Placeholder = Placeholder, SearchType = SearchType, LiveFilter = LiveFilter,
            SearchText = texts.Search, SearchCloseText = texts.SearchClose, CssClass = css, Attributes = attrs
        }, await ListRender.Inner(output));
    }
}

/// <summary>Row of icon tools at the end of the toolbar. <c>search-for</c> adds the button that opens the search form.</summary>
[HtmlTargetElement("pems-list-tools")]
[PemsFacade(UiKeys.ListTools)]
public sealed class PemsListToolsTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("search-for")] public string? SearchFor { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListTools>(renderer, output,
            new ListToolsOptions { SearchFor = SearchFor, SearchOpenText = texts.SearchOpen, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// Filters in a dropdown: funnel button with the active-filter count, then a GET form whose fields are the child content,
/// with Reset and Apply. The page decides which filters exist and what "active" means (<c>count</c>).
/// </summary>
[HtmlTargetElement("pems-list-filters")]
[PemsFacade(UiKeys.ListFilters)]
public sealed class PemsListFiltersTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("form-id")] public string FormId { get; set; } = "";
    [HtmlAttributeName("action")] public string Action { get; set; } = "";
    [HtmlAttributeName("count")] public int Count { get; set; }
    [HtmlAttributeName("reset-href")] public string? ResetHref { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListFilters>(renderer, output, new ListFiltersOptions
        {
            FormId = FormId, Action = Action, Count = Count, ResetHref = ResetHref, FiltersText = texts.Filters, ActiveFiltersText = texts.ActiveFilters,
            ResetFiltersText = texts.ResetFilters, ApplyFiltersText = texts.ApplyFilters, CssClass = css, Attributes = attrs
        }, await ListRender.Inner(output));
    }
}

/// <summary>
/// One select inside <c>&lt;pems-list-filters&gt;</c> (W2b): label, an "all" option (empty value) when <c>all-text</c> is set,
/// then <c>items</c>; the item whose value equals <c>value</c> is selected. The page owns the values and their texts.
/// </summary>
[HtmlTargetElement("pems-list-filter-select", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListFilterSelect)]
public sealed class PemsListFilterSelectTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("name")] public string Name { get; set; } = "";
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("value")] public string? Value { get; set; }
    [HtmlAttributeName("all-text")] public string? AllText { get; set; }
    [HtmlAttributeName("items")] public IEnumerable<SelectListItem> Items { get; set; } = Array.Empty<SelectListItem>();

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListFilterSelect>(renderer, output, new ListFilterSelectOptions
        {
            Id = Id, Name = Name, Label = Label, Value = Value, AllText = AllText,
            Items = Items.Select(i => new ListFilterOption(i.Value ?? "", i.Text ?? "")).ToList(),
            Attributes = BlockTagHelper.PageAttributes(output)
        });
}

/// <summary>
/// A toolbar tool that opens a dropdown panel with the page's own content (W2b), for a short form that belongs to the list
/// (for example an upload) without a modal. <c>primary</c> shows the label next to the icon, like the New button.
/// </summary>
[HtmlTargetElement("pems-list-popover")]
[PemsFacade(UiKeys.ListPopover)]
public sealed class PemsListPopoverTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("icon")] public string Icon { get; set; } = "bi-three-dots";
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("primary")] public bool Primary { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListPopover>(renderer, output,
            new ListPopoverOptions { Id = Id, Icon = Icon, Label = Label, Primary = Primary, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>Button that shows/hides the KPI row (<c>target</c> = its id); the choice is kept per browser under <c>storage-key</c>.
/// <c>open</c> (W2b) shows the row until the viewer hides it, for pages whose counts are the point (give the row <c>open</c> too).</summary>
[HtmlTargetElement("pems-list-kpi-toggle", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListKpiToggle)]
public sealed class PemsListKpiToggleTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("target")] public string Target { get; set; } = "";
    [HtmlAttributeName("storage-key")] public string StorageKey { get; set; } = "";
    [HtmlAttributeName("open")] public bool Open { get; set; }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListKpiToggle>(renderer, output, new ListKpiToggleOptions { Target = Target, StorageKey = StorageKey, Open = Open, Label = PemsList.Texts(context).Kpis });
}

/// <summary>Excel export of the filtered list (the page builds the link with its filters). <c>label</c> (W2) replaces the
/// default text when the export is not the filtered list (for example an export of every row).</summary>
[HtmlTargetElement("pems-list-export", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListExport)]
public sealed class PemsListExportTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("href")] public string Href { get; set; } = "";
    [HtmlAttributeName("label")] public string? Label { get; set; }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListExport>(renderer, output, new ListExportOptions { Href = Href, Label = string.IsNullOrEmpty(Label) ? PemsList.Texts(context).Export : Label });
}

/// <summary>Resets the stored column widths of the grid (hidden on phones, where columns are not resized).</summary>
[HtmlTargetElement("pems-list-reset-columns", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListResetColumns)]
public sealed class PemsListResetColumnsTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("grid-key")] public string GridKey { get; set; } = "";

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListResetColumns>(renderer, output, new ListResetColumnsOptions { GridKey = GridKey, Label = PemsList.Texts(context).ResetColumns });
}

/// <summary>The primary "New ..." action; the label collapses to the icon on phones. <c>href</c> opens a page;
/// <c>modal</c> (an element id, W2) opens the page's own Bootstrap modal instead.</summary>
[HtmlTargetElement("pems-list-new", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListNew)]
public sealed class PemsListNewTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("href")] public string Href { get; set; } = "";
    [HtmlAttributeName("modal")] public string? Modal { get; set; }
    [HtmlAttributeName("text")] public string Text { get; set; } = "";

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListNew>(renderer, output, new ListNewOptions { Href = Href, Modal = Modal, Text = Text });
}

/// <summary>The KPI row; hidden until the KPI toggle shows it, or shown first when <c>open</c> (W2b). Child content = the page's KPI chips.</summary>
[HtmlTargetElement("pems-list-kpis")]
[PemsFacade(UiKeys.ListKpis)]
public sealed class PemsListKpisTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("open")] public bool Open { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListKpis>(renderer, output, new ListKpisOptions { Id = Id, Open = Open, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>Active-filter chips; renders nothing when there is no chip. <c>reset-href</c> adds "reset all" from two chips on.</summary>
[HtmlTargetElement("pems-list-chips")]
[PemsFacade(UiKeys.ListChips)]
public sealed class PemsListChipsTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("reset-href")] public string? ResetHref { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var html = await ListRender.Inner(output);
        if (string.IsNullOrWhiteSpace(html)) { output.SuppressOutput(); return; }

        var (css, attrs) = ListRender.Page(output);
        var count = System.Text.RegularExpressions.Regex.Matches(html, "class=\"pems-filter-chip\"").Count;
        await ListRender.Replace<PemsListChips>(renderer, output,
            new ListChipsOptions { ResetHref = ResetHref, Count = count, ActiveFiltersText = texts.ActiveFilters, ResetAllText = texts.ResetAll, CssClass = css, Attributes = attrs }, html);
    }
}

/// <summary>One active filter: "label: value" with a link that removes it.</summary>
[HtmlTargetElement("pems-list-chip", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.ListChip)]
public sealed class PemsListChipTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("value")] public string? Value { get; set; }
    [HtmlAttributeName("remove-href")] public string RemoveHref { get; set; } = "";

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsListChip>(renderer, output, new ListChipOptions { Label = Label, Value = Value, RemoveHref = RemoveHref, RemoveText = PemsList.Texts(context).RemoveFilter });
}

/// <summary>Holds the scrolling table and the footer.</summary>
[HtmlTargetElement("pems-list-grid")]
[PemsFacade(UiKeys.ListGrid)]
public sealed class PemsListGridTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListGrid>(renderer, output, new ListFrameOptions { CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// The table, scrolling inside the frame with a sticky header. Child content = thead and tbody. <c>grid-key</c> turns on the stored
/// column widths (pems-list.js). With <c>empty="true"</c> the empty state follows the table; when the list is <c>filtered</c> it also
/// says so and offers "clear all" (<c>clear-href</c>, the unfiltered list).
/// </summary>
[HtmlTargetElement("pems-list-table")]
[PemsFacade(UiKeys.ListTable)]
public sealed class PemsListTableTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("grid-key")] public string? GridKey { get; set; }
    [HtmlAttributeName("empty")] public bool Empty { get; set; }
    [HtmlAttributeName("empty-text")] public string? EmptyText { get; set; }
    [HtmlAttributeName("filtered")] public bool Filtered { get; set; }
    [HtmlAttributeName("clear-href")] public string? ClearHref { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var texts = PemsList.Texts(context);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListTable>(renderer, output, new ListTableOptions
        {
            GridKey = GridKey, Empty = Empty, EmptyText = EmptyText, Filtered = Filtered, ClearHref = ClearHref,
            NoResultsHint = texts.NoResultsHint, ClearAllText = texts.ClearAll, CssClass = css, Attributes = attrs
        }, await ListRender.Inner(output));
    }
}

/// <summary>Footer under the table (the pager).</summary>
[HtmlTargetElement("pems-list-footer")]
[PemsFacade(UiKeys.ListFooter)]
public sealed class PemsListFooterTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListFooter>(renderer, output, new ListFrameOptions { CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// Bulk-action bar (SCRUM-337 W1): hidden until at least one row is ticked. Rows tick with <c>input[data-lp-select]</c> (value = record
/// id), the header with <c>input[data-lp-select-all]</c>. Child content = the page's bulk actions; every <c>form</c> inside receives the
/// ticked ids as hidden inputs named <c>name</c> when it is submitted. <c>count-text</c> is the localised count, "{0}" = number ticked.
/// </summary>
[HtmlTargetElement("pems-list-bulk")]
[PemsFacade(UiKeys.ListBulk)]
public sealed class PemsListBulkTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("count-text")] public string CountText { get; set; } = "{0}";
    [HtmlAttributeName("name")] public string Name { get; set; } = "ids";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListBulk>(renderer, output, new ListBulkOptions { CountText = CountText, Name = Name, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A toolbar tool that opens a dropdown of the page's own items (SCRUM-337 W1), e.g. quick filters and saved views. Child content =
/// the <c>li</c> items (<c>dropdown-item</c>, <c>dropdown-header</c>, <c>dropdown-divider</c>). <c>icon</c> is a Bootstrap icon,
/// <c>label</c> the localised tool tip and accessible name.
/// </summary>
[HtmlTargetElement("pems-list-menu")]
[PemsFacade(UiKeys.ListMenu)]
public sealed class PemsListMenuTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("icon")] public string Icon { get; set; } = "bi-three-dots";
    [HtmlAttributeName("label")] public string Label { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsListMenu>(renderer, output, new ListMenuOptions { Id = Id, Icon = Icon, Label = Label, CssClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }
}
