using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-580: the uploader is a real file input with its configuration in the language of the request; the script builds its markup from templates; the options are checked by the manifest.</summary>
public sealed class UploaderTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static UploaderOptions Sample() => System.Text.Json.JsonSerializer.Deserialize<UploaderOptions>(ComponentSamples.Get("uploader")!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;

    private static string Render(UploaderOptions o, string culture)
        => UiTestRenderer.Instance.RenderAsync("uploader", new Dictionary<string, object?> { ["Options"] = o }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    private static JsonObject Config(string html)
    {
        var start = html.IndexOf("data-config=\"", StringComparison.Ordinal) + "data-config=\"".Length;
        return (JsonObject)JsonNode.Parse(System.Net.WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]))!;
    }

    [Fact]
    public void It_writes_a_real_file_input_that_posts_with_the_form()
    {
        var html = Render(Sample(), "en");
        Assert.StartsWith("<div class=\"pems-upload\" data-pems-upload", html);
        Assert.Contains("type=\"file\"", html);
        Assert.Contains("name=\"file\"", html);
        Assert.Contains("id=\"importFile\"", html);
        Assert.Contains("accept=\".xlsx,.xlsm\"", html);
        Assert.Contains("required", html);
        Assert.Contains("form-control form-control-sm", html);
        Assert.DoesNotContain(" multiple", html);
        Assert.Contains(" multiple", Render(Sample() with { Multiple = true }, "en"));
    }

    [Fact]
    public void The_configuration_carries_the_limit_the_types_and_the_texts_of_the_request_language()
    {
        var en = Config(Render(Sample(), "en"));
        var fa = Config(Render(Sample(), "fa"));
        Assert.Equal(25L * 1024 * 1024, (long)en["maxBytes"]!);
        Assert.Equal(25, (int)en["maxMb"]!);
        Assert.Equal(".xlsx,.xlsm", (string?)en["accept"]);
        Assert.Equal("choose", (string?)en["texts"]!["choose"]);
        Assert.Equal("انتخاب کنید", (string?)fa["texts"]!["choose"]);
        Assert.Contains("{1} MB", (string?)en["texts"]!["tooBig"]);
    }

    [Fact]
    public void Without_a_limit_the_configuration_has_none_and_the_page_texts_win_over_the_defaults()
    {
        var cfg = Config(Render(Sample() with { MaxMb = null, Texts = UploaderTexts.Default with { Choose = "Browse" } }, "en"));
        Assert.Null(cfg["maxBytes"]);
        Assert.Equal("Browse", (string?)cfg["texts"]!["choose"]);
    }

    [Fact]
    public void The_options_are_checked_by_the_manifest()
    {
        var catalog = new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry());
        var ok = ComponentSamples.Get("uploader")!.ToJsonString();
        Assert.True(catalog.Validate("uploader", ok).IsValid);
        var bad = JsonNode.Parse(ok)!.AsObject();
        bad["nonsense"] = 1;
        Assert.False(catalog.Validate("uploader", bad.ToJsonString()).IsValid);
        bad.Remove("nonsense"); bad.Remove("name");
        Assert.False(catalog.Validate("uploader", bad.ToJsonString()).IsValid);   // the field name is required
    }

    [Fact]
    public void The_script_builds_markup_from_templates_checks_size_and_type_and_the_sheet_uses_logical_properties()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-upload.js");
        Assert.DoesNotContain("createElement", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotMatch("https?://", js);
        var razor = Read("PEMS.UI", "Components", "PemsScriptTemplates.razor");
        foreach (var name in new[] { "up-root", "up-file" })
        {
            Assert.Contains("'" + name + "'", js);
            Assert.Contains($"data-pems-template=\"{name}\"", razor);
        }
        foreach (var feature in new[] { "setCustomValidity", "DataTransfer", "dragover", "drop", "maxBytes", "wrongType" }) Assert.Contains(feature, js);
        var css = Read("PEMS.UI", "wwwroot", "pems-views.css");
        Assert.DoesNotMatch(@"(margin|padding|border)-(left|right)\s*:|(?<![\w-])(left|right)\s*:", css);
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        Assert.True(layout.IndexOf("pems-template.js", StringComparison.Ordinal) < layout.IndexOf("pems-upload.js", StringComparison.Ordinal));
    }

    [Fact]
    public void No_view_places_a_file_input_by_hand_except_the_registered_exceptions()
    {
        // the hidden inputs a page script drives itself (DCC register, letter compose) are the only exceptions
        var views = Directory.GetFiles(Path.Combine(Root(), "PEMS.Web"), "*.cshtml", SearchOption.AllDirectories);
        var offenders = views.SelectMany(f => File.ReadLines(f).Select((l, i) => (f, l, i)))
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.l, "<(pems-input|input)\\b[^>]*\\btype=\"file\"") && !x.l.Contains("hidden"))
            .Select(x => Path.GetRelativePath(Root(), x.f) + ":" + (x.i + 1)).ToList();
        Assert.True(offenders.Count == 0, "use <pems-uploader>: " + string.Join(", ", offenders));
    }
}
