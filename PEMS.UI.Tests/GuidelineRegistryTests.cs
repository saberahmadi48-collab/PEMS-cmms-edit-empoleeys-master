using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>
/// Library-first protocol (owner decision 2026-11-02): an agent finds a component by reading chapter 02 of the guideline, so a tag helper or a registered
/// component that is missing there is a defect. The chapter may group a family with a wildcard (<c>pems-list-*</c>).
/// </summary>
public sealed class GuidelineRegistryTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Chapter() => File.ReadAllText(Path.Combine(Root(), "docs", "guidelines", "02-ui-and-components.md"));

    private static bool Mentioned(string chapter, string name)
    {
        if (chapter.Contains(name, StringComparison.Ordinal)) return true;
        var cut = name.LastIndexOf('-');
        while (cut > "pems".Length)
        {
            if (chapter.Contains(name[..cut] + "-*", StringComparison.Ordinal)) return true;
            cut = name.LastIndexOf('-', cut - 1);
        }
        return false;
    }

    [Fact]
    public void Every_pems_tag_helper_is_listed_in_the_ui_chapter()
    {
        var chapter = Chapter();
        var names = Directory.EnumerateFiles(Path.Combine(Root(), "PEMS.UI", "TagHelpers"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"HtmlTargetElement\(""(pems-[a-z-]+)""").Select(m => m.Groups[1].Value))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(names);
        var missing = names.Where(n => !Mentioned(chapter, n)).ToList();
        Assert.True(missing.Count == 0, "Add these tag helpers to the component registry of docs/guidelines/02-ui-and-components.md: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_registered_component_key_is_listed_in_the_ui_chapter()
    {
        using var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var chapter = Chapter();
        var missing = provider.GetRequiredService<IPemsComponentRegistry>().Keys.Where(k => !chapter.Contains($"`{k}`", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Add these component keys to the component registry of docs/guidelines/02-ui-and-components.md: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_chapter_states_the_library_first_protocol()
        => Assert.Contains("Library-first protocol for agents", Chapter(), StringComparison.Ordinal);
}
