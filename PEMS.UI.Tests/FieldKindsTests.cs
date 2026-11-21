using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-352 / #322: the remaining field kinds. Each posts a plain browser value; no kind renders a literal text.</summary>
public sealed class FieldKindsTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private IPemsComponentRenderer _renderer = default!;

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        _renderer = _provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static TranslatableText T(string fa, string en) => TranslatableText.Of(("fa", fa), ("en", en));
    private static void Has(string expected, string actual) => Assert.Contains(expected, actual, StringComparison.Ordinal);
    private static void HasNot(string unexpected, string actual) => Assert.DoesNotContain(unexpected, actual, StringComparison.Ordinal);

    private async Task<string> Render(FieldDescriptor d, string? value, string culture, IReadOnlyList<string>? values = null)
    {
        var p = new Dictionary<string, object?> { ["Descriptor"] = d, ["Value"] = value, ["Values"] = values };
        return WebUtility.HtmlDecode(await _renderer.RenderAsync("field", p, new CultureInfo(culture)));
    }

    private static FieldDescriptor F(FieldKind kind) => new() { Name = "Item.Field", Label = T("برچسب", "Label"), Kind = kind };

    [Fact]
    public async Task Password_never_puts_the_value_back_into_the_page()
    {
        var html = await Render(F(FieldKind.Password), "s3cret", "en");
        Has("type=\"password\"", html); Has("autocomplete=\"new-password\"", html); HasNot("s3cret", html); HasNot("value=", html);
    }

    [Fact]
    public async Task Money_is_a_left_to_right_decimal_keypad_text_and_keeps_its_value()
    {
        var html = await Render(F(FieldKind.Money), "12.50", "fa");
        Has("inputmode=\"decimal\"", html); Has("dir=\"ltr\"", html); Has("type=\"text\"", html); Has("value=\"12.50\"", html); Has(">برچسب", html);
    }

    [Fact]
    public async Task Time_and_tags_are_plain_inputs()
    {
        Has("type=\"time\"", await Render(F(FieldKind.Time), "08:30", "en"));
        var tags = await Render(F(FieldKind.Tags), "a,b", "en");
        Has("data-pems-tags=\",\"", tags); Has("value=\"a,b\"", tags);
        HasNot("data-pems-tags", await Render(F(FieldKind.Text), "x", "en"));
    }

    [Fact]
    public async Task Switch_is_a_checkbox_with_the_switch_role_and_keeps_checkbox_output_otherwise()
    {
        var on = await Render(F(FieldKind.Switch), "true", "en");
        Has("role=\"switch\"", on); Has("type=\"checkbox\"", on); Has("checked", on);
        HasNot("role=", await Render(F(FieldKind.Checkbox), "true", "en"));
    }

    [Fact]
    public async Task Radio_group_is_a_fieldset_with_a_legend_and_the_selected_option_checked()
    {
        var d = F(FieldKind.Radio) with { Required = true, Options = [new FieldOption { Value = "a", Label = T("الف", "Alpha") }, new FieldOption { Value = "b", Label = T("ب", "Beta") }] };
        var en = await Render(d, "b", "en");
        Has("<fieldset", en); Has("<legend", en); Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(en, "type=\"radio\"").Count);
        Assert.Matches(new System.Text.RegularExpressions.Regex("value=\"b\"[^>]*checked"), en); Has("required", en); Has("Alpha", en);
        Has("الف", await Render(d, "b", "fa"));
    }

    [Fact]
    public async Task Checkbox_matrix_posts_row_and_column_pairs_as_one_list_and_marks_the_chosen_ones()
    {
        var d = F(FieldKind.CheckboxMatrix) with
        {
            Options = [new FieldOption { Value = "draft", Label = T("پیش‌نویس", "Draft") }, new FieldOption { Value = "review", Label = T("بررسی", "Review") }],
            Columns = [new FieldOption { Value = "review", Label = T("بررسی", "Review") }, new FieldOption { Value = "done", Label = T("پایان", "Done") }]
        };
        var html = await Render(d, null, "en", ["draft|review"]);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "type=\"checkbox\" name=\"Item.Field\"").Count);
        Has("value=\"draft|review\" checked", html); HasNot("value=\"draft|done\" checked", html);
        Has("scope=\"col\"", html); Has("scope=\"row\"", html); Has("aria-label=\"Draft / Review\"", html);
    }

    [Fact]
    public void The_descriptor_with_the_new_kinds_survives_a_json_round_trip()
    {
        var d = F(FieldKind.CheckboxMatrix) with { Options = [new FieldOption { Value = "r", Label = T("ر", "R") }], Columns = [new FieldOption { Value = "c", Label = T("س", "C") }] };
        var json = System.Text.Json.JsonSerializer.Serialize(d, PemsJson.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<FieldDescriptor>(json, PemsJson.Options)!;
        Assert.Equal(FieldKind.CheckboxMatrix, back.Kind);
        Assert.Equal("c", back.Columns![0].Value);
        Assert.Contains("\"checkboxMatrix\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_kind_renders_in_both_languages_without_a_literal_text()
    {
        foreach (var kind in Enum.GetValues<FieldKind>())
            foreach (var culture in new[] { "en", "fa" })
                Assert.False(string.IsNullOrWhiteSpace(await Render(F(kind) with { Options = [], Columns = [] }, null, culture)), kind + " " + culture);
    }

    [Fact]
    public async Task Form_section_renders_its_fields_with_values_and_errors_from_the_state()
    {
        var options = new FormSectionOptions { Title = T("عمومی", "General"), Description = T("توضیح", "Notes"), Fields = [F(FieldKind.Text) with { Name = "Tag", Label = T("تگ", "Tag") }] };
        var state = new FormState { Values = new Dictionary<string, string?> { ["Tag"] = "PT-1" }, Errors = new Dictionary<string, TranslatableText> { ["Tag"] = T("اجباری", "Required") } };
        var html = WebUtility.HtmlDecode(await _renderer.RenderAsync("form-section", new Dictionary<string, object?> { ["Options"] = options, ["State"] = state }, new CultureInfo("en")));
        Has("<fieldset class=\"pems-form-section\"", html); Has(">General</legend>", html); Has("value=\"PT-1\"", html); Has(">Required<", html); Has(">Notes<", html);
    }

    [Fact]
    public async Task Action_bar_renders_its_buttons_and_the_sticky_variant()
    {
        var options = new ActionBarOptions { Actions = [new ButtonOptions { Label = T("ذخیره", "Save"), Type = "submit" }, new ButtonOptions { Label = T("انصراف", "Cancel"), Tone = Tone.Neutral, Href = "/x" }], Sticky = true };
        var html = WebUtility.HtmlDecode(await _renderer.RenderAsync("action-bar", new Dictionary<string, object?> { ["Options"] = options }, new CultureInfo("en")));
        Has("role=\"group\"", html); Has("pems-actionbar--sticky", html); Has(">Save</button>", html); Has("href=\"/x\"", html);
        HasNot("pems-actionbar--sticky", WebUtility.HtmlDecode(await _renderer.RenderAsync("action-bar", new Dictionary<string, object?> { ["Options"] = options with { Sticky = false } }, new CultureInfo("en"))));
    }

    [Fact]
    public async Task Form_asks_for_the_leave_guard_only_when_the_definition_says_so()
    {
        var def = new FormDefinition { Id = "f", Title = T("فرم", "Form"), Action = "/x", Fields = [], SubmitLabel = T("ثبت", "Save") };
        string Render(FormDefinition d) => WebUtility.HtmlDecode(_renderer.RenderAsync("form", new Dictionary<string, object?> { ["Definition"] = d }, new CultureInfo("en")).GetAwaiter().GetResult());
        HasNot("data-pems-dirty-guard", Render(def));
        Has("data-pems-dirty-guard=\"\"", Render(def with { WarnOnLeave = true }));
        await Task.CompletedTask;
    }
}
