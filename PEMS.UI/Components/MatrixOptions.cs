using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-627 / 626: an editable matrix (rows by columns of cells): the FMEA / RCM grids, the reliability analysis tables and the extracted tables of the OCR review are all this one component.
// The page host builds the cells from the rows of a permission-checked data source (bindings.matrix); each edited cell is posted as JSON to a stored action (pems-matrix.js). Nothing here reads data.

/// <summary>A row or a column of the matrix: the key the cells use and its label.</summary>
public sealed record MatrixAxis
{
    public required string Key { get; init; }
    public required string Label { get; init; }
}

/// <summary>One cell: its value as text and the id of the record behind it (posted back so the action knows which record to change).</summary>
public sealed record MatrixCell
{
    public string? Value { get; init; }
    public string? Id { get; init; }
}

/// <summary>A choice of a select cell.</summary>
public sealed record MatrixChoice
{
    public required string Value { get; init; }
    public required TranslatableText Label { get; init; }
}

public sealed record MatrixTexts
{
    public TranslatableText Saving { get; init; } = TranslatableText.Of(("en", "Saving"), ("fa", "در حال ذخیره"));
    public TranslatableText Saved { get; init; } = TranslatableText.Of(("en", "Saved"), ("fa", "ذخیره شد"));
    public TranslatableText Error { get; init; } = TranslatableText.Of(("en", "Could not save; the value was restored"), ("fa", "ذخیره نشد؛ مقدار برگشت"));
    public TranslatableText Empty { get; init; } = TranslatableText.Of(("en", "No cells"), ("fa", "سلولی نیست"));
    public TranslatableText Corner { get; init; } = TranslatableText.Of(("en", ""), ("fa", ""));
}

/// <summary>Inputs of <see cref="PemsMatrix"/>. <see cref="Cells"/> is keyed "rowKey|columnKey". <see cref="CellType"/> is text, number or select (with <see cref="Choices"/>). Without <see cref="ActionUrl"/> the matrix is read only.</summary>
public sealed record MatrixOptions : FrameOptions
{
    public required string Id { get; init; }
    public required IReadOnlyList<MatrixAxis> Rows { get; init; }
    public required IReadOnlyList<MatrixAxis> Columns { get; init; }
    public IReadOnlyDictionary<string, MatrixCell> Cells { get; init; } = new Dictionary<string, MatrixCell>();
    public string CellType { get; init; } = "text";
    public IReadOnlyList<MatrixChoice> Choices { get; init; } = [];
    /// <summary>A stored action (/a/{key}); an edited cell posts { row, column, value, id } as JSON (antiforgery header).</summary>
    public string? ActionUrl { get; init; }
    public MatrixTexts Texts { get; init; } = new();
    public string? Css { get; init; }
}
