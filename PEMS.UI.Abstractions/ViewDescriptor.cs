namespace PEMS.UI.Abstractions;

/// <summary>Describes a read-only list view: a title, its columns and the text shown when there are no rows. Rows are data and are passed separately.</summary>
public sealed record ViewDescriptor
{
    public required string Id { get; init; }
    public required TranslatableText Title { get; init; }
    public required IReadOnlyList<ColumnDescriptor> Columns { get; init; }
    public TranslatableText? EmptyText { get; init; }
}
