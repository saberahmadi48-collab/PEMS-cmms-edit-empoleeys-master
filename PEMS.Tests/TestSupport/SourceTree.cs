using System.Collections.Concurrent;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// SCRUM-393: the source-scanning tests (views, scripts, controllers) all walk the same tree. The file lists and the file texts are read once per test run
/// and shared, instead of once per test class; bin and obj folders are never part of the tree. Files are read only (no test writes into the source tree).
/// </summary>
public static class SourceTree
{
    private static readonly ConcurrentDictionary<string, Lazy<string[]>> Lists = new();
    private static readonly ConcurrentDictionary<string, Lazy<string>> Texts = new();

    /// <summary>The files below <paramref name="dir"/> that match the pattern (e.g. <c>*.cshtml</c>), without bin and obj; an empty list when the folder does not exist.</summary>
    public static IReadOnlyList<string> Files(string dir, string pattern = "*.cshtml")
        => Lists.GetOrAdd(dir + "|" + pattern, _ => new Lazy<string[]>(() => !Directory.Exists(dir)
            ? []
            : Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToArray())).Value;

    /// <summary>The text of a file, read once per run.</summary>
    public static string Text(string file) => Texts.GetOrAdd(file, f => new Lazy<string>(() => File.ReadAllText(f))).Value;
}
