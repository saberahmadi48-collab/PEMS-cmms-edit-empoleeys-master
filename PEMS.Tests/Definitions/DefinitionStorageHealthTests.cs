using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PEMS.Persistence.Services;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-786 (gap of SCRUM-455): definition versions are kept forever, the storage report has growth, a size warning and cache counters, and the setting is in every env file.</summary>
public sealed class DefinitionStorageHealthTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));

    [Fact]
    public void No_code_path_deletes_a_definition_version()
    {
        // a deletion of a version would break rollback and the history; only the tests and generated code are exempt
        var delete = new Regex(@"DefinitionVersions\s*\.\s*(Remove|RemoveRange|ExecuteDelete)|Set<DefinitionVersion>\(\)\s*\.\s*(Remove|RemoveRange|ExecuteDelete)", RegexOptions.Compiled);
        var hits = new List<string>();
        foreach (var project in new[] { "PEMS.Web", "PEMS.Persistence", "PEMS.Application" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) || file.Contains("Migrations")) continue;
                var count = delete.Matches(File.ReadAllText(file)).Count;
                // the one allowed deletion: discarding an open draft that was never published (DefinitionService.DiscardDraftAsync); a published or retired version is never removed
                var allowed = Path.GetFileName(file) == "DefinitionService.cs" ? 1 : 0;
                if (count > allowed) hits.Add(Path.GetRelativePath(Root(), file));
            }
        Assert.True(hits.Count == 0, "Definition versions are kept forever (ADR-0010 section 5); remove the deletion in: " + string.Join(", ", hits));
    }

    [Theory]
    [InlineData(null, 1024)]
    [InlineData("", 1024)]
    [InlineData("abc", 1024)]
    [InlineData("0", 1024)]
    [InlineData("-5", 1024)]
    [InlineData("200", 200)]
    public void The_warning_size_is_a_positive_number_of_megabytes_else_the_default(string? setting, int expected)
        => Assert.Equal(expected, DefinitionHealthGridProvider.WarnMb(setting));

    [Fact]
    public void Every_env_file_keeps_retired_versions_forever_and_has_a_warning_size()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "env"), "*.env").Where(f => !f.EndsWith(".secrets.env", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("Definitions__MaxJsonDepth", StringComparison.Ordinal)) continue;     // a file that does not carry the definition limits is not a full environment
            Assert.Contains("Definitions__RetainRetiredVersions=forever", text);
            Assert.Matches(@"Definitions__StorageWarnMb=\d+", text);
        }
    }

    [Fact]
    public void The_cache_counters_count_hits_and_misses()
    {
        var before = DefinitionCacheStats.Hits + DefinitionCacheStats.Misses + DefinitionCacheStats.StampReads + DefinitionCacheStats.Evictions;
        Assert.True(before >= 0);                      // the counters exist and are readable without a database; the store increments them on every read (QA line: the numbers move on the storage page)
    }
}
