using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 TreeView: <c>pems-tree</c> / <c>pems-tree-item</c> / <c>pems-tree-label</c> (a WAI-ARIA tree), its script, and the
/// first trees built from it (wave T1: the organization positions tree and the correspondence tag tree).
/// </summary>
public class TreeViewComponentTests
{
    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    private static async Task<string> RunItem(PemsTreeItemTagHelper helper, bool withChild, string? labelHtml = null, string toggleLabel = "Expand or collapse")
    {
        // the enclosing tree's context, as <pems-tree> puts it in Items before its children run
        var items = new Dictionary<object, object> { [typeof(PemsTreeContext)] = new PemsTreeContext { ToggleLabel = toggleLabel } };
        var context = new TagHelperContext("pems-tree-item", new TagHelperAttributeList(), items, "u1");
        var output = new TagHelperOutput("pems-tree-item", new TagHelperAttributeList(), (_, _) =>
        {
            // what a nested item or a <pems-tree-label> tells its parent while the child content runs
            var mine = (PemsTreeItemContext)context.Items[typeof(PemsTreeItemContext)];
            if (withChild) mine.HasChildren = true;
            if (labelHtml != null) mine.LabelHtml = labelHtml;
            return Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(withChild ? "<li>child</li>" : ""));
        });
        helper.Init(context);
        await helper.ProcessAsync(context, output);
        using var w = new StringWriter();
        output.WriteTo(w, HtmlEncoder.Default);
        return w.ToString();
    }

    [Fact]
    public async Task Leaf_IsATreeItemWithALink_AndNoToggle()
    {
        var html = await RunItem(new PemsTreeItemTagHelper(Renderer) { Label = "Electrical", Key = "2", Href = "/Letter?tagId=2", Current = true, Count = 4 }, withChild: false);
        Assert.StartsWith("<li class=\"pems-tree__item\" role=\"treeitem\" data-key=\"2\" data-current=\"\">", html);
        Assert.DoesNotContain("aria-expanded", html);
        Assert.DoesNotContain("pems-tree__toggle", html);
        Assert.Contains("<span aria-hidden=\"true\" class=\"pems-tree__spacer\"></span>", html);
        Assert.Contains("<a aria-current=\"page\" class=\"pems-tree__link\" href=\"/Letter?tagId=2\">Electrical<span class=\"pems-tree__count\">4</span></a>", html);
    }

    [Fact]
    public async Task Branch_HasANamedToggle_AndAGroup_OpenByDefault()
    {
        var html = await RunItem(new PemsTreeItemTagHelper(Renderer) { Label = "Engineering", Key = "1" }, withChild: true);
        Assert.Contains("role=\"treeitem\" data-key=\"1\" aria-expanded=\"true\"", html);
        Assert.Contains("<button aria-expanded=\"true\" aria-label=\"Expand or collapse: Engineering\" class=\"pems-tree__toggle\" title=\"Expand or collapse: Engineering\" type=\"button\">", html);
        Assert.Contains("<ul class=\"pems-tree__group\" role=\"group\"><li>child</li></ul>", html);
        Assert.Contains("<span class=\"pems-tree__text\">Engineering</span>", html);   // no href: plain text
    }

    [Fact]
    public async Task Branch_ClosedOnTheServer_HidesItsGroup()
    {
        var html = await RunItem(new PemsTreeItemTagHelper(Renderer) { Label = "Old", Expanded = false }, withChild: true);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("<ul class=\"pems-tree__group\" hidden=\"hidden\" role=\"group\">", html);
    }

    [Fact]
    public async Task Label_ReplacesTheText_AndThePagesLinkClassIsKept()
    {
        var html = await RunItem(new PemsTreeItemTagHelper(Renderer) { Label = "Mine", Href = "/x", LinkClass = "ct-tag" }, withChild: false, labelHtml: "<span class=\"dot\"></span>Mine");
        Assert.Contains("<a class=\"pems-tree__link ct-tag\" href=\"/x\"><span class=\"dot\"></span>Mine</a>", html);
    }

    [Fact]
    public void Script_And_Styles_FollowTheTreePattern()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-tree.js"));
        foreach (var k in new[] { "'ArrowDown'", "'ArrowUp'", "'ArrowRight'", "'ArrowLeft'", "'Home'", "'End'", "'Enter'", "data-pems-tree-remember", "[data-current]", "localStorage", "direction === 'rtl'" })
            Assert.Contains(k, js);
        Assert.DoesNotContain("innerHTML", js);
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-ui.css"));
        Assert.Contains(".pems-tree__group { margin-inline-start:", css);
        Assert.Contains("[dir=\"rtl\"] .pems-tree__toggle .bi", css);
        foreach (var layout in new[] { "_Layout.cshtml", "_LayoutEmbed.cshtml" })
            Assert.Contains("_content/PEMS.UI/pems-tree.js", File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", layout)));
    }

    [Fact]
    public void WaveT1_TheTwoTreesUseTheComponent()
    {
        var letter = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Letter", "Index.cshtml"));
        Assert.Contains("remember-key=\"pems.cartable.tagsCollapsed\"", letter);   // the same key: what a user collapsed stays collapsed
        Assert.DoesNotContain("role=\"treeitem\"", letter);
        var cartable = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "js", "pems-cartable.js"));
        Assert.DoesNotContain("data-tag-toggle", cartable);
    }

    // ---- T2: the help node tree
    [Fact]
    public async Task WaveT2_HelpTree_IsARealNestedTree_WithEdgeLabels()
    {
        var visual = new PEMS.Application.DTOs.HelpVisualRenderDto
        {
            VisualType = "DecisionTree", Caption = "Approve or return",
            Nodes =
            {
                new() { Id = "a", Label = "Submitted", IconCssClass = "bi-inbox" },
                new() { Id = "b", Label = "Approved", IconCssClass = "bi-check" },
                new() { Id = "c", Label = "Returned", IconCssClass = "bi-arrow-left" }
            },
            Edges = { new() { FromNodeId = "a", ToNodeId = "b", Label = "OK" }, new() { FromNodeId = "a", ToNodeId = "c", Label = "Comments" } }
        };
        var controller = PEMS.Tests.TestSupport.ControllerTempDataExtensions.WithTempData(new PEMS.Web.Controllers.DCCController(null!, null!, null!, null!, null!));
        var result = new Microsoft.AspNetCore.Mvc.ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(controller.ViewData) { Model = visual } };
        var html = await PEMS.Tests.TestSupport.RazorViewRenderer.RenderAsync(controller, result, "~/Views/Shared/_HelpNodeTree.cshtml");

        Assert.Contains("<ul class=\"pems-tree\" role=\"tree\" aria-label=\"Approve or return\"", html);
        var root = Regex.Match(html, "<li class=\"pems-tree__item\" role=\"treeitem\" aria-expanded=\"true\">(.*)</li>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("pems-tree__toggle", root);                              // Submitted has two children
        Assert.Contains("<ul class=\"pems-tree__group\" role=\"group\">", root);
        Assert.Equal(3, Regex.Matches(html, "role=\"treeitem\"").Count);
        Assert.Contains("pems-help-tree-edge-label", html);
        Assert.Contains("Comments", html);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_HelpNodeTreeItem.cshtml")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
