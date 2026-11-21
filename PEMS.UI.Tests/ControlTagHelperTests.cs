using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-487 / #487: the bare form controls write the Bootstrap markup the pages wrote by hand and keep every attribute.</summary>
public sealed class ControlTagHelperTests
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

    [Theory]
    [InlineData(ControlKind.Text, Size.Medium, null, "form-control")]
    [InlineData(ControlKind.Text, Size.Small, null, "form-control form-control-sm")]
    [InlineData(ControlKind.Text, Size.Large, "mb-2", "form-control form-control-lg mb-2")]
    [InlineData(ControlKind.Select, Size.Medium, null, "form-select")]
    [InlineData(ControlKind.Select, Size.Small, "w-auto", "form-select form-select-sm w-auto")]
    [InlineData(ControlKind.Check, Size.Small, "me-1", "form-check-input me-1")]
    public void Control_classes_are_the_ones_the_pages_wrote(ControlKind kind, Size size, string? css, string expected)
        => Assert.Equal(expected, ControlState.Css(new ControlOptions { Kind = kind, Size = size, CssClass = css }));

    [Fact]
    public void An_input_keeps_its_attributes_and_gets_the_classes_first()
        => Assert.Equal("<input class=\"form-control form-control-sm\" required=\"required\" placeholder=\"Tag\" name=\"tag\" type=\"number\" value=\"3\" />",
            Run(new PemsInputTagHelper(null!, UiTestRenderer.Instance) { Scale = "small", Name = "tag", InputTypeName = "number", Value = "3" }, "pems-input",
                [new("required", "required"), new("placeholder", "Tag")]));

    [Fact]
    public void An_input_keeps_its_form_attribute_the_built_in_helper_binds()
        => Assert.Equal("<input class=\"form-control\" name=\"description\" form=\"rule_2\" />",
            Run(new PemsInputTagHelper(null!, UiTestRenderer.Instance) { Name = "description", FormName = "rule_2" }, "pems-input", []));

    [Fact]
    public void A_check_input_gets_the_check_class()
        => Assert.Equal("<input class=\"form-check-input me-1\" name=\"on\" type=\"checkbox\" />",
            Run(new PemsInputTagHelper(null!, UiTestRenderer.Instance) { Kind = "check", Css = "me-1", Name = "on", InputTypeName = "checkbox" }, "pems-input", []));

    [Fact]
    public void A_select_keeps_its_options_and_attributes()
        => Assert.Equal("<select class=\"form-select form-select-sm\" multiple=\"multiple\" name=\"s\"><option>a</option></select>",
            Run(new PemsSelectTagHelper(null!, UiTestRenderer.Instance) { Scale = "small", Name = "s" }, "pems-select", [new("multiple", "multiple")], "<option>a</option>"));

    [Fact]
    public void A_textarea_keeps_its_text_and_attributes()
        => Assert.Equal("<textarea class=\"form-control\" rows=\"3\" name=\"n\">hello</textarea>",
            Run(new PemsTextAreaTagHelper(null!, UiTestRenderer.Instance) { Name = "n" }, "pems-textarea", [new("rows", "3")], "hello"));

    [Fact]
    public void A_class_the_built_in_helper_added_stays_after_the_control_classes()
        => Assert.Equal("<input class=\"form-control input-validation-error\" />",
            Run(new PemsInputTagHelper(null!, UiTestRenderer.Instance), "pems-input", [new("class", "input-validation-error")]));

    private static async Task<string> RunAsync(TagHelper helper, string tag, TagHelperAttributeList attributes, string inner = "")
    {
        var context = new TagHelperContext(tag, new TagHelperAttributeList(), new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput(tag, attributes, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(inner)));
        output.Content.SetHtmlContent(inner);
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Theory]
    [InlineData(null, "form-label")]
    [InlineData("small", "form-label small")]
    [InlineData("small fw-bold", "form-label small fw-bold")]
    public async Task A_label_gets_the_form_label_class_first(string? css, string expected)
        => Assert.Equal($"<label class=\"{expected}\" for=\"qty\">Quantity</label>",
            await RunAsync(new PemsLabelTagHelper(null!, UiTestRenderer.Instance) { Css = css }, "pems-label", [new("for", "qty")], "Quantity"));

    [Theory]
    [InlineData(true, null, "dropdown-menu dropdown-menu-end")]
    [InlineData(false, "w-100", "dropdown-menu w-100")]
    [InlineData(true, "p-0", "dropdown-menu dropdown-menu-end p-0")]
    public void Dropdown_menu_classes(bool end, string? css, string expected) => Assert.Equal(expected, FrameState.DropdownMenu(end, css));

    [Theory]
    [InlineData("tabs", "mb-3", "nav nav-tabs mb-3")]
    [InlineData(null, null, "nav nav-tabs")]
    [InlineData("pills", null, "nav nav-pills")]
    public void Nav_classes(string? kind, string? css, string expected) => Assert.Equal(expected, FrameState.Nav(kind, css));

    [Fact]
    public void A_dropdown_menu_keeps_its_attributes_and_items()
        => Assert.Equal("<ul class=\"dropdown-menu dropdown-menu-end p-0\" id=\"m\" aria-labelledby=\"b\"><li>x</li></ul>",
            Run(new PemsDropdownMenuTagHelper(UiTestRenderer.Instance) { End = true, Css = "p-0" }, "pems-dropdown-menu", [new("id", "m"), new("aria-labelledby", "b")], "<li>x</li>"));

    [Fact]
    public void A_dropdown_panel_can_be_a_div()
        => Assert.StartsWith("<div class=\"dropdown-menu\"", Run(new PemsDropdownMenuTagHelper(UiTestRenderer.Instance) { As = "div" }, "pems-dropdown-menu", [], "x"));

    [Fact]
    public void A_tab_strip_keeps_role_and_id()
        => Assert.Equal("<ul class=\"nav nav-tabs mb-3\" id=\"docTabs\" role=\"tablist\"><li>t</li></ul>",
            Run(new PemsNavTagHelper(UiTestRenderer.Instance) { Css = "mb-3" }, "pems-nav", [new("id", "docTabs"), new("role", "tablist")], "<li>t</li>"));

    [Fact]
    public void A_breadcrumb_is_an_ol()
        => Assert.Equal("<ol class=\"breadcrumb mb-0 small\"><li>a</li></ol>", Run(new PemsBreadcrumbTagHelper(UiTestRenderer.Instance) { Css = "mb-0 small" }, "pems-crumb-trail", [], "<li>a</li>"));

    [Theory]
    [InlineData(false, false, null, "table")]
    [InlineData(true, false, null, "table table-enterprise")]
    [InlineData(true, true, "mb-0", "table table-enterprise table-sm mb-0")]
    [InlineData(false, true, "vs-dense mb-0", "table table-sm vs-dense mb-0")]
    public void Table_classes(bool enterprise, bool small, string? css, string expected) => Assert.Equal(expected, FrameState.Table(enterprise, small, css));

    [Fact]
    public void A_table_keeps_its_attributes_and_rows()
        => Assert.Equal("<table class=\"table table-enterprise table-sm mb-0\" id=\"permTable\"><tbody><tr><td>x</td></tr></tbody></table>",
            Run(new PemsTableFrameTagHelper(UiTestRenderer.Instance) { Kind = "enterprise", Scale = "small", Css = "mb-0" }, "pems-table", [new("id", "permTable")], "<tbody><tr><td>x</td></tr></tbody>"));

    [Theory]
    [InlineData(true, false, null, "list-group-item list-group-item-action")]
    [InlineData(true, true, "d-flex", "list-group-item list-group-item-action active d-flex")]
    [InlineData(false, false, null, "list-group-item")]
    public void List_group_item_classes(bool action, bool active, string? css, string expected) => Assert.Equal(expected, FrameState.ListGroupItem(action, active, css));

    [Theory]
    [InlineData("success", null, "toast align-items-center text-bg-success border-0 show")]
    [InlineData("DANGER", "mb-1", "toast align-items-center text-bg-danger border-0 show mb-1")]
    public void Toast_classes(string tone, string? css, string expected) => Assert.Equal(expected, FrameState.Toast(tone, css));

    [Fact]
    public void A_toast_keeps_its_attributes_and_body()
        => Assert.Equal("<div class=\"toast align-items-center text-bg-success border-0 show\" role=\"alert\" data-bs-delay=\"5000\"><div class=\"toast-body\">x</div></div>",
            Run(new PemsToastTagHelper(UiTestRenderer.Instance) { Tone = "success" }, "pems-toast", [new("role", "alert"), new("data-bs-delay", "5000")], "<div class=\"toast-body\">x</div>"));

    [Fact]
    public void A_list_group_item_is_a_link_by_default_and_keeps_its_attributes()
        => Assert.Equal("<a class=\"list-group-item list-group-item-action active\" href=\"/x\">t</a>",
            Run(new PemsListGroupItemTagHelper(null!, UiTestRenderer.Instance) { IsAction = true, Active = true }, "pems-list-group-item", [new("href", "/x")], "t"));

    [Fact]
    public void A_list_group_item_can_be_a_div()
        => Assert.StartsWith("<div class=\"list-group-item\"", Run(new PemsListGroupItemTagHelper(null!, UiTestRenderer.Instance) { As = "div" }, "pems-list-group-item", [], "t"));
}
