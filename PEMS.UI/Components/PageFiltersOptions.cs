using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// Batch 5 (SCRUM-747, 713, 669): the filters of a report or search page are a definition. The bar is a GET form: the values come back as the query of the page (/p/report?projectId=3), the page puts them
// into the urls of its grids (/d/key?f.projectId={{params.projectId}}) and the read operation takes them as arguments. Nothing here reads data; a select gets its choices from the definition or from
// the page host (bindings.filterOptions).

/// <summary>One choice of a select filter.</summary>
public sealed record PageFilterOption
{
    public required string Value { get; init; }
    public required TranslatableText Label { get; init; }
}

/// <summary>One filter: <c>select</c> (the choices, or text of a data source), <c>text</c> or <c>date</c>. <see cref="Value"/> is the value now on the page (usually {{params.name}}).</summary>
public sealed record PageFilterField
{
    public required string Name { get; init; }
    public required TranslatableText Label { get; init; }
    public string Kind { get; init; } = "select";
    public string? Value { get; init; }
    public IReadOnlyList<PageFilterOption> Options { get; init; } = [];
}

/// <summary>Inputs of <see cref="PemsPageFilters"/>. <see cref="ResetUrl"/> is the page without a query.</summary>
public sealed record PageFiltersOptions : FrameOptions
{
    public string? Id { get; init; }
    public string? Css { get; init; }
    public IReadOnlyList<PageFilterField> Fields { get; init; } = [];
    public string? ResetUrl { get; init; }
    public TranslatableText Submit { get; init; } = TranslatableText.Of(("en", "Show"), ("fa", "نمایش"));
    public TranslatableText Reset { get; init; } = TranslatableText.Of(("en", "Reset"), ("fa", "بازنشانی"));
    public TranslatableText All { get; init; } = TranslatableText.Of(("en", "All"), ("fa", "همه"));
}
