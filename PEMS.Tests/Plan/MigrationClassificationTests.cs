using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Plan;

/// <summary>SCRUM-106 (#85): every MVC controller has exactly one row (one wave, one owner, one decision) in plan/migration-classification.csv, so a new module cannot be forgotten. Regenerate proposals with tools/rest-inventory/classify.py --write.</summary>
public sealed class MigrationClassificationTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));

    private static readonly HashSet<string> Decisions = ["page-definition", "form-definition", "workflow-hybrid", "code-only"];

    private static List<string> Split(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { parts.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }
        parts.Add(current.ToString());
        return parts;
    }

    private static List<Dictionary<string, string>> Rows()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "plan", "migration-classification.csv")).Where(l => l.Length > 0).ToList();
        var header = Split(lines[0]);
        return lines.Skip(1).Select(l => { var v = Split(l); return header.Select((h, i) => (h, i < v.Count ? v[i] : "")).ToDictionary(x => x.h, x => x.Item2); }).ToList();
    }

    /// <summary>The controllers of the MVC host as the code has them now: <c>PEMS.Web/Controllers</c> and <c>PEMS.Web/Areas/{area}/Controllers</c> (the REST controllers in <c>PEMS.Web/Api</c> are not part of the migration).</summary>
    private static List<(string Area, string Name)> Controllers()
    {
        var web = Path.Combine(Root(), "PEMS.Web");
        var found = new List<(string, string)>();
        var all = Directory.GetFiles(web, "*Controller.cs", SearchOption.AllDirectories); if (all.Length == 0) throw new InvalidOperationException("no controller files under " + web);
        foreach (var file in Directory.GetFiles(web, "*Controller.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(web, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith("Api/") || rel.StartsWith("bin/") || rel.StartsWith("obj/")) continue;
            var area = Regex.Match(rel, @"^Areas/([^/]+)/Controllers/") is { Success: true } m ? m.Groups[1].Value : rel.StartsWith("Controllers/") ? "" : null;
            if (area is null) continue;
            // a file may hold more than one controller (FormHostController.cs also holds ActionHostController)
            foreach (Match c in Regex.Matches(File.ReadAllText(file), @"\bclass\s+(\w+)Controller\b")) found.Add((area, c.Groups[1].Value));
        }
        return found;
    }

    [Fact]
    public void Every_controller_has_exactly_one_row_with_a_wave_an_owner_and_a_decision()
    {
        var rows = Rows();
        var problems = new List<string>();
        var counted = rows.GroupBy(r => (r["area"], r["controller"])).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (area, name) in Controllers())
        {
            // a controller that only exists as a partial helper or base has no actions of its own; the inventory lists the ones that do
            if (!counted.ContainsKey((area, name))) problems.Add($"{(area.Length > 0 ? area + "/" : "")}{name} has no row");
        }
        foreach (var (key, n) in counted) if (n > 1) problems.Add($"{key} has {n} rows");
        foreach (var r in rows)
        {
            foreach (var f in new[] { "module", "wave", "owner", "decision" }) if (string.IsNullOrWhiteSpace(r[f])) problems.Add($"{r["controller"]}: {f} is empty");
            if (!Decisions.Contains(r["decision"])) problems.Add($"{r["controller"]}: decision '{r["decision"]}'");
        }
        Assert.True(problems.Count == 0, "Run python tools/rest-inventory/classify.py --write and review the new proposals:\n" + string.Join("\n", problems.Take(30)));
    }

    [Fact]
    public void A_row_without_a_controller_is_stale()
    {
        var live = Controllers().ToHashSet();
        var stale = Rows().Where(r => !live.Contains((r["area"], r["controller"]))).Select(r => r["controller"]).ToList();
        Assert.True(stale.Count == 0, $"rows without a controller ({live.Count} live): " + string.Join(", ", stale.Take(8)));
    }
}
