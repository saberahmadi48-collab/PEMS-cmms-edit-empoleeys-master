using System.Text.Json;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// SCRUM-280: reader for <c>deploy/db/supported-releases.json</c> (the database states PEMS must upgrade from) and for the
/// migration files of the current chain. A release = every migration up to <see cref="Release.LastMigration"/> minus
/// <see cref="Release.NotIncluded"/> (migrations merged later with an older timestamp).
/// </summary>
public static class SupportedReleases
{
    public sealed record Release(string Name, string Commit, string LastMigration, IReadOnlyList<string> NotIncluded)
    {
        /// <summary>The migrations a database of this release has applied, in chain order.</summary>
        public IReadOnlyList<string> MigrationSet(IEnumerable<string> chain)
            => chain.Where(m => string.CompareOrdinal(m, LastMigration) <= 0 && !NotIncluded.Contains(m)).ToList();

        public override string ToString() => Name;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (PEMS.slnx) not found above " + AppContext.BaseDirectory);
    }

    public static string ManifestPath() => Path.Combine(RepoRoot(), "deploy", "db", "supported-releases.json");

    public static string ChecksumPath() => Path.Combine(RepoRoot(), "deploy", "db", "released-migrations.sha256");

    public static string MigrationsDirectory() => Path.Combine(RepoRoot(), "PEMS.Migrations", "Migrations");

    public static IReadOnlyList<Release> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        return doc.RootElement.GetProperty("releases").EnumerateArray()
            .Select(r => new Release(
                r.GetProperty("name").GetString()!,
                r.GetProperty("commit").GetString()!,
                r.GetProperty("lastMigration").GetString()!,
                r.GetProperty("notIncluded").EnumerateArray().Select(x => x.GetString()!).ToList()))
            .ToList();
    }

    /// <summary>Migration ids of the source tree (file names "yyyyMMddHHmmss_Name.cs", Designer files excluded), sorted.</summary>
    public static IReadOnlyList<string> ChainFromFiles()
        => Directory.GetFiles(MigrationsDirectory(), "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && n.Length > 15 && n[..14].All(char.IsDigit) && n[14] == '_' && !n.EndsWith(".Designer", StringComparison.Ordinal))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>xUnit MemberData: one row per supported release.</summary>
    public static IEnumerable<object[]> TheoryData() => Load().Select(r => new object[] { r.Name });
}
