using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-55: the view types are one element with a configuration in the language of the request; the script builds all markup from registered templates; the options are checked by the manifest.</summary>
public sealed class DataViewTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static DataViewOptions Sample(string? view = null)
    {
        var node = ComponentSamples.Get("data-view")!.AsObject();
        if (view is not null) node["view"] = view;
        return System.Text.Json.JsonSerializer.Deserialize<DataViewOptions>(node.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;
    }

    private static JsonObject Config(string html)
    {
        var start = html.IndexOf("data-config=\"", StringComparison.Ordinal) + "data-config=\"".Length;
        return (JsonObject)JsonNode.Parse(System.Net.WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]))!;
    }

    private static string Render(DataViewOptions o, string culture)
        => UiTestRenderer.Instance.RenderAsync("data-view", new Dictionary<string, object?> { ["Options"] = o }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    [Theory]
    [InlineData("cards")]
    [InlineData("kanban")]
    [InlineData("calendar")]
    [InlineData("timeline")]
    [InlineData("gantt")]
    [InlineData("tree")]
    public void Each_view_type_is_one_element_named_by_its_type_with_its_configuration(string view)
    {
        var html = Render(Sample(view), "en");
        Assert.StartsWith($"<div class=\"pems-view pems-view--{view}\" id=\"tasks-board\" data-pems-view=\"\" data-config=\"", html);
        var cfg = Config(html);
        Assert.Equal(view, (string?)cfg["view"]);
        Assert.Equal("/Tasks/Grid", (string?)cfg["url"]);
        Assert.Equal("due", (string?)cfg["fields"]!["date"]);
        Assert.Equal("status", (string?)cfg["fields"]!["group"]);
        Assert.Equal(3, ((JsonArray)cfg["groups"]!).Count);
        Assert.Equal(6, (int)cfg["weekStart"]!);
    }

    [Fact]
    public void Texts_and_the_locale_follow_the_language_of_the_render()
    {
        var en = Config(Render(Sample(), "en"));
        var fa = Config(Render(Sample(), "fa"));
        Assert.Equal(("Today", "en"), (((string?)en["texts"]!["today"])!, (string?)en["locale"]));
        Assert.Equal(("امروز", "fa"), (((string?)fa["texts"]!["today"])!, (string?)fa["locale"]));
        Assert.Equal("In progress", (string?)en["groups"]![1]!["label"]);
        Assert.Equal("در حال انجام", (string?)fa["groups"]![1]!["label"]);
        Assert.Equal("{0}+ مورد دیگر", (string?)fa["texts"]!["moreItems"]);
    }

    [Fact]
    public void The_options_are_checked_by_the_manifest()
    {
        var catalog = new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry());
        var ok = ComponentSamples.Get("data-view")!.ToJsonString();
        Assert.True(catalog.Validate("data-view", ok).IsValid);
        var bad = JsonNode.Parse(ok)!.AsObject();
        bad["nonsense"] = 1;
        Assert.False(catalog.Validate("data-view", bad.ToJsonString()).IsValid);
        bad.Remove("nonsense"); bad.Remove("fields");
        Assert.False(catalog.Validate("data-view", bad.ToJsonString()).IsValid);   // the fields are required
    }

    [Fact]
    public void The_script_builds_markup_from_templates_reads_the_grid_endpoint_and_the_sheet_uses_logical_properties()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-views.js");
        Assert.DoesNotContain("createElement", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotMatch("https?://", js);
        var razor = Read("PEMS.UI", "Components", "PemsScriptTemplates.razor");
        foreach (var name in new[] { "dv-root", "dv-card", "dv-col", "dv-cal", "dv-cal-day", "dv-chip", "dv-tl-day", "dv-more", "dv-gantt", "dv-gantt-row", "dv-gantt-tick", "dv-tree", "dv-tree-node" })
        {
            Assert.Contains("'" + name + "'", js);
            Assert.Contains($"data-pems-template=\"{name}\"", razor);
        }
        foreach (var feature in new[] { "range:", "RequestVerificationToken", "persian", "weekStart", "draggable", "altKey", "showGantt", "showTree", "aria-expanded", "setProperty('--from'", "MAX_DAYS" }) Assert.Contains(feature, js);
        var css = Read("PEMS.UI", "wwwroot", "pems-views.css");
        Assert.DoesNotMatch(@"(margin|padding|border)-(left|right)\s*:|(?<![\w-])(left|right)\s*:", css);
        Assert.DoesNotMatch("#[0-9a-fA-F]{3,8}\\b", css);
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        Assert.Contains("pems-views.css", layout);
        Assert.True(layout.IndexOf("pems-template.js", StringComparison.Ordinal) < layout.IndexOf("pems-views.js", StringComparison.Ordinal));
    }
}
