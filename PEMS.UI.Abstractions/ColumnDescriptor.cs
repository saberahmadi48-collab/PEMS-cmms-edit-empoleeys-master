namespace PEMS.UI.Abstractions;

public enum ColumnAlignment
{
    Start,
    Center,
    End
}

/// <summary>
/// Describes one table column. <see cref="Key"/> selects the value in each row (data, never translated); <see cref="Header"/> is text.
/// <see cref="Kind"/> says how the value is formatted. A boolean value shows <see cref="TrueText"/> or <see cref="FalseText"/>, which are inputs too.
/// </summary>
public sealed record ColumnDescriptor
{
    public required string Key { get; init; }
    public required TranslatableText Header { get; init; }
    public ColumnAlignment Alignment { get; init; } = ColumnAlignment.Start;
    public bool Sortable { get; init; }
    public ValueKind Kind { get; init; } = ValueKind.Auto;
    public TranslatableText? TrueText { get; init; }
    public TranslatableText? FalseText { get; init; }
}
