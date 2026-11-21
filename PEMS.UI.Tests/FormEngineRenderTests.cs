using System.Runtime.CompilerServices;
using PEMS.UI.Abstractions;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-578: the form component keeps today's markup for a plain form and adds the grid, the hidden cells, the errors of the form and the rules for the browser only when a definition asks for them.</summary>
public sealed class FormEngineRenderTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));

    private static TranslatableText T(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    private static FormDefinition Form(int columns = 1, params FieldDescriptor[] fields) => new()
    {
        Id = "f", Title = T("Form", "فرم"), SubmitLabel = T("Save", "ذخیره"), Fields = fields, Columns = columns, Action = "/post"
    };

    private static FieldDescriptor F(string name) => new() { Name = name, Label = T(name, name) };

    private static string Render(FormDefinition def, FormState? state = null, string? rules = null)
        => UiTestRenderer.Instance.RenderAsync("form", new Dictionary<string, object?> { ["Definition"] = def, ["State"] = state ?? FormState.Empty, ["RequestToken"] = "tok", ["RulesJson"] = rules }, new System.Globalization.CultureInfo("en")).GetAwaiter().GetResult();

    [Fact]
    public void A_plain_form_has_no_grid_no_cells_and_no_rules()
    {
        var html = Render(Form(1, F("a"), F("b")));
        Assert.DoesNotContain("pems-form__grid", html);
        Assert.DoesNotContain("pems-form__cell", html);
        Assert.DoesNotContain("data-pems-rules", html);
        Assert.DoesNotContain("pems-form__errors", html);
    }

    [Fact]
    public void Two_columns_write_the_grid_and_a_wide_field_spans_both()
    {
        var html = Render(Form(2, F("a"), F("b") with { Span = 2 }));
        Assert.Contains("class=\"pems-form__grid\"", html);
        Assert.Contains("pems-form__cell pems-form__cell--span\" data-field=\"b\"", html);
        Assert.Contains("class=\"pems-form__cell\" data-field=\"a\"", html);
    }

    [Fact]
    public void A_field_with_a_visible_condition_is_a_cell_and_is_hidden_while_the_condition_is_false()
    {
        var def = Form(1, F("a"), F("b") with { Visible = "values.a == 'x'" });
        var hidden = Render(def, new FormState { Hidden = ["b"] });
        Assert.Matches("data-field=\"b\"[^>]*hidden", hidden);
        Assert.DoesNotMatch("data-field=\"b\"[^>]*hidden", Render(def));
    }

    [Fact]
    public void The_errors_of_the_form_and_the_rules_for_the_browser_are_written_when_given()
    {
        var html = Render(Form(1, F("a")), new FormState { FormErrors = [T("The dates do not match.", "تاریخ‌ها نمی‌خوانند.")] }, "{\"fields\":[]}");
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("The dates do not match.", html);
        Assert.Contains("data-pems-rules=", html);
    }

    private static string RenderButton(string culture)
        => UiTestRenderer.Instance.RenderAsync("action-button", new Dictionary<string, object?> { ["Options"] = System.Text.Json.JsonSerializer.Deserialize<PEMS.UI.Components.ActionButtonOptions>(PEMS.UI.Manifest.ComponentSamples.Get("action-button")!.ToJsonString(), PemsJson.Options) }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    [Fact]
    public void The_action_button_posts_the_values_to_the_action_host_with_the_token_and_asks_first()
    {
        var html = System.Net.WebUtility.HtmlDecode(RenderButton("en"));
        Assert.Contains("action=\"/a/department-create\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\" value=\"t\"", html);
        Assert.Contains("name=\"id\" value=\"12\"", html);
        Assert.Contains("name=\"returnUrl\" value=\"/Department\"", html);
        Assert.Contains("data-pems-confirm=\"Create it?\"", html);
        Assert.Contains("btn btn-success", html);
        Assert.Contains("Create</button>", html);
        Assert.Contains("ایجاد", System.Net.WebUtility.HtmlDecode(RenderButton("fa")));
        Assert.True(new PEMS.UI.Manifest.ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry()).Validate("action-button", PEMS.UI.Manifest.ComponentSamples.Get("action-button")!.ToJsonString()).IsValid);
    }

    [Fact]
    public void The_rules_script_is_loaded_after_the_expression_engine_and_changes_no_markup_by_itself()
    {
        var js = File.ReadAllText(Path.Combine(Root(), "PEMS.UI", "wwwroot", "pems-form-rules.js"));
        Assert.DoesNotContain("createElement", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotMatch("https?://", js);
        var layout = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.True(layout.IndexOf("pems-expr.js", StringComparison.Ordinal) < layout.IndexOf("pems-form-rules.js", StringComparison.Ordinal));
        var css = File.ReadAllText(Path.Combine(Root(), "PEMS.UI", "wwwroot", "pems-views.css"));
        Assert.DoesNotMatch(@"(margin|padding|border)-(left|right)\s*:|(?<![\w-])(left|right)\s*:", css);
    }
}
