using System.Text.Json;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-578: the form engine decides visible, required and read only by conditions, validates the post from the definition with messages in both languages, and refuses a bad definition at load.</summary>
public sealed class FormEngineTests
{
    private static readonly FormRequestContext Ctx = new("en", "demo", ["Admin"], "hamid");

    private static TranslatableText T(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    private static FormDefinition Form(params FieldDescriptor[] fields) => new()
    {
        Id = "demo", Title = T("Demo", "نمونه"), SubmitLabel = T("Save", "ذخیره"), Fields = fields
    };

    private static FieldDescriptor F(string name, FieldKind kind = FieldKind.Text) => new() { Name = name, Label = T(name, name), Kind = kind };

    private static Dictionary<string, string?> V(params (string, string?)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private static string En(TranslatableText t) => t.Resolve(new System.Globalization.CultureInfo("en"));
    private static string Fa(TranslatableText t) => t.Resolve(new System.Globalization.CultureInfo("fa"));

    // ---- conditions

    [Fact]
    public void Conditions_decide_visible_required_and_read_only_from_the_values()
    {
        var form = Form(F("kind", FieldKind.Select) with { Options = [new() { Value = "a", Label = "A" }, new() { Value = "b", Label = "B" }] },
            F("detail") with { Visible = "values.kind == 'b'", RequiredWhen = "values.kind == 'b'" },
            F("code") with { ReadOnlyWhen = "'Admin' in user.roles" });
        var a = FormEngine.Evaluate(form, V(("kind", "a")), Ctx);
        var b = FormEngine.Evaluate(form, V(("kind", "b")), Ctx);
        Assert.False(a["detail"].Visible);
        Assert.True(b["detail"].Visible && b["detail"].Required);
        Assert.True(a["code"].ReadOnly);   // the user is an Admin
        Assert.False(FormEngine.Evaluate(form, V(("kind", "b")), Ctx with { Roles = [] })["code"].ReadOnly);
    }

    [Fact]
    public void A_hidden_field_is_not_part_of_the_post_and_a_read_only_one_keeps_its_current_value()
    {
        var form = Form(F("flag", FieldKind.Switch), F("extra") with { Visible = "values.flag", Required = true }, F("locked") with { ReadOnlyWhen = "true" });
        var result = FormEngine.Validate(form, V(("flag", null), ("extra", "x"), ("locked", "hacked")), Ctx, new Dictionary<string, string?> { ["locked"] = "kept" });
        Assert.True(result.IsValid);                                   // the required field is hidden: not asked
        Assert.False(result.Values.ContainsKey("extra"));              // and its posted value is dropped
        Assert.Equal("kept", result.Values["locked"]);                 // the posted value of a read-only field is ignored
        Assert.Equal("false", result.Values["flag"]);
    }

    // ---- built-in checks and messages in both languages

    [Fact]
    public void A_required_field_that_is_empty_has_an_error_in_both_languages()
    {
        var result = FormEngine.Validate(Form(F("name") with { Required = true }), V(("name", "   ")), Ctx);
        Assert.False(result.IsValid);
        Assert.Equal("This field is required.", En(result.Errors["name"]));
        Assert.Equal("این فیلد الزامی است.", Fa(result.Errors["name"]));
    }

    [Fact]
    public void The_message_of_the_definition_replaces_the_built_in_one()
    {
        var f = F("name") with { Required = true, Messages = new FieldMessages { Required = T("Name please", "نام را بنویسید") } };
        var e = FormEngine.Validate(Form(f), V(("name", "")), Ctx).Errors["name"];
        Assert.Equal(("Name please", "نام را بنویسید"), (En(e), Fa(e)));
    }

    [Fact]
    public void Length_number_range_pattern_date_and_choice_checks()
    {
        var form = Form(F("a") with { MinLength = 3, MaxLength = 5 }, F("n", FieldKind.Number) with { Min = 1, Max = 10 }, F("p") with { Pattern = "[A-Z]{2}\\d+" },
            F("d", FieldKind.Date), F("s", FieldKind.Select) with { Options = [new() { Value = "x", Label = "X" }] });
        Assert.Equal(["a", "n", "p", "d", "s"], FormEngine.Validate(form, V(("a", "ab"), ("n", "11"), ("p", "ab1"), ("d", "2026-13-40"), ("s", "y")), Ctx).Errors.Keys.OrderBy(k => "anpds".IndexOf(k[0])));
        Assert.True(FormEngine.Validate(form, V(("a", "abcd"), ("n", "5"), ("p", "AB12"), ("d", "2026-11-18"), ("s", "x")), Ctx).IsValid);
        Assert.Equal("At most 5 characters.", En(FormEngine.Validate(form, V(("a", "abcdef")), Ctx).Errors["a"]));
        Assert.Equal("حداکثر ۵ نویسه مجاز است.".Replace('۵', '5'), Fa(FormEngine.Validate(form, V(("a", "abcdef")), Ctx).Errors["a"]));
    }

    [Fact]
    public void A_number_typed_with_persian_digits_is_the_same_number()
    {
        var form = Form(F("n", FieldKind.Number) with { Min = 10 });
        Assert.True(FormEngine.Validate(form, V(("n", "۱۵")), Ctx).IsValid);
        Assert.False(FormEngine.Validate(form, V(("n", "۵")), Ctx).IsValid);
        Assert.Equal(1234.5m, FormEngine.TryNumber("۱٬۲۳۴٫۵".Replace("٬", ","), out var n) ? n : -1);
    }

    [Fact]
    public void A_pattern_that_takes_too_long_is_a_failed_check_not_a_hang()
    {
        var form = Form(F("x") with { Pattern = "(a+)+$" });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = FormEngine.Validate(form, V(("x", new string('a', 60) + "!")), Ctx);
        Assert.False(result.IsValid);
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }

    // ---- rules

    [Fact]
    public void Field_rules_and_form_rules_show_their_message_on_the_named_field_or_on_the_form()
    {
        var form = Form(F("from", FieldKind.Number), F("to", FieldKind.Number) with { Rules = [new FieldRule { Expression = "values.to > 0", Message = T("Must be positive", "باید مثبت باشد") }] })
            with { Rules = [new FieldRule { Expression = "values.from <= values.to", Message = T("From is after to", "شروع بعد از پایان است"), Field = "from" }, new FieldRule { Expression = "values.from != 99", Message = T("99 is reserved", "۹۹ رزرو است") }] };
        var bad = FormEngine.Validate(form, V(("from", "5"), ("to", "-1")), Ctx);
        Assert.Equal("Must be positive", En(bad.Errors["to"]));
        Assert.Equal("From is after to", En(bad.Errors["from"]));
        var reserved = FormEngine.Validate(form, V(("from", "99"), ("to", "100")), Ctx);
        Assert.Empty(reserved.Errors);
        Assert.Equal("99 is reserved", En(reserved.FormErrors.Single()));
        Assert.True(FormEngine.Validate(form, V(("from", "1"), ("to", "2")), Ctx).IsValid);
    }

    // ---- the effective form

    [Fact]
    public void The_effective_form_carries_the_evaluated_flags_and_the_hydrated_options()
    {
        var form = Form(F("kind", FieldKind.Select) with { OptionsFrom = "kinds" }, F("x") with { RequiredWhen = "values.kind == 'b'" });
        var options = new Dictionary<string, IReadOnlyList<FieldOption>> { ["kind"] = [new() { Value = "b", Label = "B" }] };
        var shown = FormEngine.Effective(form, FormEngine.Evaluate(form, V(("kind", "b")), Ctx), options);
        Assert.Single(shown.Fields[0].Options!);
        Assert.True(shown.Fields[1].Required);
        Assert.True(FormEngine.Validate(shown, V(("kind", "zzz"), ("x", "v")), Ctx).Errors.ContainsKey("kind"));   // a value that is not in the hydrated options is refused
    }

    // ---- the definition is checked at load

    private static IReadOnlyList<string> Codes(FormDefinition f) => FormEngine.CheckDefinition(f).Select(e => e.Code + "@" + e.Pointer).ToList();

    [Fact]
    public void A_good_definition_has_no_findings()
        => Assert.Empty(FormEngine.CheckDefinition(Form(F("a") with { Visible = "values.b == 1", Rules = [new FieldRule { Expression = "values.a != ''", Message = T("x", "ی") }] }, F("b", FieldKind.Number))));

    [Fact]
    public void A_bad_expression_a_foreign_variable_a_missing_translation_and_an_unknown_rule_field_are_refused()
    {
        var bad = Form(F("a") with { Visible = "values.b ==", RequiredWhen = "page.title == 'x'", Label = TranslatableText.Of(("en", "A")) }) with
        {
            Rules = [new FieldRule { Expression = "values.a == 1", Message = T("m", "پ"), Field = "ghost" }]
        };
        var codes = Codes(bad);
        Assert.Contains(codes, c => c.EndsWith("@/fields/0/visible"));
        Assert.Contains(codes, c => c.EndsWith("@/fields/0/requiredWhen"));
        Assert.Contains("missing-translation@/fields/0/label", codes);
        Assert.Contains("unknown-field@/rules/0/field", codes);
    }

    [Fact]
    public void Options_come_from_one_place_and_a_choice_field_needs_them()
    {
        var overlap = Form(F("s", FieldKind.Select) with { OptionsFrom = "x", Options = [new() { Value = "a", Label = "A" }] });
        Assert.Contains("overlap@/fields/0/optionsFrom", Codes(overlap));
        Assert.Contains("required@/fields/0/options", Codes(Form(F("s", FieldKind.Select))));
        Assert.DoesNotContain(Codes(Form(F("s", FieldKind.Select) with { OptionsFrom = "x" })), c => c.Contains("options"));
    }

    [Fact]
    public void Columns_span_pattern_and_ranges_are_checked()
    {
        var f = Form(F("a") with { Span = 3, Pattern = "([", Min = 5, Max = 1 }) with { Columns = 3 };
        var codes = Codes(f);
        Assert.Contains("range@/columns", codes);
        Assert.Contains("invalid-pattern@/fields/0/pattern", codes);
        Assert.Contains("range@/fields/0/min", codes);
    }

    [Fact]
    public void The_definition_survives_a_json_round_trip_with_every_new_property()
    {
        var json = """
        {"id":"d","title":{"en":"D","fa":"د"},"submitLabel":{"en":"S","fa":"ذ"},"actionKey":"act","permission":"Admin.Create","columns":2,
         "fields":[{"name":"a","label":{"en":"A","fa":"ا"},"visible":"true","requiredWhen":"false","readOnlyWhen":"false","minLength":1,"min":1,"max":9,"pattern":"x","span":2,"optionsFrom":"src",
                    "rules":[{"expression":"true","message":{"en":"m","fa":"پ"}}],"messages":{"required":{"en":"r","fa":"ا"}}}],
         "rules":[{"expression":"true","message":{"en":"m","fa":"پ"},"field":"a"}]}
        """;
        var form = JsonSerializer.Deserialize<FormDefinition>(json, PemsJson.Options)!;
        Assert.Equal(("act", "Admin.Create", 2), (form.ActionKey, form.Permission, form.Columns));
        Assert.Equal("src", form.Fields[0].OptionsFrom);
        Assert.Equal(2, form.Fields[0].Span);
        Assert.Single(form.Fields[0].Rules!);
        Assert.Equal("r", En(form.Fields[0].Messages!.Required!));
        Assert.Equal("a", form.Rules![0].Field);
    }
}
