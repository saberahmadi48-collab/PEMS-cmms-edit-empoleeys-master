using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>The text of one table cell: data is formatted by the locale (never translated); a boolean shows the column's true/false texts.</summary>
public static class TableCells
{
    public static string Text(IPemsLocale locale, ColumnDescriptor column, IReadOnlyDictionary<string, object?> row)
    {
        row.TryGetValue(column.Key, out var value);
        if (value is bool b)
        {
            var shown = locale.Text(b ? column.TrueText : column.FalseText);
            return shown.Length > 0 ? shown : locale.Format(b);
        }
        return locale.Format(value, column.Kind);
    }
}
