using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-54 (#32): the enterprise data grid. The page (or a page definition) says which columns, which endpoint and which texts; the grid reads the rows from the endpoint one page at a
// time, shows only the rows in view (100,000 rows stay smooth), and offers sort, column filters, a column picker, pinning, density, selection with bulk actions, export and saved views.

/// <summary>One column of the grid. <see cref="Type"/> is text, number, date, datetime, bool or badge; <see cref="Pinned"/> is start or end (the side follows the reading direction).</summary>
public sealed record GridColumn
{
    public required string Key { get; init; }
    public required TranslatableText Header { get; init; }
    public string Type { get; init; } = "text";
    public int Width { get; init; } = 140;
    public bool Sortable { get; init; } = true;
    public bool Filterable { get; init; } = true;
    public bool Hidden { get; init; }
    public string? Pinned { get; init; }
    /// <summary>start, center or end; empty: numbers end, the rest start.</summary>
    public string? Align { get; init; }
    /// <summary>For a column of type <c>actions</c>: the buttons of every row.</summary>
    public IReadOnlyList<GridRowAction> Actions { get; init; } = [];
}

/// <summary>A button of the bulk bar: it posts the selected ids (or "all matching" with the query) as JSON to <see cref="Url"/>.</summary>
/// <summary>An action of a row (an "actions" column): a link (<c>get</c>, e.g. the edit form) or a post to an action (<c>post</c>, e.g. <c>/a/department-toggle</c>); <c>{id}</c> in the url is the row id.</summary>
public sealed record GridRowAction
{
    public required string Key { get; init; }
    public required TranslatableText Label { get; init; }
    public required string Url { get; init; }
    /// <summary>get (a link, the default) or post (the id is posted; the answer is JSON; the grid reloads on success).</summary>
    public string Method { get; init; } = "get";
    public TranslatableText? Confirm { get; init; }
    /// <summary>primary, secondary, success, danger, warning.</summary>
    public string Tone { get; init; } = "secondary";
    /// <summary>An expression over <c>row</c> (the row as the grid has it): the action is shown only while it holds. The server decides again when the action runs.</summary>
    public string? ShowWhen { get; init; }
}

public sealed record GridBulkAction
{
    public required string Key { get; init; }
    public required TranslatableText Label { get; init; }
    public required string Url { get; init; }
    public TranslatableText? Confirm { get; init; }
    public string Tone { get; init; } = "primary";
}

/// <summary>Every text of the grid is an input of the page (ADR-0008). <c>{0}</c> is replaced by a number.</summary>
public sealed record GridTexts
{
    public required TranslatableText Search { get; init; }
    public required TranslatableText Columns { get; init; }
    public required TranslatableText Density { get; init; }
    public required TranslatableText Compact { get; init; }
    public required TranslatableText Comfortable { get; init; }
    public required TranslatableText Export { get; init; }
    public required TranslatableText Views { get; init; }
    public required TranslatableText SaveView { get; init; }
    public required TranslatableText ViewName { get; init; }
    public required TranslatableText Loading { get; init; }
    public required TranslatableText Empty { get; init; }
    public required TranslatableText Error { get; init; }
    public required TranslatableText Rows { get; init; }
    public required TranslatableText Selected { get; init; }
    public required TranslatableText SelectAllMatching { get; init; }
    public required TranslatableText ClearSelection { get; init; }
    public required TranslatableText Filter { get; init; }
    public required TranslatableText Delete { get; init; }

    /// <summary>The scope choice "only me" of a saved view (SCRUM-793); optional, a page that writes no texts block or an older one keeps this default.</summary>
    public TranslatableText ViewScopeMe { get; init; } = TranslatableText.Of(("en", "Only me"), ("fa", "فقط من"));

    /// <summary>The grid texts a page gets when it writes none (SCRUM-743: a stored page needs no texts block); every text has both languages, the page may still replace any of them.</summary>
    public static GridTexts Default { get; } = new()
    {
        Search = TranslatableText.Of(("en", "Search"), ("fa", "جستجو")),
        Columns = TranslatableText.Of(("en", "Columns"), ("fa", "ستون‌ها")),
        Density = TranslatableText.Of(("en", "Density"), ("fa", "تراکم")),
        Compact = TranslatableText.Of(("en", "Compact"), ("fa", "فشرده")),
        Comfortable = TranslatableText.Of(("en", "Comfortable"), ("fa", "راحت")),
        Export = TranslatableText.Of(("en", "Export"), ("fa", "خروجی")),
        Views = TranslatableText.Of(("en", "Views"), ("fa", "نماها")),
        SaveView = TranslatableText.Of(("en", "Save view"), ("fa", "ذخیره نما")),
        ViewName = TranslatableText.Of(("en", "View name"), ("fa", "نام نما")),
        Loading = TranslatableText.Of(("en", "Loading"), ("fa", "در حال بارگذاری")),
        Empty = TranslatableText.Of(("en", "No rows"), ("fa", "ردیفی نیست")),
        Error = TranslatableText.Of(("en", "Could not load"), ("fa", "بارگذاری نشد")),
        Rows = TranslatableText.Of(("en", "{0} rows"), ("fa", "{0} ردیف")),
        Selected = TranslatableText.Of(("en", "{0} selected"), ("fa", "{0} انتخاب شد")),
        SelectAllMatching = TranslatableText.Of(("en", "Select all {0}"), ("fa", "انتخاب همه {0}")),
        ClearSelection = TranslatableText.Of(("en", "Clear"), ("fa", "پاک کردن")),
        Filter = TranslatableText.Of(("en", "Filter"), ("fa", "فیلتر")),
        Delete = TranslatableText.Of(("en", "Delete"), ("fa", "حذف")),
    };
}

/// <summary>Inputs of <see cref="PemsDataGrid"/>. The endpoint answers <c>GET url?page=&amp;pageSize=&amp;sort=&amp;q=&amp;f.column=op:value</c> with <c>{ items: [...], total: n }</c> (see <c>GridQueryExtensions</c>).</summary>
public sealed record DataGridOptions : FrameOptions
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public string IdField { get; init; } = "id";
    public required IReadOnlyList<GridColumn> Columns { get; init; }
    /// <summary>Null uses <see cref="GridTexts.Default"/>.</summary>
    public GridTexts? Texts { get; init; }
    public int PageSize { get; init; } = 100;
    /// <summary>Row height in pixels; fixed, so only the rows in view are written.</summary>
    public int RowHeight { get; init; } = 36;
    /// <summary>Height of the scroll area in pixels.</summary>
    public int Height { get; init; } = 480;
    public string Density { get; init; } = "comfortable";
    public bool Selectable { get; init; }
    public IReadOnlyList<GridBulkAction> BulkActions { get; init; } = [];
    public string? ExportUrl { get; init; }
    /// <summary>Saved views are kept per user in the browser under this key; null turns saved views off.</summary>
    public string? ViewsKey { get; init; }
    public string? Css { get; init; }
}
