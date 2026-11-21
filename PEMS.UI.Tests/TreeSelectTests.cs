using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-513: the tree select writes a hidden input, a button, a closed panel with a search box and an empty tree; the script reads the tree one level at a time from the page's endpoint.</summary>
public sealed class TreeSelectTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Render(PemsTreeSelectTagHelper helper)
    {
        var context = new TagHelperContext("pems-tree-select", new TagHelperAttributeList(), new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput("pems-tree-select", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void The_field_posts_the_id_shows_the_selected_text_and_starts_with_a_closed_panel()
    {
        var html = Render(new PemsTreeSelectTagHelper(UiTestRenderer.Instance)
        {
            Name = "FunctionalLocationId", Url = "/FunctionalLocation/TreeNodes", Value = "2", SelectedText = "U100-CW - Cooling water", Placeholder = "Select", SearchLabel = "Search", Css = "mb-2"
        });
        Assert.Contains("pems-tree-select mb-2", html);
        Assert.Contains("data-url=\"/FunctionalLocation/TreeNodes\"", html);
        Assert.Contains("id=\"ts_FunctionalLocationId\"", html);
        Assert.Contains("name=\"FunctionalLocationId\"", html);
        Assert.Contains("value=\"2\"", html);
        Assert.Contains(">U100-CW - Cooling water</button>", html);
        Assert.Contains("aria-haspopup=\"tree\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("id=\"ts_FunctionalLocationId_panel\" hidden", html);
        Assert.Contains("role=\"tree\"", html);
        Assert.Contains("aria-label=\"Search\"", html);
    }

    [Fact]
    public void Nothing_selected_shows_the_placeholder_and_a_disabled_field_disables_the_button()
    {
        var html = Render(new PemsTreeSelectTagHelper(UiTestRenderer.Instance) { Name = "A.B", Url = "/x", Placeholder = "Select a location", Disabled = true, Required = true });
        Assert.Contains(">Select a location</button>", html);
        Assert.Contains("id=\"ts_A_B\"", html);
        Assert.Contains("disabled", html);
        Assert.Contains("aria-required=\"true\"", html);
    }

    [Fact]
    public void The_script_pages_a_level_searches_the_whole_tree_and_follows_the_tree_keyboard_pattern()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-tree-select.js");
        foreach (var key in new[] { "ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End", "Enter", "Escape" }) Assert.Contains("'" + key + "'", js);
        Assert.Contains("SEARCH_MIN = 2", js);
        Assert.Contains("data.hasMore", js);
        Assert.Contains("parentId", js);
        Assert.Contains("clone(name)", js);              // the markup is the registered script template, not a string or createElement
        Assert.Contains("'ts-item'", js);
        Assert.DoesNotContain("createElement", js);
        Assert.Contains("'aria-expanded'", js);
        Assert.DoesNotContain("innerHTML", js);          // nodes are built as elements, the text goes in as text
        Assert.DoesNotMatch("https?://", js);
        var css = Read("PEMS.UI", "wwwroot", "pems-select.css");
        Assert.Contains(".pems-tree-select__panel", css);
        Assert.DoesNotMatch(@"(margin|padding)-(left|right)\s*:", css);
    }

    [Fact]
    public void The_node_and_more_row_templates_are_registered_in_the_layout_set()
    {
        var razor = Read("PEMS.UI", "Components", "PemsScriptTemplates.razor");
        Assert.Contains("data-pems-template=\"ts-item\"", razor);
        Assert.Contains("data-pems-template=\"ts-more\"", razor);
    }

    [Fact]
    public void The_layout_loads_the_script_after_the_select_script()
    {
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        Assert.True(layout.IndexOf("pems-select.js", StringComparison.Ordinal) < layout.IndexOf("pems-tree-select.js", StringComparison.Ordinal));
    }
}
