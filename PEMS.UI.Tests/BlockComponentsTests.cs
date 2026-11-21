using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Components;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-53 / #31: card, progress, accordion, list-group and spinner write the Bootstrap markup the pages wrote by hand.</summary>
public sealed class BlockComponentsTests
{
    private static async Task<string> Run(Func<IPemsComponentRenderer, TagHelper> make, string tag, string inner, TagHelperContext? context = null, TagHelperAttributeList? attributes = null)
    {
        await using var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var helper = make(provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>());
        context ??= new TagHelperContext(tag, new TagHelperAttributeList(), new Dictionary<object, object>(), "t");
        var output = new TagHelperOutput(tag, attributes ?? new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(inner)));
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task Card_writes_the_card_box_with_the_page_classes()
        => Assert.Equal("<div class=\"card shadow-sm border-0 h-100\"><div class=\"card-body\">x</div></div>",
            await Run(r => new PemsCardTagHelper(r) { Css = "shadow-sm border-0 h-100" }, "pems-card", "<div class=\"card-body\">x</div>"));

    [Fact]
    public async Task Progress_writes_track_bar_width_and_progressbar_attributes()
        => Assert.Equal("<div class=\"progress mb-3\" style=\"height:8px;\" role=\"progressbar\" aria-valuenow=\"3\" aria-valuemin=\"0\" aria-valuemax=\"4\"><div class=\"progress-bar bg-success\" style=\"width:75%\"></div></div>",
            await Run(r => new PemsProgressTagHelper(r) { Value = 3, Max = 4, Tone = "success", Height = "8px", Css = "mb-3" }, "pems-progress", ""));

    [Fact]
    public async Task Progress_keeps_the_text_inside_the_bar_and_the_extra_style()
        => Assert.Equal("<div class=\"progress flex-grow-1\" style=\"height:22px; min-width: 70px;\" role=\"progressbar\" aria-valuenow=\"40\" aria-valuemin=\"0\" aria-valuemax=\"100\" aria-label=\"Setup\"><div class=\"progress-bar\" style=\"width:40%\">40%</div></div>",
            await Run(r => new PemsProgressTagHelper(r) { Value = 40, Height = "22px", Style = "min-width: 70px;", Css = "flex-grow-1", Label = "Setup" }, "pems-progress", " 40% "));

    [Theory]
    [InlineData(-5, "0")]
    [InlineData(250, "100")]
    public async Task Progress_clamps_the_width(double value, string width)
        => Assert.Contains($"width:{width}%", await Run(r => new PemsProgressTagHelper(r) { Value = value }, "pems-progress", ""));

    [Fact]
    public async Task Accordion_and_item_write_the_collapse_markup()
    {
        var accordion = await Run(r => new PemsAccordionTagHelper(r) { Id = "letterAccordion", Css = "mb-3" }, "pems-accordion", "ITEMS");
        Assert.Equal("<div class=\"accordion mb-3\" id=\"letterAccordion\">ITEMS</div>", accordion);

        var context = new TagHelperContext("pems-accordion-item", new TagHelperAttributeList(), new Dictionary<object, object>(), "i");
        var item = await Run(r => new PemsAccordionItemTagHelper(r) { Target = "accRecipients", Parent = "letterAccordion", ButtonCss = "small", BodyCss = "p-2" },
            "pems-accordion-item", "<p>body</p>", context);
        // The header tag helper runs inside the item; here its slot is empty, the body and the ids are what matter.
        Assert.Contains("data-bs-target=\"#accRecipients\"", item);
        Assert.Contains("class=\"accordion-button collapsed small\"", item);
        Assert.Contains("id=\"accRecipients\" class=\"accordion-collapse collapse\" data-bs-parent=\"#letterAccordion\"", item);
        Assert.Contains("<div class=\"accordion-body p-2\"><p>body</p></div>", item);
    }

    [Fact]
    public async Task Accordion_header_fills_the_button_of_its_item()
    {
        var slot = new PemsAccordionItemTagHelper.HeaderSlot();
        var items = new Dictionary<object, object> { [typeof(PemsAccordionItemTagHelper.HeaderSlot)] = slot };
        var header = new PemsAccordionHeaderTagHelper();
        var context = new TagHelperContext("pems-accordion-header", new TagHelperAttributeList(), items, "h");
        var output = new TagHelperOutput("pems-accordion-header", new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(" <i class=\"bi bi-people\"></i> Recipients (2) ")));
        await header.ProcessAsync(context, output);
        Assert.Equal("<i class=\"bi bi-people\"></i> Recipients (2)", slot.Html);
        Assert.True(output.TagName is null && output.Content.IsEmptyOrWhiteSpace);
    }

    [Theory]
    [InlineData(true, null, "list-group list-group-flush")]
    [InlineData(false, null, "list-group")]
    [InlineData(false, "mb-2", "list-group mb-2")]
    public async Task List_group_writes_the_box_classes(bool flush, string? css, string expected)
        => Assert.Equal($"<div class=\"{expected}\">x</div>", await Run(r => new PemsListGroupTagHelper(r) { Flush = flush, Css = css }, "pems-list-group", "x"));

    [Theory]
    [InlineData("small", "neutral", false, "spinner-border spinner-border-sm text-secondary")]
    [InlineData("medium", null, false, "spinner-border")]
    [InlineData("small", "primary", true, "spinner-grow spinner-grow-sm text-primary")]
    public async Task Spinner_writes_the_spinner_classes(string size, string? tone, bool grow, string expected)
        => Assert.Equal($"<div class=\"{expected}\" role=\"status\" aria-hidden=\"true\"></div>",
            await Run(r => new PemsSpinnerTagHelper(r) { Size = size, Tone = tone, Grow = grow }, "pems-spinner", ""));

    [Fact]
    public async Task Modal_frame_wraps_the_page_markup_and_keeps_its_attributes()
        => Assert.Equal("<div id=\"m1\" class=\"modal fade\" tabindex=\"-1\" aria-hidden=\"true\"><div class=\"modal-dialog modal-lg modal-dialog-scrollable\"><div class=\"modal-content\"><form>x</form></div></div></div>",
            await Run(r => new PemsModalTagHelper(r) { Size = "large", DialogCss = "modal-dialog-scrollable" }, "pems-modal", "<form>x</form>",
                attributes: [new("id", "m1"), new("tabindex", "-1"), new("aria-hidden", "true")]));

    [Fact]
    public async Task Modal_frame_defaults_to_the_plain_dialog_and_takes_extra_classes()
        => Assert.Equal("<div id=\"m2\" class=\"modal fade ct-settings\"><div class=\"modal-dialog\"><div class=\"modal-content\">x</div></div></div>",
            await Run(r => new PemsModalTagHelper(r) { Css = "ct-settings" }, "pems-modal", "x", attributes: [new("id", "m2")]));

    [Theory]
    [InlineData("small", "modal-dialog modal-sm")]
    [InlineData("extralarge", "modal-dialog modal-xl")]
    public async Task Modal_frame_sizes(string size, string dialog)
        => Assert.Contains($"<div class=\"{dialog}\">", await Run(r => new PemsModalTagHelper(r) { Size = size }, "pems-modal", "x", attributes: [new("id", "m")]));

    [Fact]
    public async Task Offcanvas_frame_writes_the_panel_with_the_page_markup()
        => Assert.Equal("<div id=\"lxMenu\" class=\"offcanvas offcanvas-start\" tabindex=\"-1\" aria-labelledby=\"lxMenuTitle\"><div class=\"offcanvas-header\">h</div></div>",
            await Run(r => new PemsOffcanvasTagHelper(r) { Side = "start" }, "pems-offcanvas", "<div class=\"offcanvas-header\">h</div>",
                attributes: [new("id", "lxMenu"), new("tabindex", "-1"), new("aria-labelledby", "lxMenuTitle")]));

    [Fact]
    public async Task Spinner_keeps_its_id_label_and_extra_classes()
        => Assert.Equal("<div class=\"spinner-border spinner-border-sm text-secondary d-none\" role=\"status\" aria-hidden=\"true\" id=\"permBusy\" aria-label=\"Saving\"></div>",
            await Run(r => new PemsSpinnerTagHelper(r) { Size = "small", Tone = "neutral", Css = "d-none" }, "pems-spinner", "", attributes: [new("id", "permBusy"), new("aria-label", "Saving")]));

    [Fact]
    public async Task A_stacked_progress_writes_the_extra_bars_after_the_main_one()
    {
        var slot = new PemsProgressTagHelper.SegmentSlot();
        var context = new TagHelperContext("pems-progress", new TagHelperAttributeList(), new Dictionary<object, object> { [typeof(PemsProgressTagHelper.SegmentSlot)] = slot }, "p");
        var segment = new PemsProgressSegmentTagHelper { Value = 20, Tone = "neutral" };
        segment.Process(context, new TagHelperOutput("pems-progress-segment", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())));
        Assert.Single(slot.Items);
        Assert.Equal(20, slot.Items[0].Value);

        await using var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var html = await renderer.RenderAsync<PemsProgress>(new Dictionary<string, object?>
        {
            ["Options"] = new ProgressOptions { Value = 50, Tone = Tone.Success, Height = "8px", Segments = [new ProgressSegment(20, Tone.Neutral), new ProgressSegment(30, Tone.Danger)] }
        });
        Assert.Contains("progress-bar bg-success\" style=\"width:50%", html);
        Assert.Contains("progress-bar bg-secondary\" style=\"width:20%", html);
        Assert.Contains("progress-bar bg-danger\" style=\"width:30%", html);
    }
}
