using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Manifest;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-54: the grid component writes its configuration (columns, endpoint, every text in the language of the request); the script builds all markup from registered templates and uses logical CSS only.</summary>
public sealed class DataGridTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static DataGridOptions Sample()
    {
        var sample = ComponentSamples.Get("data-grid")!.ToJsonString();
        return System.Text.Json.JsonSerializer.Deserialize<DataGridOptions>(sample, PEMS.UI.Abstractions.PemsJson.Options)!;
    }

    private static string Render(DataGridOptions options)
    {
        var context = new TagHelperContext("pems-data-grid", new TagHelperAttributeList(), new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput("pems-data-grid", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        new PemsDataGridTagHelper(UiTestRenderer.Instance) { Options = options }.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static JsonObject Config(string html)
    {
        var start = html.IndexOf("data-config=\"", StringComparison.Ordinal) + "data-config=\"".Length;
        var json = System.Net.WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
        return (JsonObject)JsonNode.Parse(json)!;
    }

    [Fact]
    public void The_grid_is_one_element_with_its_configuration_and_nothing_else_the_script_builds_the_rest()
    {
        var html = Render(Sample());
        Assert.StartsWith("<div class=\"pems-grid\" id=\"items\" data-pems-grid=\"\" data-config=\"", html);
        var cfg = Config(html);
        Assert.Equal("/Items/Grid", (string?)cfg["url"]);
        Assert.Equal(4, ((JsonArray)cfg["columns"]!).Count);
        Assert.Equal("start", (string?)cfg["columns"]![0]!["pinned"]);
        Assert.True((bool)cfg["selectable"]!);
        Assert.Equal("items", (string?)cfg["viewsKey"]);
        Assert.Equal(100, (int)cfg["pageSize"]!);
    }

    [Fact]
    public void Every_text_of_the_configuration_follows_the_language_of_the_render()
    {
        var options = Sample();
        var fa = UiTestRenderer.Instance.RenderAsync("data-grid", new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo("fa")).GetAwaiter().GetResult();
        var en = UiTestRenderer.Instance.RenderAsync("data-grid", new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo("en")).GetAwaiter().GetResult();
        Assert.Equal("Search", (string?)Config(en)["texts"]!["search"]);
        Assert.Equal("Code", (string?)Config(en)["columns"]![0]!["header"]);
        Assert.Equal("جستجو", (string?)Config(fa)["texts"]!["search"]);
        Assert.Equal("کد", (string?)Config(fa)["columns"]![0]!["header"]);
        Assert.Equal("{0} ردیف", (string?)Config(fa)["texts"]!["rows"]);
        Assert.Equal("Close the selected items?", (string?)Config(en)["bulkActions"]![0]!["confirm"]);
    }

    [Fact]
    public void The_options_are_checked_by_the_manifest_so_a_definition_cannot_name_an_unknown_property()
    {
        var catalog = new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry());
        var ok = ComponentSamples.Get("data-grid")!.ToJsonString();
        Assert.True(catalog.Validate("data-grid", ok).IsValid);
        var bad = JsonNode.Parse(ok)!.AsObject();
        bad["nonsense"] = 1;
        Assert.False(catalog.Validate("data-grid", bad.ToJsonString()).IsValid);
        bad.Remove("nonsense"); bad.Remove("columns");
        Assert.False(catalog.Validate("data-grid", bad.ToJsonString()).IsValid);   // columns are required
    }

    [Fact]
    public void An_actions_column_carries_its_row_actions_in_the_configuration_in_the_language_of_the_render()
    {
        var sample = Sample();
        var withActions = sample with
        {
            Columns = [.. sample.Columns, new GridColumn
            {
                Key = "_actions", Header = PEMS.UI.Abstractions.TranslatableText.Of(("en", "Actions"), ("fa", "عملیات")), Type = "actions", Sortable = false, Filterable = false,
                Actions =
                [
                    new GridRowAction { Key = "edit", Label = PEMS.UI.Abstractions.TranslatableText.Of(("en", "Edit"), ("fa", "ویرایش")), Url = "/f/x-edit/{id}" },
                    new GridRowAction { Key = "del", Label = PEMS.UI.Abstractions.TranslatableText.Of(("en", "Delete"), ("fa", "حذف")), Url = "/a/x-delete", Method = "post", Tone = "danger", ShowWhen = "row.system == false",
                        Confirm = PEMS.UI.Abstractions.TranslatableText.Of(("en", "Delete?"), ("fa", "حذف شود؟")) }
                ]
            }]
        };
        var en = Config(Render(withActions));
        var column = ((JsonArray)en["columns"]!).Last()!;
        Assert.Equal("actions", (string?)column["type"]);
        Assert.Equal(("Edit", "get", "/f/x-edit/{id}"), (((string?)column["actions"]![0]!["label"])!, ((string?)column["actions"]![0]!["method"])!, ((string?)column["actions"]![0]!["url"])!));
        Assert.Equal(("post", "danger", "row.system == false", "Delete?"), (((string?)column["actions"]![1]!["method"])!, ((string?)column["actions"]![1]!["tone"])!, ((string?)column["actions"]![1]!["showWhen"])!, ((string?)column["actions"]![1]!["confirm"])!));
        Assert.Equal("ویرایش", (string?)((JsonArray)Config(RenderIn(withActions, "fa"))["columns"]!).Last()!["actions"]![0]!["label"]);
        var js = Read("PEMS.UI", "wwwroot", "pems-grid.js");
        foreach (var feature in new[] { "runRowAction", "application/json", "showWhen", "dg-row-button" }) Assert.Contains(feature, js);
        Assert.Contains("actions: c.actions", js);                                  // the column keeps its actions when the script copies the columns
        Assert.True(new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry()).Validate("data-grid", System.Text.Json.JsonSerializer.Serialize(withActions, PEMS.UI.Abstractions.PemsJson.Options)).IsValid);
    }

    private static string RenderIn(DataGridOptions options, string culture)
        => UiTestRenderer.Instance.RenderAsync("data-grid", new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    [Fact]
    public void The_script_builds_markup_only_from_registered_templates_and_the_sheet_uses_logical_properties()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-grid.js");
        Assert.DoesNotContain("createElement", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotMatch("https?://", js);
        foreach (var name in new[] { "dg-root", "dg-hcell", "dg-fcell", "dg-row", "dg-cell", "dg-check", "dg-menu-item", "dg-view-item", "dg-bulk-action", "dg-cell-actions", "dg-row-link", "dg-row-button" })
        {
            Assert.Contains("'" + name + "'", js);
            Assert.Contains($"data-pems-template=\"{name}\"", Read("PEMS.UI", "Components", "PemsScriptTemplates.razor"));
        }
        foreach (var feature in new[] { "RequestVerificationToken", "allMatching", "viewsKey", "localStorage", "OVERSCAN", "pageSize" }) Assert.Contains(feature, js);
        var css = Read("PEMS.UI", "wwwroot", "pems-grid.css");
        Assert.DoesNotMatch(@"(margin|padding|border)-(left|right)\s*:|(?<![\w-])(left|right)\s*:", css);
        Assert.DoesNotMatch("#[0-9a-fA-F]{3,8}\\b", css);   // theme tokens only
    }

    [Fact]
    public void The_layout_loads_the_sheet_and_the_script_after_the_template_script()
    {
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        Assert.Contains("pems-grid.css", layout);
        Assert.True(layout.IndexOf("pems-template.js", StringComparison.Ordinal) < layout.IndexOf("pems-grid.js", StringComparison.Ordinal));
    }
}
