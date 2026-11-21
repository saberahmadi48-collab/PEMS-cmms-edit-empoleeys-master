using System.Security.Cryptography;
using System.Text;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-280 (#262): schema compatibility rules that need no database (docs/guidelines/01 section 3, CODE_FIRST_DATABASE.md).
/// - the supported-release manifest only names migrations of this chain;
/// - a migration shipped in a supported release is never edited (released-migrations.sha256);
/// - migration ids are well formed and unique, and every migration has its Designer file;
/// - the pre-update backup file name follows the server's path style.
/// </summary>
public class MigrationCompatibilityRulesTests
{
    [Fact]
    public void Manifest_names_releases_whose_migrations_are_all_in_the_chain()
    {
        var chain = SupportedReleases.ChainFromFiles();
        var releases = SupportedReleases.Load();
        Assert.True(releases.Count >= 2, "keep at least the last releases that reached a database (N-1, N-2) in deploy/db/supported-releases.json");
        Assert.Equal(releases.Count, releases.Select(r => r.Name).Distinct().Count());

        foreach (var r in releases)
        {
            Assert.True(chain.Contains(r.LastMigration), $"{r.Name}: lastMigration {r.LastMigration} is not in the chain (a released migration was removed or renamed)");
            foreach (var n in r.NotIncluded)
            {
                Assert.True(chain.Contains(n), $"{r.Name}: notIncluded {n} is not in the chain");
                Assert.True(string.CompareOrdinal(n, r.LastMigration) < 0, $"{r.Name}: notIncluded {n} is newer than lastMigration; list only older-timestamp migrations merged later");
            }
        }

        // releases are listed oldest first, each newer than the previous
        var lasts = releases.Select(r => r.LastMigration).ToList();
        Assert.Equal(lasts.OrderBy(m => m, StringComparer.Ordinal), lasts);
    }

    [Fact]
    public void Released_migrations_are_never_edited()
    {
        var chain = SupportedReleases.ChainFromFiles();
        var released = SupportedReleases.Load().SelectMany(r => r.MigrationSet(chain)).ToHashSet(StringComparer.Ordinal);

        var recorded = File.ReadAllLines(SupportedReleases.ChecksumPath())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split("  ", 2))
            .ToDictionary(p => p[1].Trim(), p => p[0].Trim(), StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var id in released.OrderBy(m => m, StringComparer.Ordinal))
        {
            var actual = Hash(Path.Combine(SupportedReleases.MigrationsDirectory(), id + ".cs"));
            if (!recorded.TryGetValue(id, out var expected))
                problems.Add($"missing line (add it): {actual}  {id}");
            else if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{id} was edited after it shipped; restore it and fix the problem with a new migration");
        }
        foreach (var id in recorded.Keys.Where(k => !released.Contains(k)))
            problems.Add($"{id} is recorded but belongs to no supported release (remove the line, or the release was dropped from the manifest by mistake)");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Migration_ids_are_well_formed_unique_and_have_a_designer_file()
    {
        var dir = SupportedReleases.MigrationsDirectory();
        var chain = SupportedReleases.ChainFromFiles();
        Assert.True(chain.Count > 100);
        Assert.Equal(chain.Count, chain.Select(m => m[..14]).Distinct().Count());   // two migrations with one timestamp apply in an undefined order
        foreach (var id in chain)
        {
            Assert.Matches(@"^20\d{12}_[A-Za-z][A-Za-z0-9_]*$", id);
            Assert.True(File.Exists(Path.Combine(dir, id + ".Designer.cs")), $"{id} has no Designer file (EF would not discover it)");
        }
    }

    [Theory]
    [InlineData(@"C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup", "PEMSDB", @"C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\PEMSDB_preupdate_20261027_101500.bak")]
    [InlineData(@"D:\Backups\", "PEMSDB", @"D:\Backups\PEMSDB_preupdate_20261027_101500.bak")]
    [InlineData("/var/opt/mssql/data/", "PEMS", "/var/opt/mssql/data/PEMS_preupdate_20261027_101500.bak")]
    [InlineData("/backups", "my db;x", "/backups/my_db_x_preupdate_20261027_101500.bak")]
    public void Backup_file_follows_the_server_folder_style_and_a_safe_name(string folder, string database, string expected)
        => Assert.Equal(expected, DatabaseUpdateService.BackupFilePath(folder, database, new DateTime(2026, 10, 27, 10, 15, 0, DateTimeKind.Utc)));

    /// <summary>Same normalisation as the file header says: no BOM, LF line ends (a Windows and a Linux checkout agree).</summary>
    private static string Hash(string path)
    {
        var text = File.ReadAllText(path, new UTF8Encoding(false)).TrimStart('\uFEFF').Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
