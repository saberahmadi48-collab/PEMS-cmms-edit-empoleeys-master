namespace PEMS.Application.Migration;

// SCRUM-583: the dry run of a data feed. Pure: rows from the package, the rows that exist (by key) and the entity's own validation go in; a plan per row comes out. The apply step runs the plan
// through the entity write actions (same writable fields, permissions and audit as the screens), so no entity has feed code of its own, and running the same feed twice changes nothing (idempotent).

public enum FeedRowAction { Add, Skip, Change, Refuse }

public sealed record FeedRowPlan(int Row, string Key, FeedRowAction Action, IReadOnlyList<string> ChangedFields, string? Error);

public sealed record FeedPlan(string Entity, string Mode, IReadOnlyList<FeedRowPlan> Rows)
{
    public int Count(FeedRowAction action) => Rows.Count(r => r.Action == action);
    public bool HasRefusals => Rows.Any(r => r.Action == FeedRowAction.Refuse);
}

public static class FeedPlanner
{
    /// <summary>
    /// Modes: <c>append</c> adds rows whose key is new and skips the rest; <c>overwrite</c> replaces the values of a row with the same key (a row that already equals the package is skipped);
    /// <c>merge</c> fills only the fields that are empty in the database. Refused rows: no key, a key twice in the file, a failed validation. <paramref name="validate"/> returns the error text or null.
    /// </summary>
    public static FeedPlan Plan(string entity, string mode, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, IReadOnlyList<string> keyFields,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> existing, Func<IReadOnlyDictionary<string, string?>, string?>? validate = null)
    {
        if (mode is not ("append" or "overwrite" or "merge")) throw new ArgumentException($"Unknown feed mode '{mode}'.", nameof(mode));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var plans = new List<FeedRowPlan>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = KeyOf(row, keyFields);
            if (key.Length == 0) { plans.Add(new(i + 1, "", FeedRowAction.Refuse, [], "The row has no value for the key (" + string.Join(", ", keyFields) + ").")); continue; }
            if (!seen.Add(key)) { plans.Add(new(i + 1, key, FeedRowAction.Refuse, [], "The key appears twice in the file.")); continue; }
            if (validate?.Invoke(row) is { } error) { plans.Add(new(i + 1, key, FeedRowAction.Refuse, [], error)); continue; }

            if (!existing.TryGetValue(key, out var current)) { plans.Add(new(i + 1, key, FeedRowAction.Add, row.Keys.ToList(), null)); continue; }
            switch (mode)
            {
                case "append":
                    plans.Add(new(i + 1, key, FeedRowAction.Skip, [], null));
                    break;
                case "overwrite":
                {
                    var changed = row.Where(f => !Same(current.GetValueOrDefault(f.Key), f.Value)).Select(f => f.Key).ToList();
                    plans.Add(new(i + 1, key, changed.Count == 0 ? FeedRowAction.Skip : FeedRowAction.Change, changed, null));
                    break;
                }
                default:   // merge: only a field that is empty in the database and has a value in the package
                {
                    var filled = row.Where(f => string.IsNullOrWhiteSpace(current.GetValueOrDefault(f.Key)) && !string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Key).ToList();
                    plans.Add(new(i + 1, key, filled.Count == 0 ? FeedRowAction.Skip : FeedRowAction.Change, filled, null));
                    break;
                }
            }
        }
        return new FeedPlan(entity, mode, plans);
    }

    /// <summary>The key of a row: its key fields trimmed and joined, case-insensitive (unique rules of the entities compare without case).</summary>
    public static string KeyOf(IReadOnlyDictionary<string, string?> row, IReadOnlyList<string> keyFields)
    {
        var parts = keyFields.Select(k => (row.GetValueOrDefault(k) ?? "").Trim().ToLowerInvariant()).ToList();
        return parts.Any(p => p.Length == 0) ? "" : string.Join("|", parts);
    }

    private static bool Same(string? a, string? b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
}
