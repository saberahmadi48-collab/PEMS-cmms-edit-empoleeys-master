using System.Data;
using Microsoft.Data.SqlClient;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// SCRUM-280: gives an old-release database one row in every table it can, without knowing the entities of that release,
/// so the upgrade migrations run over data (a NOT NULL column added to a filled table, an UPDATE that moves values,
/// a new unique index or foreign key) and not over empty tables.
/// Generic, from the catalog: required columns get a typed sample value, required foreign keys point at a row of the parent
/// (parents are filled first, in repeated passes), nullable columns stay NULL, defaults and identities are left to SQL Server.
/// A table whose insert is refused (a check constraint, a cycle, an exotic type) is skipped and reported, never forced.
/// </summary>
public static class RepresentativeDataFiller
{
    public sealed record Result(int TablesTotal, int TablesWithRows, IReadOnlyDictionary<string, string> Skipped);

    private sealed record Column(string Name, string Type, int MaxLength, bool Nullable, bool Generated, bool HasDefault);

    private sealed record ForeignKey(string Name, string ReferencedTable, IReadOnlyList<(string Column, string ReferencedColumn)> Columns);

    private static readonly HashSet<string> SkippedTables = new(StringComparer.OrdinalIgnoreCase) { "dbo.__EFMigrationsHistory" };

    public static async Task<Result> FillAsync(string connectionString, int maxPasses = 12)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var columns = await ReadColumnsAsync(conn);
        var foreignKeys = await ReadForeignKeysAsync(conn);
        var tables = columns.Keys.Where(t => !SkippedTables.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var progress = false;
            foreach (var table in tables)
            {
                if (await CountAsync(conn, table) > 0) { skipped.Remove(table); continue; }
                var outcome = await TryInsertAsync(conn, table, columns[table], foreignKeys.GetValueOrDefault(table) ?? new List<ForeignKey>());
                if (outcome is null) { progress = true; skipped.Remove(table); }
                else skipped[table] = outcome;
            }
            if (!progress) break;
        }

        var withRows = 0;
        foreach (var table in tables)
            if (await CountAsync(conn, table) > 0) withRows++;
        return new Result(tables.Count, withRows, skipped);
    }

    /// <summary>Row count of every user table ("schema.table" -> rows).</summary>
    public static async Task<Dictionary<string, long>> RowCountsAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT s.name + '.' + t.name, SUM(p.rows)
                            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
                            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
                            GROUP BY s.name, t.name";
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            counts[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1));
        return counts;
    }

    /// <returns>null when a row was inserted, otherwise why not.</returns>
    private static async Task<string?> TryInsertAsync(SqlConnection conn, string table, IReadOnlyList<Column> cols, IReadOnlyList<ForeignKey> fks)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var fk in fks)
        {
            // a NOT NULL reference is filled even when it has a default: the default rarely points at an existing row
            var required = fk.Columns.Any(c => !cols.First(x => x.Name == c.Column).Nullable);
            if (!required) continue;   // optional reference: left NULL
            if (fk.ReferencedTable == table) return "required self reference";
            var parent = await FirstRowAsync(conn, fk.ReferencedTable, fk.Columns.Select(c => c.ReferencedColumn).ToList());
            if (parent is null) return "parent " + fk.ReferencedTable + " is empty";
            for (var i = 0; i < fk.Columns.Count; i++)
                values[fk.Columns[i].Column] = parent[i];
        }

        foreach (var col in cols)
        {
            if (col.Generated || col.Nullable || col.HasDefault || values.ContainsKey(col.Name)) continue;
            var sample = Sample(col);
            if (sample is null) return "no sample value for type " + col.Type;
            values[col.Name] = sample;
        }

        await using var cmd = conn.CreateCommand();
        var names = values.Keys.ToList();
        cmd.CommandText = names.Count == 0
            ? $"INSERT INTO {Quote(table)} DEFAULT VALUES"
            : $"INSERT INTO {Quote(table)} ({string.Join(", ", names.Select(QuoteName))}) VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i))})";
        for (var i = 0; i < names.Count; i++)
            cmd.Parameters.AddWithValue("@p" + i, values[names[i]]);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
        }
    }

    private static object? Sample(Column col)
    {
        var chars = col.MaxLength < 0 ? 8 : col.Type is "nvarchar" or "nchar" ? col.MaxLength / 2 : col.MaxLength;
        return col.Type switch
        {
            "int" or "bigint" or "smallint" or "tinyint" => 1,
            "bit" => false,
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => 1,
            "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" => "QA-UPGRADE"[..Math.Min(10, Math.Max(1, chars))],
            "uniqueidentifier" => Guid.NewGuid(),
            "datetime" or "datetime2" or "smalldatetime" or "date" => new DateTime(2026, 1, 15, 10, 0, 0),
            "datetimeoffset" => new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
            "time" => new TimeSpan(10, 0, 0),
            "varbinary" or "binary" or "image" => new byte[] { 1 },
            "xml" => "<qa/>",
            _ => null
        };
    }

    private static async Task<object[]?> FirstRowAsync(SqlConnection conn, string table, IReadOnlyList<string> cols)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT TOP (1) {string.Join(", ", cols.Select(QuoteName))} FROM {Quote(table)}";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var row = new object[cols.Count];
        reader.GetValues(row);
        return row;
    }

    private static async Task<long> CountAsync(SqlConnection conn, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT_BIG(*) FROM {Quote(table)}";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Dictionary<string, List<Column>>> ReadColumnsAsync(SqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT s.name + '.' + t.name, c.name, ty.name, c.max_length, c.is_nullable,
                                   CAST(CASE WHEN c.is_identity = 1 OR c.is_computed = 1 OR ty.name IN ('timestamp', 'rowversion') THEN 1 ELSE 0 END AS bit),
                                   CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit)
                            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
                            JOIN sys.columns c ON c.object_id = t.object_id
                            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                            WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                            ORDER BY 1, c.column_id";
        var result = new Dictionary<string, List<Column>>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (!result.TryGetValue(table, out var list)) result[table] = list = new List<Column>();
            list.Add(new Column(reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6)));
        }
        return result;
    }

    private static async Task<Dictionary<string, List<ForeignKey>>> ReadForeignKeysAsync(SqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ps.name + '.' + pt.name, fk.name, rs.name + '.' + rt.name, pc.name, rc.name
                            FROM sys.foreign_keys fk
                            JOIN sys.foreign_key_columns k ON k.constraint_object_id = fk.object_id
                            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
                            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
                            JOIN sys.columns pc ON pc.object_id = k.parent_object_id AND pc.column_id = k.parent_column_id
                            JOIN sys.columns rc ON rc.object_id = k.referenced_object_id AND rc.column_id = k.referenced_column_id
                            ORDER BY 1, 2, k.constraint_column_id";
        var rows = new List<(string Table, string Fk, string Ref, string Col, string RefCol)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));

        return rows.GroupBy(r => r.Table, StringComparer.Ordinal).ToDictionary(
            g => g.Key,
            g => g.GroupBy(r => r.Fk).Select(f => new ForeignKey(f.Key, f.First().Ref, f.Select(r => (r.Col, r.RefCol)).ToList())).ToList(),
            StringComparer.Ordinal);
    }

    private static string Quote(string schemaTable)
    {
        var dot = schemaTable.IndexOf('.');
        return QuoteName(schemaTable[..dot]) + "." + QuoteName(schemaTable[(dot + 1)..]);
    }

    private static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";
}
