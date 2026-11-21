using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-508: the check / switch row, the pager, the label element and the plain table write the classes the pages wrote by hand.</summary>
public sealed class CheckRowPagerTests
{
    private static string Run(TagHelper helper, string tag, TagHelperAttributeList attributes, string inner = "")
    {
        var context = new TagHelperContext(tag, new TagHelperAttributeList(), new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput(tag, attributes, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(inner)));
        output.Content.SetHtmlContent(inner);
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void An_action_bar_is_a_div_with_the_page_css_around_the_buttons()
    {
        Assert.Equal("<div class=\"mt-4\">x</div>", Run(new PemsActionBarTagHelper(UiTestRenderer.Instance) { Css = "mt-4" }, "pems-action-bar", [], "x"));
    }

    [Fact]
    public void A_form_group_is_a_div_with_the_page_css_around_its_content()
    {
        Assert.Equal("<div class=\"mb-3\">x</div>", Run(new PemsFormGroupTagHelper(UiTestRenderer.Instance) { Css = "mb-3" }, "pems-form-group", [], "x"));
        Assert.Equal("<div>x</div>", Run(new PemsFormGroupTagHelper(UiTestRenderer.Instance), "pems-form-group", [], "x"));
    }

    [Fact]
    public void A_switch_row_is_form_check_form_switch_with_the_page_css()
        => Assert.Equal("<div class=\"form-check form-switch mb-0\">x</div>", Run(new PemsCheckRowTagHelper(UiTestRenderer.Instance) { Switch = true, Css = "mb-0" }, "pems-check-row", [], "x"));

    [Fact]
    public void A_plain_check_row_is_form_check_and_inline_adds_form_check_inline()
    {
        Assert.Equal("<div class=\"form-check\">x</div>", Run(new PemsCheckRowTagHelper(UiTestRenderer.Instance), "pems-check-row", [], "x"));
        Assert.Equal("<div class=\"form-check form-check-inline\">x</div>", Run(new PemsCheckRowTagHelper(UiTestRenderer.Instance) { Inline = true }, "pems-check-row", [], "x"));
    }

    [Fact]
    public void A_pager_writes_a_link_per_page_and_marks_the_current_one()
    {
        var html = Run(new PemsPagerTagHelper(UiTestRenderer.Instance) { Current = 2, Total = 3, Href = "/r?page={page}", Scale = "small", Css = "mb-0" }, "pems-pager", [new("class", "mt-2")]);
        Assert.Contains("<nav class=\"mt-2\">", html);
        Assert.Contains("<ul class=\"pagination pagination-sm mb-0\">", html);
        Assert.Contains("<li class=\"page-item\"><a class=\"page-link\" href=\"/r?page=1\">1</a></li>", html);
        Assert.Contains("<li class=\"page-item active\"><a class=\"page-link\" href=\"/r?page=2\">2</a></li>", html);
        Assert.Contains("href=\"/r?page=3\"", html);
    }

    [Theory]
    [InlineData("span", "<span class=\"form-label d-block\">t</span>")]
    [InlineData("legend", "<legend class=\"form-label d-block\">t</legend>")]
    [InlineData("label", "<label class=\"form-label d-block\">t</label>")]
    [InlineData("script", "<label class=\"form-label d-block\">t</label>")]
    public void A_form_label_can_be_a_span_or_legend_and_never_another_element(string element, string expected)
        => Assert.Equal(expected, Run(new PemsLabelTagHelper(null!, UiTestRenderer.Instance) { As = element, Css = "d-block" }, "pems-label", [], "t"));

    [Fact]
    public void A_plain_table_writes_only_the_page_class()
    {
        Assert.Equal("<table class=\"ct-table\">r</table>", Run(new PemsTableFrameTagHelper(UiTestRenderer.Instance) { Kind = "plain", Css = "ct-table" }, "pems-table", [], "r"));
        Assert.Equal("<table class=\"table table-sm\">r</table>", Run(new PemsTableFrameTagHelper(UiTestRenderer.Instance) { Scale = "small" }, "pems-table", [], "r"));
    }

    [Fact]
    public void A_content_card_keeps_content_card_first_then_the_page_css_and_can_be_a_section_or_details()
    {
        Assert.Equal("<div class=\"content-card mb-3\">x</div>", Run(new PemsContentCardTagHelper(UiTestRenderer.Instance) { Css = "mb-3" }, "pems-content-card", [], "x"));
        Assert.Equal("<section class=\"content-card\" id=\"p\">x</section>", Run(new PemsContentCardTagHelper(UiTestRenderer.Instance) { As = "section" }, "pems-content-card", [new("id", "p")], "x"));
        Assert.Equal("<details class=\"content-card\">x</details>", Run(new PemsContentCardTagHelper(UiTestRenderer.Instance) { As = "details" }, "pems-content-card", [], "x"));
        Assert.Equal("<div class=\"content-card\">x</div>", Run(new PemsContentCardTagHelper(UiTestRenderer.Instance) { As = "script" }, "pems-content-card", [], "x"));
    }

    [Theory]
    [InlineData("admin", "admin-page-shell")]
    [InlineData("workspace", "vs-page")]
    [InlineData("", "vs-page")]
    public void A_page_shell_is_the_admin_or_the_workspace_wrapper(string kind, string expected)
        => Assert.Equal($"<div class=\"{expected} pc-page\">x</div>", Run(new PemsPageShellTagHelper(UiTestRenderer.Instance) { Kind = kind, Css = "pc-page" }, "pems-page-shell", [], "x"));

    [Fact]
    public void A_flex_container_writes_the_utility_classes_in_a_fixed_order_and_the_element_asked_for()
    {
        Assert.Equal("<div class=\"d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3\">x</div>",
            Run(new PemsFlexTagHelper(UiTestRenderer.Instance) { Justify = "between", Align = "center", Wrap = true, Gap = "2", Css = "mb-3" }, "pems-flex", [], "x"));
        Assert.Equal("<div class=\"d-flex flex-column gap-1\">x</div>", Run(new PemsFlexTagHelper(UiTestRenderer.Instance) { Dir = "column", Gap = "1" }, "pems-flex", [], "x"));
        Assert.Equal("<li class=\"d-inline-flex py-1\">x</li>", Run(new PemsFlexTagHelper(UiTestRenderer.Instance) { As = "li", Inline = true, Css = "py-1" }, "pems-flex", [], "x"));
        Assert.Equal("<h6 class=\"d-flex align-items-center mb-0\">x</h6>", Run(new PemsFlexTagHelper(UiTestRenderer.Instance) { As = "h6", Align = "center", Css = "mb-0" }, "pems-flex", [], "x"));
        Assert.Equal("<div class=\"d-flex\">x</div>", Run(new PemsFlexTagHelper(UiTestRenderer.Instance) { As = "script" }, "pems-flex", [], "x"));
    }

    [Fact]
    public void Help_text_scroll_wrapper_and_tab_parts_write_the_bootstrap_classes()
    {
        Assert.Equal("<div class=\"form-text mt-1\">x</div>", Run(new PemsHelpTextTagHelper(UiTestRenderer.Instance) { Css = "mt-1" }, "pems-help-text", [], "x"));
        Assert.Equal("<small class=\"form-text\">x</small>", Run(new PemsHelpTextTagHelper(UiTestRenderer.Instance) { As = "small" }, "pems-help-text", [], "x"));
        Assert.Equal("<p class=\"form-text\">x</p>", Run(new PemsHelpTextTagHelper(UiTestRenderer.Instance) { As = "p" }, "pems-help-text", [], "x"));
        Assert.Equal("<div class=\"table-responsive mb-3\">x</div>", Run(new PemsTableScrollTagHelper(UiTestRenderer.Instance) { Css = "mb-3" }, "pems-table-scroll", [], "x"));
        Assert.Equal("<div class=\"tab-content pt-3\">x</div>", Run(new PemsTabContentTagHelper(UiTestRenderer.Instance) { Css = "pt-3" }, "pems-tab-content", [], "x"));
        Assert.Equal("<div class=\"tab-pane fade show active\" id=\"a\">x</div>", Run(new PemsTabPaneTagHelper(UiTestRenderer.Instance) { Fade = true, Active = true }, "pems-tab-pane", [new("id", "a")], "x"));
        Assert.Equal("<div class=\"tab-pane active\">x</div>", Run(new PemsTabPaneTagHelper(UiTestRenderer.Instance) { Active = true }, "pems-tab-pane", [], "x"));
        Assert.Equal("<div class=\"tab-pane fade\">x</div>", Run(new PemsTabPaneTagHelper(UiTestRenderer.Instance) { Fade = true }, "pems-tab-pane", [], "x"));
        Assert.Equal("<li class=\"nav-item\" role=\"presentation\"><button class=\"nav-link active\" data-bs-toggle=\"tab\" data-bs-target=\"#tab-a\" type=\"button\">x</button></li>", Run(new PemsTabTagHelper(UiTestRenderer.Instance) { Target = "tab-a", Active = true }, "pems-tab", [], "x"));
        Assert.Equal("<h6 class=\"mb-3\" id=\"t\">x</h6>", Run(new PemsHeadingTagHelper(UiTestRenderer.Instance) { Level = 6 }, "pems-heading", [new("class", "mb-3"), new("id", "t")], "x"));
        Assert.Equal("<p class=\"small\">x</p>", Run(new PemsTextTagHelper(UiTestRenderer.Instance), "pems-text", [new("class", "small")], "x"));
        Assert.Equal("<ul class=\"mb-0\"><li>x</li></ul>", Run(new PemsBulletListTagHelper(UiTestRenderer.Instance), "pems-bullet-list", [new("class", "mb-0")], "<li>x</li>"));
        Assert.Equal("<ol></ol>", Run(new PemsBulletListTagHelper(UiTestRenderer.Instance) { Ordered = true }, "pems-bullet-list", [], ""));
        Assert.Equal("<li class=\"a\">x</li>", Run(new PemsBulletTagHelper(UiTestRenderer.Instance), "pems-bullet", [new("class", "a")], "x"));
        Assert.StartsWith("<hr class=\"my-3\"", Run(new PemsDividerTagHelper(UiTestRenderer.Instance), "pems-divider", [new("class", "my-3")]));
        var wf = Run(new PemsStageDiagramTagHelper(UiTestRenderer.Instance) { Steps = [new("A", "bi-a", StageState.Done, "R"), new("B", "bi-b", StageState.Current), new("C", "bi-c", StageState.Pending), new("D", "bi-d", StageState.Rejected)] }, "pems-stage-diagram", []);
        wf = System.Text.RegularExpressions.Regex.Replace(wf, @">\s+<", "><");   // whitespace between block elements does not show
        Assert.StartsWith("<div class=\"pems-wf-diagram\"><div class=\"pems-wf-step done\"><div class=\"pems-wf-circle\"><i class=\"bi bi-check-lg\"></i></div><div class=\"pems-wf-label\">A</div><div class=\"pems-wf-role\">R</div></div><div class=\"pems-wf-connector done\"></div>", wf);
        Assert.Contains("<div class=\"pems-wf-step current\"><div class=\"pems-wf-circle\"><i class=\"bi bi-b\"></i></div><div class=\"pems-wf-label\">B</div></div><div class=\"pems-wf-connector \"></div>", wf);
        Assert.Contains("<div class=\"pems-wf-step \">", wf);
        Assert.Contains("<div class=\"pems-wf-step rejected\"><div class=\"pems-wf-circle\"><i class=\"bi bi-x-lg\"></i>", wf);
        var wo = Run(new PemsStageDiagramTagHelper(UiTestRenderer.Instance) { Variant = "wo", Steps = [new("A", "bi-a", StageState.Done), new("B", "bi-b", StageState.Current, "ignored")] }, "pems-stage-diagram", []);
        wo = System.Text.RegularExpressions.Regex.Replace(wo, @">\s+<", "><");
        Assert.Equal("<div class=\"pems-wo-diagram\"><div class=\"pems-wo-step done\"><div class=\"pems-wo-circle\"><i class=\"bi bi-check-lg\"></i></div><div class=\"pems-wo-label\">A</div></div><div class=\"pems-wo-connector done\"></div><div class=\"pems-wo-step current\"><div class=\"pems-wo-circle\"><i class=\"bi bi-b\"></i></div><div class=\"pems-wo-label\">B</div></div></div>", wo);
        var vs = Run(new PemsStageDiagramTagHelper(UiTestRenderer.Instance) { Variant = "vs", Steps = [new("A", "bi-a", StageState.Done, "R"), new("B", "bi-b", StageState.Current)] }, "pems-stage-diagram", []);
        vs = System.Text.RegularExpressions.Regex.Replace(vs, @">\s+<", "><");
        Assert.Equal("<div class=\"vs-tracker\"><div class=\"vs-step vs-step--done\"><div class=\"vs-step__dot\"><i class=\"bi bi-check-lg\" aria-hidden=\"true\"></i></div><div class=\"vs-step__label\">A</div><div class=\"vs-step__meta\">R</div></div><div class=\"vs-step vs-step--current\" aria-current=\"step\"><div class=\"vs-step__dot\"><i class=\"bi bi-b\" aria-hidden=\"true\"></i></div><div class=\"vs-step__label\">B</div><div class=\"vs-step__meta\"></div></div></div>", vs);
        var side = System.Text.RegularExpressions.Regex.Replace(Run(new PemsSideLinkTagHelper(null!, UiTestRenderer.Instance) { Active = true, Icon = "bi-inbox", Count = 3, Hot = "ct-count--alert" }, "pems-side-link", [new("href", "/x")], "Inbox"), @">\s+<", "><");
        Assert.Equal("<a class=\"ct-folder active\" href=\"/x\" aria-current=\"page\"><i class=\"bi bi-inbox\" aria-hidden=\"true\"></i><span class=\"ct-label\">Inbox</span><span class=\"ct-count ct-count--alert\">3</span></a>", side);
        Assert.Equal("<a class=\"ct-folder\" href=\"/y\"><span class=\"ct-label\">Out</span></a>", System.Text.RegularExpressions.Regex.Replace(Run(new PemsSideLinkTagHelper(null!, UiTestRenderer.Instance) { Count = 0 }, "pems-side-link", [new("href", "/y")], "Out"), @">\s+<", "><"));
        Assert.Contains("pems-sidenav__item pems-sidenav__item--active", Run(new PemsSideLinkTagHelper(null!, UiTestRenderer.Instance) { Look = "pems", Active = true, Count = 2 }, "pems-side-link", [new("href", "/z")], "L"));
        Assert.Equal("<h2>x</h2>", Run(new PemsHeadingTagHelper(UiTestRenderer.Instance), "pems-heading", [], "x"));
        Assert.Equal("<a class=\"text-muted\" href=\"/x\" target=\"_blank\">x</a>", Run(new PemsLinkTagHelper(null!, UiTestRenderer.Instance), "pems-link", [new("class", "text-muted"), new("href", "/x"), new("target", "_blank")], "x"));
        Assert.Equal("<li class=\"nav-item\"><a class=\"nav-link active\" href=\"/x\">x</a></li>", Run(new PemsNavLinkTagHelper(null!, UiTestRenderer.Instance) { Active = true }, "pems-nav-link", [new("href", "/x")], "x"));
        Assert.Equal("<a class=\"nav-link nav-subitem\" href=\"/x\" target=\"_blank\" rel=\"noopener\">x</a>", Run(new PemsNavLinkTagHelper(null!, UiTestRenderer.Instance) { Item = false, Sub = true, NewTab = true }, "pems-nav-link", [new("href", "/x")], "x"));
        Assert.Equal("<li class=\"nav-item\" role=\"presentation\"><button class=\"nav-link px-3\" id=\"t1\" data-bs-toggle=\"tab\" data-bs-target=\"#tab-b\" type=\"button\">x</button></li>", Run(new PemsTabTagHelper(UiTestRenderer.Instance) { Target = "#tab-b", Css = "px-3" }, "pems-tab", [new("id", "t1")], "x"));
    }

    [Fact]
    public void Modal_parts_write_the_bootstrap_modal_markup()
    {
        Assert.Equal("<div class=\"modal-header py-2\"><h6 class=\"modal-title\">T</h6><button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"Close\"></button></div>",
            Run(new PemsModalHeaderTagHelper(UiTestRenderer.Instance) { Close = true, CloseLabel = "Close", Css = "py-2" }, "pems-modal-header", [], "T"));
        Assert.Equal("<div class=\"modal-header\"><h5 class=\"modal-title fw-bold\" id=\"t\">T</h5></div>",
            Run(new PemsModalHeaderTagHelper(UiTestRenderer.Instance) { Level = 5, TitleCss = "fw-bold", TitleId = "t" }, "pems-modal-header", [], "T"));
        Assert.Equal("<div class=\"modal-body pb-0\">x</div>", Run(new PemsModalBodyTagHelper(UiTestRenderer.Instance) { Css = "pb-0" }, "pems-modal-body", [], "x"));
        Assert.Equal("<div class=\"modal-footer justify-content-between\">x</div>", Run(new PemsModalFooterTagHelper(UiTestRenderer.Instance) { Css = "justify-content-between" }, "pems-modal-footer", [], "x"));
    }

    [Fact]
    public void Dashboard_blocks_write_the_admin_kpi_metric_and_hero_markup()
    {
        Assert.Equal("<div class=\"admin-kpi mb-2\"><div class=\"admin-kpi-label\">Modules</div><div class=\"admin-kpi-value\">7</div></div>",
            Run(new PemsStatTagHelper(UiTestRenderer.Instance) { Label = "Modules", Css = "mb-2" }, "pems-stat", [], "7"));
        Assert.Equal("<div class=\"admin-kpi-grid\">x</div>", Run(new PemsStatGridTagHelper(UiTestRenderer.Instance), "pems-stat-grid", [], "x"));
        Assert.Equal("<div class=\"fs-4 fw-bold text-success\">42</div>", Run(new PemsMetricTagHelper(UiTestRenderer.Instance) { Css = "text-success" }, "pems-metric", [], "42"));
        Assert.Equal("<div class=\"admin-hero mb-3\">x</div>", Run(new PemsPageHeroTagHelper(UiTestRenderer.Instance) { Css = "mb-3" }, "pems-page-hero", [], "x"));
    }

    [Fact]
    public async Task A_form_frame_keeps_the_page_attributes_and_content_and_has_no_empty_class()
    {
        var withClass = await UiTestRenderer.Instance.RenderAsync<PemsFormFrame>(new Dictionary<string, object?>
        {
            [nameof(PemsFormFrame.Options)] = new FormFrameOptions { PageClass = "row g-2", Attributes = new Dictionary<string, object> { ["action"] = "/x/save", ["method"] = "post", ["id"] = "f1" } },
            [nameof(PemsFormFrame.ChildContent)] = (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddMarkupContent(0, "<input name=\"a\" />"))
        });
        Assert.Contains("<form", withClass);
        Assert.Contains("class=\"row g-2\"", withClass);
        Assert.Contains("action=\"/x/save\"", withClass);
        Assert.Contains("id=\"f1\"", withClass);
        Assert.Contains("<input name=\"a\" /></form>", withClass);
        var plain = await UiTestRenderer.Instance.RenderAsync<PemsFormFrame>(new Dictionary<string, object?> { [nameof(PemsFormFrame.Options)] = new FormFrameOptions() });
        Assert.DoesNotContain("class=", plain);
    }

    [Fact]
    public async Task A_plain_button_writes_only_the_page_classes_and_attributes()
    {
        var tab = await UiTestRenderer.Instance.RenderAsync<PemsButtonFrame>(new Dictionary<string, object?>
        {
            [nameof(PemsButtonFrame.Options)] = new ButtonFrameOptions { Plain = true, Css = "nav-link active", Attributes = new Dictionary<string, object> { ["type"] = "button", ["data-bs-toggle"] = "tab" } },
            [nameof(PemsButtonFrame.ChildContent)] = (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddContent(0, "Info"))
        });
        Assert.Contains("<button class=\"nav-link active\" type=\"button\" data-bs-toggle=\"tab\">Info</button>", tab);
        Assert.DoesNotContain("btn", tab);
        var bare = await UiTestRenderer.Instance.RenderAsync<PemsButtonFrame>(new Dictionary<string, object?> { [nameof(PemsButtonFrame.Options)] = new ButtonFrameOptions { Plain = true, Disabled = true } });
        Assert.DoesNotContain("class=", bare);
        Assert.Contains("disabled", bare);
    }
}
