using PEMS.Application.Definitions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-451: the kinds view and theme: a valid sample, refusals with a pointer, and the registration of every kind of the product.</summary>
public sealed class ViewAndThemeKindsTests
{
    private const string ViewSample = """
        { "columns": [
            { "key": "name", "header": { "en": "Name", "fa": "نام" }, "sortable": true, "kind": "text" },
            { "key": "qty", "header": { "en": "Quantity", "fa": "تعداد" }, "align": "end", "kind": "number" },
            { "key": "active", "header": { "en": "Active", "fa": "فعال" }, "trueText": { "en": "Yes", "fa": "بله" }, "falseText": { "en": "No", "fa": "خیر" } } ],
          "emptyText": { "en": "No rows", "fa": "ردیفی نیست" } }
        """;

    private const string ThemeSample = """
        { "mode": "dark", "tokens": { "--pems-accent": "#d98c2b", "--pems-bg": "rgb(12, 21, 36)", "--pems-radius": "6px", "--pems-shadow-offset": "0 2px 4px", "--pems-surface": "var(--pems-navy)" } }
        """;

    private static IReadOnlyList<DefinitionFieldError> View(string payload) => new ViewDefinitionKind().Validate(payload, new DefinitionLimits());
    private static IReadOnlyList<DefinitionFieldError> Theme(string payload) => new ThemeDefinitionKind().Validate(payload, new DefinitionLimits());

    [Fact]
    public void The_samples_are_valid() { Assert.Empty(View(ViewSample)); Assert.Empty(Theme(ThemeSample)); }

    [Theory]
    [InlineData("""{ "columns": [] }""", "/columns", "required")]
    [InlineData("""{ "columns": [ { "key": "a b", "header": { "en": "A", "fa": "ا" } } ] }""", "/columns/0/key", "invalid-key")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" } }, { "key": "a", "header": { "en": "A", "fa": "ا" } } ] }""", "/columns/1/key", "duplicate")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A" } } ] }""", "/columns/0/header", "missing-translation")]
    [InlineData("""{ "columns": [ { "key": "a" } ] }""", "/columns/0/header", "required")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" }, "align": "left" } ] }""", "/columns/0/align", "invalid-value")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" }, "kind": "money" } ] }""", "/columns/0/kind", "invalid-value")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" }, "css": "x" } ] }""", "/columns/0/css", "unknown-property")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" } } ], "emptyText": { "en": "x" } }""", "/emptyText", "missing-translation")]
    [InlineData("""{ "columns": [ { "key": "a", "header": { "en": "A", "fa": "ا" } } ], "rows": [] }""", "/rows", "unknown-property")]
    public void A_bad_view_is_refused_with_the_pointer_and_the_reason(string payload, string pointer, string code)
        => Assert.Contains(View(payload), e => e.Pointer == pointer && e.Code == code);

    [Theory]
    [InlineData("""{ "mode": "sepia", "tokens": { "--pems-a": "#fff" } }""", "/mode", "invalid-value")]
    [InlineData("""{ "mode": "dark", "tokens": {} }""", "/tokens", "required")]
    [InlineData("""{ "mode": "dark", "tokens": { "color": "#fff" } }""", "/tokens/color", "invalid-name")]
    [InlineData("""{ "mode": "dark", "tokens": { "--other-a": "#fff" } }""", "/tokens/--other-a", "invalid-name")]
    [InlineData("""{ "mode": "dark", "tokens": { "--pems-a": "url(http://evil.example/x)" } }""", "/tokens/--pems-a", "invalid-value")]
    [InlineData("""{ "mode": "dark", "tokens": { "--pems-a": "red; } body { display: none" } }""", "/tokens/--pems-a", "invalid-value")]
    [InlineData("""{ "mode": "dark", "tokens": { "--pems-a": "expression(alert(1))" } }""", "/tokens/--pems-a", "invalid-value")]
    [InlineData("""{ "mode": "dark", "tokens": { "--pems-a": 5 } }""", "/tokens/--pems-a", "invalid-value")]
    [InlineData("""{ "mode": "dark", "tokens": { "--pems-a": "#fff" }, "css": "x" }""", "/css", "unknown-property")]
    public void A_bad_theme_is_refused_with_the_pointer_and_the_reason(string payload, string pointer, string code)
        => Assert.Contains(Theme(payload), e => e.Pointer == pointer && e.Code == code);

    [Fact]
    public void The_product_registers_every_kind_once_and_the_two_new_kinds_among_them()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddPemsDefinitionKinds();
        var keys = services.Where(s => s.ServiceType == typeof(IDefinitionKind)).Select(s => s.ImplementationType!).ToList();
        Assert.Contains(typeof(ViewDefinitionKind), keys);
        Assert.Contains(typeof(ThemeDefinitionKind), keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
