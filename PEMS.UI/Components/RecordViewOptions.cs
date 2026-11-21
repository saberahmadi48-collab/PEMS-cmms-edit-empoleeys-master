using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-600: the read-only display of one record (the body of a details page). A definition names the fields; the values arrive with the options (the page host reads the record through the
// permission-checked data source of the page, so nothing here reads data). Reuses the label/value look of info-grid.

/// <summary>One field of a record: the key of the row property, its label and how its value is shown.</summary>
public sealed record RecordField
{
    /// <summary>The property of the row (camel case, as the grid endpoints write it).</summary>
    public required string Key { get; init; }
    public required TranslatableText Label { get; init; }
    /// <summary>text (default), number, date, datetime, bool (shows the yes/no texts) or badge (the value as a badge).</summary>
    public string Type { get; init; } = "text";
    /// <summary>The label and value take the full width of the grid (long texts).</summary>
    public bool Wide { get; init; }
    /// <summary>Shown in a monospace, left-to-right face (codes, numbers).</summary>
    public bool Mono { get; init; }
    /// <summary>A text with merge fields, <c>Dear {{name}}, your order {{code}} is ready</c>: each {{key}} is replaced by that property of the record (as text, never markup); the field's own key is ignored (SCRUM-624).</summary>
    public string? Template { get; init; }
}

/// <summary>The texts of the record view: the yes and no of a bool, the text of an empty value and of a record that is not found.</summary>
public sealed record RecordViewTexts
{
    public TranslatableText Yes { get; init; } = TranslatableText.Of(("en", "Yes"), ("fa", "بله"));
    public TranslatableText No { get; init; } = TranslatableText.Of(("en", "No"), ("fa", "خیر"));
    public TranslatableText Empty { get; init; } = TranslatableText.Of(("en", "-"), ("fa", "-"));
    public TranslatableText NotFound { get; init; } = TranslatableText.Of(("en", "The record was not found."), ("fa", "رکورد پیدا نشد."));
}

/// <summary>Inputs of <see cref="PemsRecordView"/>. <see cref="Values"/> is the row (property to value as text; the page host fills it from the data source of the page); null means the record was not found.</summary>
public sealed record RecordViewOptions : FrameOptions
{
    public required IReadOnlyList<RecordField> Fields { get; init; }
    /// <summary>The row, by property (camel case): dates as ISO text, bool as true or false. Null: not found.</summary>
    public IReadOnlyDictionary<string, string?>? Values { get; init; }
    public RecordViewTexts Texts { get; init; } = new();
    /// <summary>The id of the element (the page host sets it from the instance id).</summary>
    public string? Id { get; init; }
    public string? Css { get; init; }
}
