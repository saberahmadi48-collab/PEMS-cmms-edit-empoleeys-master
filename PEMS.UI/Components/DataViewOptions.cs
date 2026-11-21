using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-55 (#33): the view types of a record set: cards, kanban, calendar (Jalali and Gregorian) and timeline. They read the same endpoint as the data grid (GridRequest / GridFields) and are
// configured from the field names of the entity (title, date, group), so a model gets a calendar or a status board by naming its fields, with no code.

/// <summary>The fields of a row that the views show. Only <see cref="Title"/> is always needed; the others serve the view that uses them.</summary>
public sealed record ViewFields
{
    public string Id { get; init; } = "id";
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>A short status or category text shown as a badge on the card.</summary>
    public string? Badge { get; init; }
    public string? Image { get; init; }
    /// <summary>The date (or date and time, ISO) of a row: places it in the calendar and the timeline, and is the start of its bar in the gantt.</summary>
    public string? Date { get; init; }
    /// <summary>Gantt: the end date of a row; a row without one is a one-day bar.</summary>
    public string? End { get; init; }
    /// <summary>Tree: the field that holds the id of the parent row; a row without a parent (or with a parent that is not in the result) is a root.</summary>
    public string? Parent { get; init; }
    /// <summary>The field that says which board column a row belongs to (kanban).</summary>
    public string? Group { get; init; }
}

/// <summary>One column of the status board: the value of the group field, its label and a colour family.</summary>
public sealed record ViewGroup
{
    public required string Value { get; init; }
    public required TranslatableText Label { get; init; }
    public string Tone { get; init; } = "secondary";
}

public sealed record ViewTexts
{
    public required TranslatableText Loading { get; init; }
    public required TranslatableText Empty { get; init; }
    public required TranslatableText Error { get; init; }
    public required TranslatableText More { get; init; }
    public required TranslatableText Today { get; init; }
    public required TranslatableText Previous { get; init; }
    public required TranslatableText Next { get; init; }
    public required TranslatableText Jalali { get; init; }
    public required TranslatableText Gregorian { get; init; }
    public required TranslatableText Uncategorized { get; init; }
    /// <summary>The "and {0} more" line of a crowded calendar day.</summary>
    public required TranslatableText MoreItems { get; init; }
    /// <summary>The toggle of a tree node (collapse, expand): the text a screen reader hears.</summary>
    public TranslatableText Collapse { get; init; } = TranslatableText.Of(("en", "Collapse"), ("fa", "بستن"));
    public TranslatableText Expand { get; init; } = TranslatableText.Of(("en", "Expand"), ("fa", "باز کردن"));
}

/// <summary>Inputs of <see cref="PemsDataView"/>. <see cref="View"/> is cards, kanban, calendar, timeline, gantt or tree.</summary>
public sealed record DataViewOptions : FrameOptions
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required string View { get; init; }
    public required ViewFields Fields { get; init; }
    public required ViewTexts Texts { get; init; }
    /// <summary>Kanban columns, in order; rows with another value fall into an "uncategorized" column.</summary>
    public IReadOnlyList<ViewGroup> Groups { get; init; } = [];
    /// <summary>gregorian or jalali: the calendar the month grid starts in (the user can switch).</summary>
    public string Calendar { get; init; } = "gregorian";
    /// <summary>First day of the week, 0 = Sunday ... 6 = Saturday.</summary>
    public int WeekStart { get; init; } = 6;
    /// <summary>The link of a row, <c>{id}</c> is replaced: <c>/Items/Details/{id}</c>.</summary>
    public string? ItemUrl { get; init; }
    /// <summary>Kanban: moving a card posts <c>{ id, to }</c> (JSON, antiforgery token) here; without it the board is read only.</summary>
    public string? MoveUrl { get; init; }
    public int PageSize { get; init; } = 50;
    public int Height { get; init; } = 560;
    public string? Css { get; init; }
}
