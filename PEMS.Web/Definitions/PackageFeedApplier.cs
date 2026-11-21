using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Migration;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-807 (gap of SCRUM-583): the apply step of a data feed. FeedPlanner is the dry run; this runs the plan through the entity catalog: the same writable properties, the same create and edit permissions, the same
// validation attributes (MaxLength, Range, Required) and the same audit (the context's SaveChanges) as the screens, with no code per entity. The all-or-nothing option runs every row in one transaction and writes
// nothing when any row is refused; running the same package again changes nothing, and every run is recorded with the package hash (table AppliedPackages).
//
// A data file of a package is JSON: { "keyFields": ["Code"], "rows": [ { "Code": "A-1", "Name": "Main store" } ] }.

/// <summary>What a feed run did, per data set: the plan (counts per action, rows with errors) and whether anything was written.</summary>
public sealed record FeedDataSetResult(string Entity, string Mode, int Added, int Changed, int Skipped, IReadOnlyList<FeedRowPlan> Refused);

public sealed record FeedApplyResult(bool Ok, bool Written, IReadOnlyList<FeedDataSetResult> DataSets, string? Error)
{
    public string Summary => Error ?? string.Join("; ", DataSets.Select(d => $"{d.Entity} ({d.Mode}): {d.Added} added, {d.Changed} changed, {d.Skipped} skipped" + (d.Refused.Count > 0 ? $", {d.Refused.Count} refused" : "")));
}

public sealed class PackageFeedApplier(IServiceScopeFactory scopes, EntityCatalog catalog, IActionTransaction transaction)
{
    public const int MaxRowsPerDataSet = 50_000;

    /// <summary>The key fields and rows of a data file; throws <see cref="PackageRefusedException"/> when the file is not in the format.</summary>
    public static (IReadOnlyList<string> KeyFields, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows) ReadDataFile(byte[] bytes)
    {
        JsonObject root;
        try { root = JsonNode.Parse(bytes) as JsonObject ?? throw new PackageRefusedException("A data file is a JSON object with keyFields and rows."); }
        catch (System.Text.Json.JsonException) { throw new PackageRefusedException("A data file is not valid JSON."); }
        var keys = (root["keyFields"] as JsonArray)?.Select(n => EntityJson.Str(n) ?? "").Where(k => k.Length > 0).ToList() ?? [];
        if (keys.Count == 0) throw new PackageRefusedException("A data file names its keyFields.");
        if (root["rows"] is not JsonArray rows) throw new PackageRefusedException("A data file has a list of rows.");
        if (rows.Count > MaxRowsPerDataSet) throw new PackageRefusedException($"A data file has at most {MaxRowsPerDataSet} rows.");
        var list = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var node in rows)
        {
            if (node is not JsonObject o) throw new PackageRefusedException("A row is an object.");
            list.Add(o.ToDictionary(p => p.Key, p => p.Value is null ? null : p.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)v : p.Value.ToJsonString(), StringComparer.Ordinal));
        }
        return (keys, list);
    }

    /// <summary>
    /// Applies the data sets of a verified package. <paramref name="dryRun"/> only plans. With <paramref name="allOrNothing"/> (the default) nothing is written when any row of any data set is refused.
    /// The caller's roles must hold the create permission of every entity that gets a new row and the edit permission of every entity that gets a changed row.
    /// </summary>
    public async Task<FeedApplyResult> ApplyAsync(PackageManifest manifest, IReadOnlyDictionary<string, byte[]> files, string packageHash, IReadOnlyCollection<string> roles, string? userName,
        bool dryRun = false, bool allOrNothing = true, CancellationToken ct = default)
    {
        var results = new List<FeedDataSetResult>();
        var steps = new List<Func<Task>>();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        foreach (var set in manifest.DataSets)
        {
            var entity = catalog.Get(set.Entity);
            if (entity is null) return new FeedApplyResult(false, false, results, $"The entity '{set.Entity}' is not one a feed may write.");
            (IReadOnlyList<string> keys, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) data;
            try { data = ReadDataFile(files[set.File]); }
            catch (PackageRefusedException ex) { return new FeedApplyResult(false, false, results, $"{set.File}: {ex.Message}"); }

            var method = typeof(PackageFeedApplier).GetMethod(nameof(PlanAsync), BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(entity.ClrType);
            var planned = await (Task<(FeedPlan Plan, Func<Task> Write)>)method.Invoke(this, [db, entity, set.Mode, data.keys, data.rows, ct])!;
            results.Add(new FeedDataSetResult(entity.Name, set.Mode, planned.Plan.Count(FeedRowAction.Add), planned.Plan.Count(FeedRowAction.Change), planned.Plan.Count(FeedRowAction.Skip), planned.Plan.Rows.Where(r => r.Action == FeedRowAction.Refuse).ToList()));
            if (planned.Plan.Count(FeedRowAction.Add) > 0 && !await EntityAccess.CanWriteAsync(scope.ServiceProvider, entity, entity.CreatePermission, roles, ct))
                return new FeedApplyResult(false, false, results, $"You may not add {entity.Name} rows.");
            if (planned.Plan.Count(FeedRowAction.Change) > 0 && !await EntityAccess.CanWriteAsync(scope.ServiceProvider, entity, entity.EditPermission, roles, ct))
                return new FeedApplyResult(false, false, results, $"You may not change {entity.Name} rows.");
            steps.Add(planned.Write);
        }

        var refused = results.Sum(r => r.Refused.Count);
        if (dryRun || (allOrNothing && refused > 0)) return new FeedApplyResult(refused == 0, false, results, refused > 0 && !dryRun ? $"{refused} row(s) were refused; nothing was written." : null);

        var outcome = await transaction.RunAsync(async () =>
        {
            foreach (var write in steps) await write();
            await db.SaveChangesAsync(ct);
            return ActionOutcome.Done();
        }, ct);
        return outcome.Ok ? new FeedApplyResult(true, true, results, null) : new FeedApplyResult(false, false, results, "The feed could not be written; nothing was changed.");
    }

    /// <summary>Plans one data set against the rows of the entity and prepares the writes of the rows that are not refused.</summary>
    private async Task<(FeedPlan Plan, Func<Task> Write)> PlanAsync<T>(PEMSDbContext db, EntityRegistration entity, string mode, IReadOnlyList<string> keyFields, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, StringComparer.Ordinal);
        string Text(T row, string name) => EntityJson.Show(props[name].GetValue(row));

        var existingRows = await db.Set<T>().ToListAsync(ct);
        var byKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var r in existingRows)
        {
            var k = FeedPlanner.KeyOf(keyFields.Where(props.ContainsKey).ToDictionary(f => f, f => (string?)Text(r, f)), keyFields);
            if (k.Length > 0) byKey.TryAdd(k, r);
        }
        var existing = byKey.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string?>)props.Keys.ToDictionary(n => n, n => (string?)Text(p.Value, n)), StringComparer.Ordinal);

        string? Validate(IReadOnlyDictionary<string, string?> row)
        {
            var probe = Activator.CreateInstance<T>();
            foreach (var (name, value) in row)
            {
                if (!entity.Writable.Contains(name) && !keyFields.Contains(name)) return $"'{name}' is not a field a feed may write.";
                if (!props.TryGetValue(name, out var pi)) return $"'{name}' is not a field of {entity.Name}.";
                var converted = EntityJson.Convert(pi.PropertyType, value);
                if (converted is EntityJson.NotConvertible) return $"'{name}' has a value that does not fit.";
                // the validation attributes of the property (MaxLength, StringLength, Range, Required) judge the value, as they do for the screens; only the fields the row carries are judged
                var problems = new List<ValidationResult>();
                if (!Validator.TryValidateProperty(converted, new ValidationContext(probe) { MemberName = name }, problems)) return $"'{name}': {problems[0].ErrorMessage}";
            }
            return null;
        }

        void Fill(T target, IReadOnlyDictionary<string, string?> row)
        {
            foreach (var (name, value) in row)
                if (props.TryGetValue(name, out var pi) && EntityJson.Convert(pi.PropertyType, value) is { } converted && converted is not EntityJson.NotConvertible) EntityJson.SetValue(pi, target!, converted);
                else if (props.TryGetValue(name, out var pn) && string.IsNullOrWhiteSpace(value)) EntityJson.SetValue(pn, target!, null);
        }

        var plan = FeedPlanner.Plan(entity.Name, mode, rows, keyFields, existing, Validate);
        Task Write()
        {
            foreach (var rp in plan.Rows)
            {
                var row = rows[rp.Row - 1];
                if (rp.Action == FeedRowAction.Add)
                {
                    var created = Activator.CreateInstance<T>();
                    Fill(created, row);
                    entity.BeforeCreate?.Invoke(created);
                    db.Add(created);
                }
                else if (rp.Action == FeedRowAction.Change && byKey.TryGetValue(rp.Key, out var current))
                    Fill(current, row.Where(f => rp.ChangedFields.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value));
            }
            return Task.CompletedTask;
        }
        return (plan, Write);
    }
}
