using PEMS.Application.Validation;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Validation;

/// <summary>SCRUM-372, 373 and 380: the validator, its stable codes, the texts and the one error shape. The catalog document, the code constants and the messages must agree.</summary>
public sealed class FieldValidatorTests
{
    private static FieldConstraints F(string name, FieldType type = FieldType.Text, Func<FieldConstraints, FieldConstraints>? with = null)
    {
        var f = new FieldConstraints { Name = name, Type = type };
        return with is null ? f : with(f);
    }

    private static IReadOnlyDictionary<string, string?> V(params (string Key, string? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static string[] Codes(IEnumerable<Violation> v) => v.Select(x => x.Code).ToArray();

    // ---- catalog, codes and texts agree

    [Fact]
    public void Every_code_has_a_text_in_both_languages_a_message_key_and_a_row_in_the_catalog_document()
    {
        var doc = File.ReadAllText(Path.Combine(SupportedReleases.RepoRoot(), "docs", "architecture", "FIELD_VALIDATION_CATALOG.md"));
        Assert.Equal(ConstraintCodes.All.Count, ConstraintCodes.All.Distinct().Count());
        foreach (var code in ConstraintCodes.All)
        {
            Assert.True(ValidationMessages.Has(code), code + " has no text");
            var text = ValidationMessages.Text(code, new Dictionary<string, object?> { ["min"] = 1, ["max"] = 2, ["scale"] = 3 });
            Assert.Empty(text.MissingLanguages(["en", "fa"]));
            Assert.DoesNotContain("{", text.Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));   // every parameter placeholder is filled
            Assert.Contains($"| `{code}` |", doc);
        }
        Assert.Equal("Val_MaxLength", ConstraintCodes.MessageKey(ConstraintCodes.MaxLength));
        Assert.Equal("Val_NotANumber", ConstraintCodes.MessageKey(ConstraintCodes.NotANumber));
    }

    // ---- per type and constraint

    [Theory]
    [InlineData(FieldType.Text, "abcdef", ConstraintCodes.MaxLength)]
    [InlineData(FieldType.Text, "a", ConstraintCodes.MinLength)]
    [InlineData(FieldType.Number, "abc", ConstraintCodes.NotANumber)]
    [InlineData(FieldType.Number, "0", ConstraintCodes.Min)]
    [InlineData(FieldType.Number, "11", ConstraintCodes.Max)]
    [InlineData(FieldType.Money, "5.123", ConstraintCodes.Scale)]
    [InlineData(FieldType.Date, "2026-13-40", ConstraintCodes.NotADate)]
    [InlineData(FieldType.Date, "2020-01-01", ConstraintCodes.DateBefore)]
    [InlineData(FieldType.Date, "2030-01-01", ConstraintCodes.DateAfter)]
    [InlineData(FieldType.Time, "25:61", ConstraintCodes.NotATime)]
    [InlineData(FieldType.Select, "z", ConstraintCodes.NotAllowed)]
    [InlineData(FieldType.Tags, "a,b,c,d", ConstraintCodes.MaxItems)]
    [InlineData(FieldType.Tags, "a", ConstraintCodes.MinItems)]
    public void Each_constraint_reports_its_own_stable_code(FieldType type, string value, string code)
    {
        var f = F("f", type, x => x with
        {
            MaxLength = type == FieldType.Text && code == ConstraintCodes.MaxLength ? 3 : null,
            MinLength = type == FieldType.Text && code == ConstraintCodes.MinLength ? 3 : null,
            Min = type == FieldType.Number ? 1 : null, Max = type == FieldType.Number ? 10 : null, Scale = type == FieldType.Money ? 2 : null,
            MinDate = "2025-01-01", MaxDate = "2027-01-01", Options = ["a", "b"], MinItems = 2, MaxItems = 3,
        });
        var violations = FieldValidator.CheckValue(f, value);
        Assert.Contains(code, Codes(violations));
        Assert.All(violations, v => Assert.Equal("/f", v.Pointer));
    }

    [Theory]
    [InlineData(FieldType.Number, "۱۲٫۵")]          // Persian digits and the Persian decimal separator
    [InlineData(FieldType.Number, "1,234.5")]        // thousands separator
    [InlineData(FieldType.Money, "10.50")]
    [InlineData(FieldType.Date, "۲۰۲۶-۰۱-۱۵")]
    [InlineData(FieldType.Time, "08:30")]
    [InlineData(FieldType.Select, "a")]
    [InlineData(FieldType.Tags, "x, y")]
    public void Valid_values_of_every_type_pass_including_persian_digits(FieldType type, string value)
    {
        var f = F("f", type, x => x with { Scale = 2, MinDate = "2025-01-01", MaxDate = "2027-01-01", Options = ["a", "b"], MinItems = 1, MaxItems = 3 });
        Assert.Empty(FieldValidator.CheckValue(f, value));
    }

    [Fact]
    public void A_scale_of_zero_means_whole_numbers_only_and_trailing_zeros_do_not_count()
    {
        var whole = F("n", FieldType.Number, x => x with { Scale = 0 });
        Assert.Empty(FieldValidator.CheckValue(whole, "12"));
        Assert.Empty(FieldValidator.CheckValue(whole, "12.00"));
        Assert.Equal([ConstraintCodes.Scale], Codes(FieldValidator.CheckValue(whole, "12.5")));
    }

    [Fact]
    public void A_pattern_must_match_the_whole_value_and_a_runaway_pattern_is_a_refusal()
    {
        var f = F("code", FieldType.Text, x => x with { Pattern = "[A-Z]{3}-\\d{2}" });
        Assert.Empty(FieldValidator.CheckValue(f, "ABC-12"));
        Assert.Equal([ConstraintCodes.Pattern], Codes(FieldValidator.CheckValue(f, "xABC-12")));
        var evil = F("e", FieldType.Text, x => x with { Pattern = "(a+)+$" });
        Assert.Equal([ConstraintCodes.Pattern], Codes(FieldValidator.CheckValue(evil, new string('a', 40) + "!")));
    }

    // ---- create and update modes

    private static readonly IReadOnlyList<FieldConstraints> Fields =
    [
        F("name", FieldType.Text, x => x with { Required = true, MaxLength = 5 }),
        F("qty", FieldType.Number, x => x with { Required = true, Min = 1 }),
        F("note", FieldType.Text),
    ];

    [Fact]
    public void Create_requires_every_required_field_and_whitespace_counts_as_empty()
    {
        var result = FieldValidator.Validate(Fields, V(("name", "   ")), ValidationMode.Create);
        Assert.False(result.IsValid);
        Assert.Equal([ConstraintCodes.Required, ConstraintCodes.Required], Codes(result.Violations));
        Assert.Equal(["/name", "/qty"], result.Violations.Select(v => v.Pointer));
    }

    [Fact]
    public void Update_checks_only_the_keys_that_are_present_so_an_omitted_key_never_clears_or_fails()
    {
        Assert.True(FieldValidator.Validate(Fields, V(("note", "hello")), ValidationMode.Update).IsValid);
        var present = FieldValidator.Validate(Fields, V(("qty", "0")), ValidationMode.Update);
        Assert.Equal([ConstraintCodes.Min], Codes(present.Violations));
        var blanked = FieldValidator.Validate(Fields, V(("name", "")), ValidationMode.Update);    // sending an empty required value is a refusal, not a clear
        Assert.Equal([ConstraintCodes.Required], Codes(blanked.Violations));
    }

    [Fact]
    public void All_violations_are_returned_and_the_list_is_capped_by_the_setting()
    {
        var many = Enumerable.Range(0, 10).Select(i => F("f" + i, FieldType.Number, x => x with { Min = 1 })).ToList();
        var values = many.ToDictionary(f => f.Name, _ => (string?)"0");
        var all = FieldValidator.Validate(many, values, ValidationMode.Create);
        Assert.Equal(10, all.Violations.Count);
        Assert.False(all.Truncated);
        var capped = FieldValidator.Validate(many, values, ValidationMode.Create, new ValidationOptions { MaxErrors = 4 });
        Assert.Equal(4, capped.Violations.Count);
        Assert.True(capped.Truncated);
    }

    [Fact]
    public void An_advisory_field_warns_without_making_the_result_invalid_and_is_not_in_the_error_list()
    {
        var f = F("w", FieldType.Number, x => x with { Max = 5, Advisory = true });
        var result = FieldValidator.Validate([f], V(("w", "9")), ValidationMode.Create);
        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Violations);
        Assert.True(warning.Advisory);
        Assert.Empty(ValidationMessages.ToApiErrors(result.Violations, "en"));
    }

    // ---- one error shape (SCRUM-380)

    [Fact]
    public void Violations_become_the_rfc_9457_field_errors_with_pointer_code_key_parameters_and_a_message_in_the_users_language()
    {
        var result = FieldValidator.Validate(Fields, V(("name", "toolong"), ("qty", "0")), ValidationMode.Create);
        var en = ValidationMessages.ToApiErrors(result.Violations, "en");
        Assert.Equal(["/name", "/qty"], en.Select(e => e.Pointer));
        Assert.Equal([ConstraintCodes.MaxLength, ConstraintCodes.Min], en.Select(e => e.Code));
        Assert.Equal(["Val_MaxLength", "Val_Min"], en.Select(e => e.MessageKey));
        Assert.Equal("At most 5 characters.", en[0].Message);
        Assert.Equal(5, en[0].Params!["max"]);
        var fa = ValidationMessages.ToApiErrors(result.Violations, "fa");
        Assert.Equal("حداکثر 5 نویسه مجاز است.", fa[0].Message);
        Assert.Equal(en.Select(e => (e.Pointer, e.Code, e.MessageKey)), fa.Select(e => (e.Pointer, e.Code, e.MessageKey)));   // only the language differs
    }

    [Fact]
    public void The_form_engine_uses_the_same_validator_and_keeps_its_texts()
    {
        var form = new PEMS.UI.Abstractions.FormDefinition
        {
            Title = "t", Id = "k", SubmitLabel = "Save",
            Fields =
            [
                new PEMS.UI.Abstractions.FieldDescriptor { Name = "qty", Label = "Quantity", Kind = PEMS.UI.Abstractions.FieldKind.Number, Min = 1, Required = true },
            ],
        };
        var ctx = new FormRequestContext("en", "k", [], "u");
        var bad = FormEngine.Validate(form, new Dictionary<string, string?> { ["qty"] = "0" }, ctx);
        Assert.Equal("The smallest allowed value is 1.", bad.Errors["qty"].Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.True(FormEngine.Validate(form, new Dictionary<string, string?> { ["qty"] = "۳" }, ctx).IsValid);
        Assert.Equal("This field is required.", FormEngine.Validate(form, new Dictionary<string, string?> { ["qty"] = "" }, ctx).Errors["qty"].Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
    }
}
