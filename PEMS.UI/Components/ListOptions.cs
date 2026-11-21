namespace PEMS.UI.Components;

// SCRUM-507: the list page (SCRUM-337) as registered components. Every text is resolved by the tag helper (it owns the page's
// PemsListTexts) so a component never reads the tag helper context. Attributes the markup builder used to sort by name stay sorted here.

/// <summary>Inputs of the plain list frames (page, panel, grid, footer): the page's own classes and attributes.</summary>
public sealed record ListFrameOptions
{
    /// <summary>The page's own classes, kept after the component's.</summary>
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListToolbar"/>: title and optional count; the page's search form and tools are the child content.</summary>
public sealed record ListToolbarOptions
{
    public string Heading { get; init; } = "";
    public string? HeadingTitle { get; init; }
    public int? Total { get; init; }
    /// <summary>The localised word for the count, spoken as "Total: 42".</summary>
    public string TotalLabel { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListIconTool"/>: an icon-only button or link whose label is its tool tip and its screen-reader text.</summary>
public sealed record ListIconToolOptions
{
    /// <summary>"button" (default) or "a".</summary>
    public string Tag { get; init; } = "button";
    public required string Icon { get; init; }
    public required string Label { get; init; }
    /// <summary>Classes after <c>lp-icon-btn</c>.</summary>
    public string? ExtraClass { get; init; }
    /// <summary>More attributes (data-*, aria-*, href, id); merged with the component's and written in name order.</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListSearch"/>: the search form that opens over the title; hidden inputs that keep the filters are the child content.</summary>
public sealed record ListSearchOptions
{
    public string Id { get; init; } = "";
    public string Action { get; init; } = "";
    public string InputId { get; init; } = "";
    public string Name { get; init; } = "search";
    public string? Value { get; init; }
    public string? Placeholder { get; init; }
    public string? SearchType { get; init; }
    public bool LiveFilter { get; init; }
    public string SearchText { get; init; } = "";
    public string SearchCloseText { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListTools"/>: the row of icon tools; <see cref="SearchFor"/> adds the button that opens the search form.</summary>
public sealed record ListToolsOptions
{
    public string? SearchFor { get; init; }
    public string SearchOpenText { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListFilters"/>: funnel button with the active count, then a GET form (child content = fields) with Reset and Apply.</summary>
public sealed record ListFiltersOptions
{
    public string FormId { get; init; } = "";
    public string Action { get; init; } = "";
    public int Count { get; init; }
    public string? ResetHref { get; init; }
    public string FiltersText { get; init; } = "";
    public string ActiveFiltersText { get; init; } = "";
    public string ResetFiltersText { get; init; } = "";
    public string ApplyFiltersText { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>One option of a <see cref="PemsListFilterSelect"/>.</summary>
public sealed record ListFilterOption(string Value, string Text);

/// <summary>Inputs of <see cref="PemsListFilterSelect"/>: label, an "all" option when <see cref="AllText"/> is set, then the options.</summary>
public sealed record ListFilterSelectOptions
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Value { get; init; }
    public string? AllText { get; init; }
    public IReadOnlyList<ListFilterOption> Items { get; init; } = Array.Empty<ListFilterOption>();
    /// <summary>Every attribute the page wrote on the tag (the element is not given a class of its own).</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListPopover"/>: a toolbar tool that opens a dropdown panel holding the child content.</summary>
public sealed record ListPopoverOptions
{
    public string Id { get; init; } = "";
    public string Icon { get; init; } = "bi-three-dots";
    public string Label { get; init; } = "";
    /// <summary>true: the label shows next to the icon like the New button.</summary>
    public bool Primary { get; init; }
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListKpiToggle"/>: the button that shows or hides the KPI row (<see cref="Target"/> is its id).</summary>
public sealed record ListKpiToggleOptions
{
    public string Target { get; init; } = "";
    public string StorageKey { get; init; } = "";
    public bool Open { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsListExport"/>: the Excel export link.</summary>
public sealed record ListExportOptions
{
    public string Href { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsListResetColumns"/>: resets the stored column widths of the grid <see cref="GridKey"/>.</summary>
public sealed record ListResetColumnsOptions
{
    public string GridKey { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsListNew"/>: the primary "New ..." action; a link to <see cref="Href"/> or, with <see cref="Modal"/>, a button that opens that modal.</summary>
public sealed record ListNewOptions
{
    public string Href { get; init; } = "";
    public string? Modal { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsListKpis"/>: the KPI row, hidden until the toggle shows it unless <see cref="Open"/>.</summary>
public sealed record ListKpisOptions
{
    public string Id { get; init; } = "";
    public bool Open { get; init; }
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListChips"/>: the active-filter chips (child content); "reset all" shows from two chips on.</summary>
public sealed record ListChipsOptions
{
    public string? ResetHref { get; init; }
    public int Count { get; init; }
    public string ActiveFiltersText { get; init; } = "";
    public string ResetAllText { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListChip"/>: "label: value" with a link that removes the filter.</summary>
public sealed record ListChipOptions
{
    public string Label { get; init; } = "";
    public string? Value { get; init; }
    public string RemoveHref { get; init; } = "";
    public string RemoveText { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsListTable"/>: the scrolling table (thead and tbody are the child content) and its empty state.</summary>
public sealed record ListTableOptions
{
    public string? GridKey { get; init; }
    public bool Empty { get; init; }
    public string? EmptyText { get; init; }
    public bool Filtered { get; init; }
    public string? ClearHref { get; init; }
    public string NoResultsHint { get; init; } = "";
    public string ClearAllText { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListBulk"/>: the bulk-action bar, hidden until a row is ticked.</summary>
public sealed record ListBulkOptions
{
    public string CountText { get; init; } = "{0}";
    public string Name { get; init; } = "ids";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsListMenu"/>: a toolbar tool that opens a dropdown of the page's own items (child content).</summary>
public sealed record ListMenuOptions
{
    public string Id { get; init; } = "";
    public string Icon { get; init; } = "bi-three-dots";
    public string Label { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Small helpers the list components share.</summary>
internal static class ListMarkup
{
    /// <summary>The class string with the page's own classes after the component's.</summary>
    public static string Css(string own, string? page) => string.IsNullOrWhiteSpace(page) ? own : own + " " + page;

    /// <summary>Attributes written in name order (the old markup builder sorted them); a null value drops the attribute.</summary>
    public static IEnumerable<KeyValuePair<string, object>> Sorted(IEnumerable<KeyValuePair<string, string?>> attributes)
        => attributes.Where(a => a.Value is not null).OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => new KeyValuePair<string, object>(a.Key, a.Value!));
}
