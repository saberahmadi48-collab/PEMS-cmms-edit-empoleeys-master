using System.Text.Json.Serialization;
using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-339 (slice 6): the typed-column data table. The page owns every text (headers, empty text, cell values already localized, ADR-0008) and builds the columns in C#;
// the component owns the table markup, the cell kinds and the empty row. Audit trails, approval histories and comment lists are configurations of it.

/// <summary>How a cell shows its value.</summary>
public enum DataCellKind
{
    /// <summary>The value as text.</summary>
    Text,
    /// <summary>Muted small text (a reason, a note).</summary>
    Muted,
    /// <summary>A date and time, written yyyy-MM-dd HH:mm (current culture, like <c>ToString("yyyy-MM-dd HH:mm")</c> in a view) in a left-to-right run; the value is a DateTime or DateTimeOffset.</summary>
    DateTime,
    /// <summary>A date, written yyyy-MM-dd (current culture) in a left-to-right run.</summary>
    Date,
    /// <summary>A status pill: the text with the tone the column's <see cref="DataColumn.Tone"/> picks.</summary>
    Badge,
    /// <summary>Preformatted text (a list of changed fields, a diff).</summary>
    Pre,
    /// <summary>Code or an identifier (<c>&lt;code&gt;</c>).</summary>
    Code,
}

/// <summary>One column of <see cref="PemsDataTable"/>: the header text, how to read the value from a row, how to show it. Build it with <see cref="Of{T}"/>.</summary>
public sealed record DataColumn
{
    public required string Header { get; init; }
    public required Func<object, object?> Value { get; init; }
    public DataCellKind Kind { get; init; } = DataCellKind.Text;
    /// <summary>Extra classes on the cells (small, text-nowrap, text-end ...).</summary>
    public string? Css { get; init; }
    /// <summary>Extra classes on the header cell; null means the header has none.</summary>
    public string? HeaderCss { get; init; }
    /// <summary>Wraps the value in one inline element (span, strong, small, em, bdi); null writes the value as is.</summary>
    public CellElement? Element { get; init; }
    /// <summary>Classes on the cell computed from the row (a Razor class expression); added after <see cref="Css"/>.</summary>
    public Func<object, string?>? CssFor { get; init; }
    /// <summary>For <see cref="DataCellKind.Badge"/>: the tone name for a row (neutral, primary, success, danger, warning, info, light, dark); null shows neutral.</summary>
    public Func<object, string?>? Tone { get; init; }
    /// <summary>Text direction of the cell (<c>ltr</c> for codes, routes and numbers); null writes none.</summary>
    public string? Dir { get; init; }
    /// <summary>The cell's <c>title</c> (tooltip) computed from the row; null or empty writes none.</summary>
    public Func<object, string?>? TitleFor { get; init; }

    /// <summary>A typed column: <c>DataColumn.Of&lt;History&gt;(Localizer["Date"].Value, h =&gt; h.ActionDate, DataCellKind.DateTime)</c>.</summary>
    public static DataColumn Of<T>(string header, Func<T, object?> value, DataCellKind kind = DataCellKind.Text, string? css = null, Func<T, string?>? tone = null, string? headerCss = null, CellElement? element = null, Func<T, string?>? cssFor = null, string? dir = null, Func<T, string?>? titleFor = null) => new()
    {
        Header = header,
        Value = row => value((T)row),
        Kind = kind,
        Css = css,
        HeaderCss = headerCss,
        Element = element,
        CssFor = cssFor is null ? null : row => cssFor((T)row),
        Tone = tone is null ? null : row => tone((T)row),
        Dir = dir,
        TitleFor = titleFor is null ? null : row => titleFor((T)row),
    };
}

/// <summary>An inline element around a cell value: <c>new CellElement("span", "small text-muted")</c>.</summary>
public sealed record CellElement(string Tag, string? Css = null, string? Dir = null);

/// <summary>One piece of a cell built in C#: text, a span, an icon, a badge, a link or a group. The table writes them with the icon and badge components, so the skin is respected.</summary>
public abstract record CellNode;
public sealed record CellText(string Text) : CellNode;
public sealed record CellGroup(IReadOnlyList<CellNode> Children) : CellNode;
public sealed record CellSpan(string? Css, IReadOnlyList<CellNode> Children, string? Title = null, string? Dir = null, string Tag = "span") : CellNode;
/// <summary>A button-styled link (<c>pems-button as="a"</c>) or a button element; <see cref="Attrs"/> are extra attributes (title, aria-label, target).</summary>
public sealed record CellButton(string? Href, string Tone, string Size, bool Outline, string? Css, bool Plain, IReadOnlyList<CellNode> Children, IReadOnlyDictionary<string, string>? Attrs = null) : CellNode;
public sealed record CellIcon(string Name, string? Css = null) : CellNode;
public sealed record CellBadge(string? Tone, string? Css, string Text) : CellNode;
public sealed record CellLink(string? Href, string? Css, IReadOnlyList<CellNode> Children) : CellNode;
public sealed record CellCode(IReadOnlyList<CellNode> Children) : CellNode;

/// <summary>Builds a cell the way Razor writes mixed content: markup parts as they are, everything else HTML-encoded, <c>null</c> as nothing.</summary>
public static class DataCell
{
    /// <summary>The parts of a cell: a <see cref="CellNode"/> stays, a string or any other value becomes text (encoded when written), null is nothing.</summary>
    public static IReadOnlyList<CellNode> Parts(params object?[] parts)
    {
        var list = new List<CellNode>();
        foreach (var p in parts)
        {
            switch (p)
            {
                case null: break;
                case CellNode n: list.Add(n); break;
                default: list.Add(new CellText(p.ToString() ?? "")); break;
            }
        }
        return list;
    }

    /// <summary>A cell written as Razor markup (forms, inputs, any tag helper): <c>var act = DataCell.Template(rows, @&lt;text&gt;...@item...&lt;/text&gt;);</c> then <c>.Add(header, r =&gt; (object?)act(r))</c>. The rows are passed so the template knows the row type (SCRUM-570).</summary>
    public static Func<T, Microsoft.AspNetCore.Html.IHtmlContent> Template<T>(IEnumerable<T> rows, Func<T, Microsoft.AspNetCore.Html.IHtmlContent> template) => template;

    /// <summary>Several parts in one cell: <c>DataCell.Group(a.Module, " - ", a.RequestNumber)</c>.</summary>
    public static CellNode Group(params object?[] parts) => new CellGroup(Parts(parts));
    /// <summary>A span: <c>DataCell.Span("text-danger", DataCell.Icon("bi-x"), " ", Localizer["Yes"])</c>; <see cref="WithTitle"/> and <see cref="WithDir"/> add the attributes.</summary>
    public static CellSpan Span(string? css, params object?[] parts) => new(css, Parts(parts));
    public static CellSpan WithTitle(this CellSpan span, string? title) => span with { Title = title };
    public static CellSpan WithDir(this CellSpan span, string? dir) => span with { Dir = dir };
    /// <summary>Another element than span: strong, small, em, bdi, pre or div.</summary>
    public static CellSpan WithTag(this CellSpan span, string tag) => span with { Tag = tag is "strong" or "small" or "em" or "bdi" or "pre" or "div" ? tag : "span" };
    /// <summary>A button-styled link: <c>DataCell.ButtonLink(Url.Action("Details", new { id = r.Id }), "primary", "small", true, null, Localizer["Open"])</c> (tone as on pems-button, "none" when css carries the colour).</summary>
    public static CellButton ButtonLink(string? href, string tone, string size, bool outline, string? css, params object?[] parts) => new(href, tone, size, outline, css, false, Parts(parts));
    /// <summary>A link with only the page's classes (no btn classes), as <c>pems-button plain="true" as="a"</c>.</summary>
    public static CellButton PlainLink(string? href, string? css, params object?[] parts) => new(href, "primary", "medium", false, css, true, Parts(parts));
    /// <summary>An extra attribute on a button link (title, aria-label, target, rel).</summary>
    public static CellButton WithAttr(this CellButton button, string name, string? value) => button with { Attrs = new Dictionary<string, string>(button.Attrs ?? new Dictionary<string, string>()) { [name] = value ?? "" } };
    /// <summary>An icon (<c>bi bi-name</c>) with optional extra classes.</summary>
    public static CellNode Icon(string name, string? css = null) => new CellIcon(name, css);
    /// <summary>A badge: tone neutral, primary, danger, success, warning, info, light or dark, or "none" when <paramref name="css"/> carries the colour classes.</summary>
    public static CellNode Badge(string? tone, object? text, string? css = null) => new CellBadge(tone, css, text?.ToString() ?? "");
    /// <summary>A link: <c>DataCell.Link(Url.Action("Edit", new { id = r.Id }), r.Name)</c> (the page computes the href, as the anchor tag helper does in a hand-written row).</summary>
    public static CellNode Link(string? href, params object?[] parts) => new CellLink(href, null, Parts(parts));
    public static CellNode LinkClass(string? href, string? css, params object?[] parts) => new CellLink(href, css, Parts(parts));
    /// <summary><c>&lt;code&gt;</c> around the parts.</summary>
    public static CellNode Code(params object?[] parts) => new CellCode(Parts(parts));

    public static Microsoft.AspNetCore.Html.IHtmlContent Join(params object?[] parts)
    {
        var b = new Microsoft.AspNetCore.Html.HtmlContentBuilder();
        foreach (var p in parts)
        {
            switch (p)
            {
                case null: break;
                case Microsoft.AspNetCore.Html.IHtmlContent h: b.AppendHtml(h); break;
                default: b.Append(p.ToString() ?? ""); break;
            }
        }
        return b;
    }
}

/// <summary>Inputs of <see cref="PemsDataTable"/>: the rows and the columns come from the page (code, not JSON); <see cref="Empty"/> shows in a single row when there are no rows.</summary>
public sealed record DataTableOptions : FrameOptions
{
    [JsonIgnore] public IEnumerable<object>? Items { get; init; }
    [JsonIgnore] public IReadOnlyList<DataColumn>? Columns { get; init; }
    /// <summary>The text of the empty row (already localized); empty writes no row.</summary>
    public string? Empty { get; init; }
    /// <summary>Compact rows (<c>table-sm</c>).</summary>
    public bool Dense { get; init; }
    /// <summary>The enterprise look (<c>table-enterprise</c>, default); false writes the plain Bootstrap table.</summary>
    public bool Enterprise { get; init; } = true;
    /// <summary>Classes on every body row (<c>tr</c>).</summary>
    public string? RowCss { get; init; }
    /// <summary>Classes on a body row computed from the row (a Razor class expression); added after <see cref="RowCss"/>.</summary>
    [JsonIgnore] public Func<object, string?>? RowClass { get; init; }
    /// <summary>Extra attributes on a body row computed from the row (<c>data-*</c>, <c>id</c>, <c>title</c>); the row's class stays <see cref="RowClass"/>.</summary>
    [JsonIgnore] public Func<object, IReadOnlyDictionary<string, object>?>? RowAttrs { get; init; }
    /// <summary>False writes no <c>thead</c> (a key-value or top-N table that has no header row); default true.</summary>
    public bool ShowHeader { get; init; } = true;
    public string? Css { get; init; }
}

/// <summary>The rows and the columns of one table, built together so the row type is inferred once: <c>DataRows.Of(Model.History, c =&gt; c.Add("Date", h =&gt; h.When).Add("By", h =&gt; h.Who))</c>.</summary>
public sealed class DataRows
{
    public required IEnumerable<object> Items { get; init; }
    public required IReadOnlyList<DataColumn> Columns { get; init; }

    /// <summary>Classes on a body row computed from the row.</summary>
    public Func<object, string?>? RowClass { get; init; }

    /// <summary>Attributes of a body row computed from the row (see <see cref="DataTableOptions.RowAttrs"/>).</summary>
    public Func<object, IReadOnlyDictionary<string, object>?>? RowAttrs { get; init; }

    public static DataRows Of<T>(IEnumerable<T> items, Func<DataColumns<T>, DataColumns<T>> columns, Func<T, string?>? rowClass = null, Func<T, IReadOnlyDictionary<string, object>?>? rowAttrs = null) where T : notnull => new()
    {
        Items = items.Cast<object>(),
        Columns = columns(new DataColumns<T>()),
        RowClass = rowClass is null ? null : row => rowClass((T)row),
        RowAttrs = rowAttrs is null ? null : row => rowAttrs((T)row)
    };
}

/// <summary>Starts a column list whose row type is inferred from the items: <c>DataColumns.For(history).Add("Date", h =&gt; h.When, DataCellKind.DateTime).Add("By", h =&gt; h.Who)</c>.</summary>
public static class DataColumns
{
    public static DataColumns<T> For<T>(IEnumerable<T> items) => new();
}

/// <summary>A list of <see cref="DataColumn"/> for rows of type <typeparamref name="T"/>; converts to the list the table takes.</summary>
public sealed class DataColumns<T> : IReadOnlyList<DataColumn>
{
    private readonly List<DataColumn> _columns = [];

    public DataColumns<T> Add(string header, Func<T, object?> value, DataCellKind kind = DataCellKind.Text, string? css = null, Func<T, string?>? tone = null, string? headerCss = null, CellElement? element = null, Func<T, string?>? cssFor = null, string? dir = null, Func<T, string?>? titleFor = null)
    {
        _columns.Add(DataColumn.Of(header, value, kind, css, tone, headerCss, element, cssFor, dir, titleFor));
        return this;
    }

    public int Count => _columns.Count;
    public DataColumn this[int index] => _columns[index];
    public IEnumerator<DataColumn> GetEnumerator() => _columns.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _columns.GetEnumerator();
}
