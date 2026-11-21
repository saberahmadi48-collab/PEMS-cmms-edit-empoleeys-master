using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-791 (gap of SCRUM-585): the change manifest of the work (plan/sheets/change-manifest-audit.md, rule in guideline 04 section 7) names what reaches a customer when they update. This test fails for a
/// migration or a shipped definition kind that the manifest does not name, so a change cannot ship a schema script or a new kind of definition without declaring it. Migrations up to the audit date are
/// covered by the audit table as a whole (the Sprint 2 work); every migration after it must be named in the manifest by its id.
/// </summary>
public sealed class ChangeManifestCoverageTests
{
    /// <summary>The audit date (SCRUM-585): migrations with an id above it must be named in the manifest.</summary>
    private const string AuditedUpTo = "20261120000000";

    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Manifest() => File.ReadAllText(Path.Combine(Root(), "plan", "sheets", "change-manifest-audit.md"));

    /// <summary>The migration ids (the timestamp prefix and name of each migration file, without the designer and snapshot files).</summary>
    internal static List<string> MigrationIds(string root) =>
        Directory.GetFiles(Path.Combine(root, "PEMS.Migrations", "Migrations"), "*.cs")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(n => !n.EndsWith(".Designer", StringComparison.Ordinal) && Regex.IsMatch(n, @"^\d{14}_"))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The migrations after the audit that the manifest does not name.</summary>
    internal static List<string> UncoveredMigrations(IEnumerable<string> ids, string manifest) =>
        ids.Where(id => string.CompareOrdinal(id[..14], AuditedUpTo) > 0 && !manifest.Contains(id, StringComparison.Ordinal)).ToList();

    /// <summary>The kinds of the shipped definition folders that the manifest never names.</summary>
    internal static List<string> UncoveredKinds(IEnumerable<string> kinds, string manifest) =>
        kinds.Where(k => !Regex.IsMatch(manifest, @"\b" + Regex.Escape(k) + @"\b")).ToList();

    [Fact]
    public void Every_migration_after_the_audit_is_named_in_the_change_manifest()
    {
        var uncovered = UncoveredMigrations(MigrationIds(Root()), Manifest());
        Assert.True(uncovered.Count == 0, "Add a row to plan/sheets/change-manifest-audit.md (migration, feed, definitions, how it reaches a customer) for: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void Every_shipped_definition_kind_is_named_in_the_change_manifest()
    {
        var package = Path.Combine(Root(), "PEMS.Persistence", "Definitions", "System");
        var kinds = Directory.GetDirectories(package).Select(Path.GetFileName).OfType<string>()
            .Where(k => Directory.EnumerateFiles(Path.Combine(package, k), "*.json", SearchOption.AllDirectories).Any()).ToList();
        Assert.NotEmpty(kinds);
        var uncovered = UncoveredKinds(kinds, Manifest());
        Assert.True(uncovered.Count == 0, "plan/sheets/change-manifest-audit.md does not name the definition kind(s): " + string.Join(", ", uncovered));
    }

    [Fact]
    public void The_check_fails_for_an_uncovered_migration_and_kind()
    {
        const string manifest = "migration `20261119190301_Scrum591DefinitionGrants`; definitions: page, form";
        Assert.Equal(["20261201000000_Later"], UncoveredMigrations(["20261119190301_Scrum591DefinitionGrants", "20261201000000_Later", "20261028111608_DefinitionStore"], manifest));
        Assert.Equal(["route"], UncoveredKinds(["page", "form", "route"], manifest));
    }
}
