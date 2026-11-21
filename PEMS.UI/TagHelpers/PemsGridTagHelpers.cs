using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// A grid row: <c>&lt;pems-row gap="2" css="mb-3"&gt;&lt;pems-col md="6"&gt;...&lt;/pems-col&gt;&lt;/pems-row&gt;</c> writes <c>&lt;div class="row g-2 mb-3"&gt;</c>.
/// <c>gap</c>, <c>gap-x</c>, <c>gap-y</c> are the gutters (0 to 5). Every other attribute (id, data-*) is kept.
/// </summary>
[HtmlTargetElement("pems-row")]
[PemsFacade(UiKeys.Row)]
public sealed class PemsRowTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("gap")] public string? Gap { get; set; }
    [HtmlAttributeName("gap-x")] public string? GapX { get; set; }
    [HtmlAttributeName("gap-y")] public string? GapY { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsRow>(renderer, output, new RowOptions { Gap = Gap, GapX = GapX, GapY = GapY, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A grid column: <c>&lt;pems-col span="12" md="6" css="mb-3"&gt;</c> writes <c>&lt;div class="col-12 col-md-6 mb-3"&gt;</c>. Each breakpoint (<c>span</c> = all sizes,
/// <c>sm</c>, <c>md</c>, <c>lg</c>, <c>xl</c>, <c>xxl</c>) takes a width 1 to 12, <c>auto</c>, or an empty value for an equal-width column.
/// </summary>
[HtmlTargetElement("pems-col")]
[PemsFacade(UiKeys.Col)]
public sealed class PemsColTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("span")] public string? Span { get; set; }
    [HtmlAttributeName("sm")] public string? Sm { get; set; }
    [HtmlAttributeName("md")] public string? Md { get; set; }
    [HtmlAttributeName("lg")] public string? Lg { get; set; }
    [HtmlAttributeName("xl")] public string? Xl { get; set; }
    [HtmlAttributeName("xxl")] public string? Xxl { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsCol>(renderer, output,
            new ColOptions { Span = Span, Sm = Sm, Md = Md, Lg = Lg, Xl = Xl, Xxl = Xxl, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The buttons at the end of a form: <c>&lt;pems-action-bar css="mt-4"&gt;&lt;pems-button .../&gt;&lt;pems-button .../&gt;&lt;/pems-action-bar&gt;</c> writes <c>&lt;div class="mt-4"&gt;</c> around them (SCRUM-488).</summary>
[HtmlTargetElement("pems-action-bar")]
[PemsFacade(UiKeys.ActionBar)]
public sealed class PemsActionBarTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsActionBar>(renderer, output, new ActionBarOptions { Plain = true, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A field that picks one node of a large tree one level at a time (SCRUM-513): <c>&lt;pems-tree-select name="FunctionalLocationId" value="@Model.FunctionalLocationId" selected-text="@Model.FunctionalLocationName"
/// url="@Url.Action("TreeNodes", "FunctionalLocation")" placeholder="@Localizer["Select"]" search-label="@Localizer["Search"]" /&gt;</c>. The value is posted as a hidden input with the name.
/// </summary>
[HtmlTargetElement("pems-tree-select")]
[PemsFacade(UiKeys.TreeSelect)]
public sealed class PemsTreeSelectTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("name")] public string Name { get; set; } = "";
    [HtmlAttributeName("id")] public string? Id { get; set; }
    [HtmlAttributeName("value")] public string? Value { get; set; }
    [HtmlAttributeName("selected-text")] public string? SelectedText { get; set; }
    [HtmlAttributeName("url")] public string Url { get; set; } = "";
    [HtmlAttributeName("placeholder")] public string? Placeholder { get; set; }
    [HtmlAttributeName("search-label")] public string? SearchLabel { get; set; }
    [HtmlAttributeName("loading-text")] public string? LoadingText { get; set; }
    [HtmlAttributeName("no-results-text")] public string? NoResultsText { get; set; }
    [HtmlAttributeName("more-text")] public string? MoreText { get; set; }
    [HtmlAttributeName("clear-text")] public string? ClearText { get; set; }
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }
    [HtmlAttributeName("required")] public bool Required { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTreeSelect>(renderer, output, new TreeSelectOptions
        {
            Name = Name, Id = Id, Value = Value, SelectedText = SelectedText, Url = Url, Placeholder = Placeholder, SearchLabel = SearchLabel, LoadingText = LoadingText,
            NoResultsText = NoResultsText, MoreText = MoreText, ClearText = ClearText, Disabled = Disabled, Required = Required, Css = Css, PageClass = pageClass, Attributes = attrs
        });
    }
}

/// <summary>
/// The enterprise data grid (SCRUM-54): <c>&lt;pems-data-grid options="@gridOptions" /&gt;</c>. The options say the endpoint, the columns and every text; the rows are read page by page and only the
/// rows in view are written. The server side is <c>GridRequest</c> and <c>GridQueryExtensions</c> in PEMS.Application.
/// </summary>
[HtmlTargetElement("pems-data-grid")]
[PemsFacade(UiKeys.DataGrid)]
public sealed class PemsDataGridTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public DataGridOptions? Options { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-data-grid needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsDataGrid>(renderer, output, Options with { PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>
/// The view types of a record set (SCRUM-55): <c>&lt;pems-data-view options="@viewOptions" /&gt;</c> with <c>View</c> = cards, kanban, calendar (Jalali and Gregorian) or timeline. They read the endpoint of the
/// data grid and are configured by the field names of the entity.
/// </summary>
[HtmlTargetElement("pems-data-view")]
[PemsFacade(UiKeys.DataView)]
public sealed class PemsDataViewTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public DataViewOptions? Options { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-data-view needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsDataView>(renderer, output, Options with { PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>One label, control and validation message of a form: <c>&lt;pems-form-group css="mb-3"&gt;&lt;pems-label .../&gt;&lt;pems-input .../&gt;&lt;span asp-validation-for="X"&gt;&lt;/span&gt;&lt;/pems-form-group&gt;</c> writes <c>&lt;div class="mb-3"&gt;</c> around them (SCRUM-501).</summary>
[HtmlTargetElement("pems-form-group")]
[PemsFacade(UiKeys.FormGroup)]
public sealed class PemsFormGroupTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsFormGroup>(renderer, output, new FormGroupOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A check box / radio / switch with its label: <c>&lt;pems-check-row switch css="mb-0"&gt;&lt;pems-input kind="check" .../&gt;&lt;label class="form-check-label"&gt;...&lt;/label&gt;&lt;/pems-check-row&gt;</c>.</summary>
[HtmlTargetElement("pems-check-row")]
[PemsFacade(UiKeys.CheckRow)]
public sealed class PemsCheckRowTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("switch")] public bool Switch { get; set; }
    [HtmlAttributeName("inline")] public bool Inline { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsCheckRow>(renderer, output, new CheckRowOptions { Switch = Switch, Inline = Inline, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>Page-number links: <c>&lt;pems-pager current="2" total="5" href="/items?page={page}" scale="small" css="mb-0" /&gt;</c> (<c>{page}</c> is replaced by each page number).</summary>
[HtmlTargetElement("pems-pager")]
[PemsFacade(UiKeys.Pager)]
public sealed class PemsPagerTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("current")] public int Current { get; set; } = 1;
    [HtmlAttributeName("total")] public int Total { get; set; } = 1;
    [HtmlAttributeName("href")] public string Href { get; set; } = "?page={page}";
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsPager>(renderer, output, new PagerOptions { Current = Current, Total = Total, Href = Href, Scale = Scale, Css = Css, PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>The white content panel of a page: <c>&lt;pems-content-card css="mb-3"&gt;...&lt;/pems-content-card&gt;</c> writes <c>&lt;div class="content-card mb-3"&gt;</c>.</summary>
[HtmlTargetElement("pems-content-card")]
[PemsFacade(UiKeys.ContentCard)]
public sealed class PemsContentCardTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>div (default), section, article or details.</summary>
    [HtmlAttributeName("as")] public string As { get; set; } = "div";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsContentCard>(renderer, output, new ContentCardOptions { As = As, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The outer wrapper of a page: <c>&lt;pems-page-shell kind="admin"&gt;</c> writes <c>&lt;div class="admin-page-shell"&gt;</c>, <c>kind="workspace"</c> (default) <c>&lt;div class="vs-page"&gt;</c>.</summary>
[HtmlTargetElement("pems-page-shell")]
[PemsFacade(UiKeys.PageShell)]
public sealed class PemsPageShellTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("kind")] public string Kind { get; set; } = "workspace";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsPageShell>(renderer, output, new PageShellOptions { Kind = Kind, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A flex row or column: <c>&lt;pems-flex justify="between" align="center" wrap="true" gap="2" css="mb-3"&gt;</c> writes
/// <c>&lt;div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3"&gt;</c>. <c>dir="column"</c> stacks; <c>as</c> is div (default), span, li, header, footer, section, h5 or h6;
/// <c>inline="true"</c> writes <c>d-inline-flex</c>. Every other attribute (id, data-*) is kept.
/// </summary>
[HtmlTargetElement("pems-flex")]
[PemsFacade(UiKeys.Flex)]
public sealed class PemsFlexTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("as")] public string As { get; set; } = "div";
    [HtmlAttributeName("inline")] public bool Inline { get; set; }
    [HtmlAttributeName("dir")] public string? Dir { get; set; }
    [HtmlAttributeName("justify")] public string? Justify { get; set; }
    [HtmlAttributeName("align")] public string? Align { get; set; }
    [HtmlAttributeName("wrap")] public bool Wrap { get; set; }
    [HtmlAttributeName("gap")] public string? Gap { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsFlex>(renderer, output, new FlexOptions { As = As, Inline = Inline, Dir = Dir, Justify = Justify, Align = Align, Wrap = Wrap, Gap = Gap, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The muted help line under a control: <c>&lt;pems-help-text&gt;text&lt;/pems-help-text&gt;</c> writes <c>&lt;div class="form-text"&gt;</c> (<c>as="small"</c> or <c>as="span"</c> for inline help).</summary>
[HtmlTargetElement("pems-help-text")]
[PemsFacade(UiKeys.HelpText)]
public sealed class PemsHelpTextTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("as")] public string As { get; set; } = "div";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsHelpText>(renderer, output, new HelpTextOptions { As = As, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The scroll wrapper of a wide table: <c>&lt;pems-table-scroll&gt;&lt;pems-table&gt;...&lt;/pems-table&gt;&lt;/pems-table-scroll&gt;</c> writes <c>&lt;div class="table-responsive"&gt;</c>.</summary>
[HtmlTargetElement("pems-table-scroll")]
[PemsFacade(UiKeys.TableScroll)]
public sealed class PemsTableScrollTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTableScroll>(renderer, output, new TableScrollOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The container of the panes of a tab strip: <c>&lt;pems-tab-content&gt;&lt;pems-tab-pane id="a" active="true" fade="true"&gt;...&lt;/pems-tab-pane&gt;&lt;/pems-tab-content&gt;</c> writes <c>&lt;div class="tab-content"&gt;</c>.</summary>
[HtmlTargetElement("pems-tab-content")]
[PemsFacade(UiKeys.TabContent)]
public sealed class PemsTabContentTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTabContent>(renderer, output, new TabContentOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>One tab pane: <c>&lt;pems-tab-pane id="info" fade="true" active="true"&gt;</c> writes <c>&lt;div class="tab-pane fade show active" id="info"&gt;</c>; <c>role</c>, <c>aria-labelledby</c> and every other attribute are kept.</summary>
[HtmlTargetElement("pems-tab-pane")]
[PemsFacade(UiKeys.TabPane)]
public sealed class PemsTabPaneTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("fade")] public bool Fade { get; set; }
    [HtmlAttributeName("active")] public bool Active { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTabPane>(renderer, output, new TabPaneOptions { Fade = Fade, Active = Active, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>One tab of a strip: <c>&lt;pems-tab target="tab-info" active="true"&gt;&lt;pems-icon .../&gt; Info&lt;/pems-tab&gt;</c> writes <c>&lt;li class="nav-item" role="presentation"&gt;&lt;button class="nav-link active" data-bs-toggle="tab" data-bs-target="#tab-info" type="button"&gt;...&lt;/button&gt;&lt;/li&gt;</c>; every other attribute (id, data-*) goes on the button.</summary>
[HtmlTargetElement("pems-tab")]
[PemsFacade(UiKeys.Tab)]
public sealed class PemsTabTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("target")] public string Target { get; set; } = "";
    [HtmlAttributeName("active")] public bool Active { get; set; }
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsTab>(renderer, output, new TabOptions { Target = Target, Active = Active, Disabled = Disabled, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A labelled number tile: <c>&lt;pems-stat label="Modules scanned"&gt;@Model.Count&lt;/pems-stat&gt;</c> writes <c>&lt;div class="admin-kpi"&gt;&lt;div class="admin-kpi-label"&gt;...&lt;/div&gt;&lt;div class="admin-kpi-value"&gt;...&lt;/div&gt;&lt;/div&gt;</c>.</summary>
[HtmlTargetElement("pems-stat")]
[PemsFacade(UiKeys.Stat)]
public sealed class PemsStatTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsStat>(renderer, output, new StatOptions { Label = Label, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The grid of stat tiles: <c>&lt;pems-stat-grid&gt;&lt;pems-stat .../&gt;...&lt;/pems-stat-grid&gt;</c> writes <c>&lt;div class="admin-kpi-grid"&gt;</c>.</summary>
[HtmlTargetElement("pems-stat-grid")]
[PemsFacade(UiKeys.StatGrid)]
public sealed class PemsStatGridTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsStatGrid>(renderer, output, new StatGridOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A big number: <c>&lt;pems-metric css="text-success"&gt;42&lt;/pems-metric&gt;</c> writes <c>&lt;div class="fs-4 fw-bold text-success"&gt;42&lt;/div&gt;</c>.</summary>
[HtmlTargetElement("pems-metric")]
[PemsFacade(UiKeys.Metric)]
public sealed class PemsMetricTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsMetric>(renderer, output, new MetricOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The intro banner under a page title: <c>&lt;pems-page-hero&gt;text and actions&lt;/pems-page-hero&gt;</c> writes <c>&lt;div class="admin-hero"&gt;</c>.</summary>
[HtmlTargetElement("pems-page-hero")]
[PemsFacade(UiKeys.PageHero)]
public sealed class PemsPageHeroTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsPageHero>(renderer, output, new PageHeroOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A table of typed columns: <c>&lt;pems-data-table items="@Model.History" columns="@cols" empty="@Localizer["No history"]" dense="true" /&gt;</c> where <c>cols</c> is built with
/// <c>DataColumn.Of&lt;Row&gt;(header, row =&gt; value, kind)</c>. The page supplies every text; the table writes <c>table table-enterprise</c>.
/// </summary>
[HtmlTargetElement("pems-data-table")]
[PemsFacade(UiKeys.DataTable)]
public sealed class PemsDataTableTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("items")] public System.Collections.IEnumerable? Items { get; set; }
    [HtmlAttributeName("columns")] public IReadOnlyList<DataColumn>? Columns { get; set; }
    /// <summary>The rows and the columns together (<c>DataRows.Of(items, c =&gt; c.Add(...))</c>); used instead of <c>items</c> and <c>columns</c>.</summary>
    [HtmlAttributeName("data")] public DataRows? Data { get; set; }
    [HtmlAttributeName("empty")] public string? Empty { get; set; }
    [HtmlAttributeName("dense")] public bool Dense { get; set; }
    /// <summary>The enterprise look (default true); <c>enterprise="false"</c> writes the plain table.</summary>
    [HtmlAttributeName("enterprise")] public bool Enterprise { get; set; } = true;
    [HtmlAttributeName("row-css")] public string? RowCss { get; set; }
    /// <summary>False writes no header row (a table of key and value rows); default true.</summary>
    [HtmlAttributeName("header")] public bool Header { get; set; } = true;
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsDataTable>(renderer, output, new DataTableOptions
        {
            Items = Data?.Items ?? Items?.Cast<object>(), Columns = Data?.Columns ?? Columns, Empty = Empty, Dense = Dense, Enterprise = Enterprise, RowCss = RowCss, RowClass = Data?.RowClass, RowAttrs = Data?.RowAttrs, ShowHeader = Header,
            Css = string.IsNullOrWhiteSpace(pageClass) ? Css : string.Join(" ", new[] { Css, pageClass }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Attributes = attrs
        });
    }
}

/// <summary>The history of a workflow instance as a timeline (SCRUM-58): <c>&lt;pems-workflow-history options="@history" /&gt;</c>, newest first; the entries come localized from the page or from <c>WorkflowComponents</c>.</summary>
[HtmlTargetElement("pems-workflow-history")]
[PemsFacade(UiKeys.WorkflowHistory)]
public sealed class PemsWorkflowHistoryTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public WorkflowHistoryOptions? Options { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-workflow-history needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsWorkflowHistory>(renderer, output, Options with { PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>The workflow action bar (SCRUM-58): <c>&lt;pems-workflow-action-bar options="@bar" /&gt;</c>, one comment box and a button per action the engine offers; build the actions with <c>WorkflowComponents.Actions</c>.</summary>
[HtmlTargetElement("pems-workflow-action-bar")]
[PemsFacade(UiKeys.WorkflowActionBar)]
public sealed class PemsWorkflowActionBarTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public WorkflowActionBarOptions? Options { get; set; }
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-workflow-action-bar needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsWorkflowActionBar>(renderer, output, Options with { Antiforgery = Options.Antiforgery ?? PemsRelated.Antiforgery(ViewContext), PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>
/// The uploader (SCRUM-580): <c>&lt;pems-uploader name="file" accept=".xlsx" required /&gt;</c> writes a real file input that posts with the form, plus (with scripting) drag and drop, the list of chosen files and the
/// size and type check. The size limit is the host's upload setting unless <c>max-mb</c> is given; every other attribute (<c>aria-label</c>, <c>form</c>) is kept on the input.
/// </summary>
[HtmlTargetElement("pems-uploader")]
[PemsFacade(UiKeys.Uploader)]
public sealed class PemsUploaderTagHelper(IPemsComponentRenderer renderer, IServiceProvider services) : TagHelper
{
    [HtmlAttributeName("name")] public string Name { get; set; } = "file";
    [HtmlAttributeName("id")] public string? Id { get; set; }
    [HtmlAttributeName("accept")] public string? Accept { get; set; }
    [HtmlAttributeName("multiple")] public bool Multiple { get; set; }
    [HtmlAttributeName("required")] public bool Required { get; set; }
    [HtmlAttributeName("scale")] public string? Scale { get; set; }
    [HtmlAttributeName("max-mb")] public int? MaxMb { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    [HtmlAttributeName("texts")] public UploaderTexts? Texts { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        var max = MaxMb ?? (services.GetService(typeof(IUploadLimits)) as IUploadLimits)?.MaxFileSizeMb;
        await ListRender.Replace<PemsUploader>(renderer, output, new UploaderOptions { Name = Name, Id = Id, Accept = Accept, Multiple = Multiple, Required = Required, Scale = Scale, MaxMb = max, Texts = Texts, Css = Css, PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>A button bound to a stored action definition (SCRUM-437): <c>&lt;pems-action-button options="@button" /&gt;</c> posts the values to <c>/a/{key}</c>; the antiforgery token is taken from the request.</summary>
[HtmlTargetElement("pems-action-button")]
[PemsFacade(UiKeys.ActionButton)]
public sealed class PemsActionButtonTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public ActionButtonOptions? Options { get; set; }
    [ViewContext, HtmlAttributeNotBound] public ViewContext? ViewContext { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-action-button needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsActionButton>(renderer, output, Options with { Antiforgery = Options.Antiforgery ?? PemsRelated.Antiforgery(ViewContext), PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>SCRUM-339: <c>&lt;pems-comments-thread options="@opts" /&gt;</c> draws the CommentsThread of a record from localized entries the page (or a hydrator) gives; the data stays in its own module.</summary>
[HtmlTargetElement("pems-comments-thread")]
[PemsFacade(UiKeys.CommentsThread)]
public sealed class PemsCommentsThreadTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public CommentsThreadOptions? Options { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-comments-thread needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsCommentsThread>(renderer, output, Options with { PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>SCRUM-339: <c>&lt;pems-audit-trail options="@opts" /&gt;</c> draws the AuditTrail of a record from localized entries the page (or a hydrator) gives; the data stays in its own module.</summary>
[HtmlTargetElement("pems-audit-trail")]
[PemsFacade(UiKeys.AuditTrail)]
public sealed class PemsAuditTrailTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("options")] public AuditTrailOptions? Options { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Options is null) throw new InvalidOperationException("pems-audit-trail needs the options attribute.");
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsAuditTrail>(renderer, output, Options with { PageClass = pageClass, Attributes = attrs });
    }
}
