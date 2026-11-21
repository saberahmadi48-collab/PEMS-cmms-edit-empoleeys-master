using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-59: a field of kind RichText renders as a labelled textarea that pems-rte.js turns into the editor.</summary>
public sealed class RichTextFieldRenderTests
{
    private static async Task<string> RenderAsync(FieldDescriptor d, string? value, string culture = "en", TranslatableText? error = null)
    {
        var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        return await renderer.RenderAsync("field", new Dictionary<string, object?> { ["Descriptor"] = d, ["Value"] = value, ["Error"] = error }, new CultureInfo(culture));
    }

    private static FieldDescriptor Body(bool required = false, bool readOnly = false) => new()
    {
        Name = "Body", Label = TranslatableText.Of(("en", "Body"), ("fa", "متن")),
        Kind = FieldKind.RichText, Required = required, ReadOnly = readOnly, MaxLength = 50,
        HelpText = TranslatableText.Literal("help")
    };

    [Fact]
    public async Task Renders_a_labelled_textarea_marked_for_the_editor()
    {
        var html = await RenderAsync(Body(required: true), "<p>x</p>");
        Assert.Contains("<label class=\"pems-field__label\" for=\"f-Body\">", html);
        Assert.Contains("<textarea id=\"f-Body\" name=\"Body\"", html);
        Assert.Contains("data-pems-rte", html);
        Assert.Contains("class=\"pems-input pems-rte\"", html);
        Assert.Contains("required", html);
        Assert.Contains("aria-describedby=\"f-Body-help\"", html);
        Assert.DoesNotContain("maxlength", html);                 // the limit applies to text, not to the HTML of the editor
    }

    [Fact]
    public async Task The_value_is_encoded_never_written_as_markup()
    {
        var html = await RenderAsync(Body(), "<p>x</p><script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Label_follows_the_language_and_an_error_marks_the_field()
    {
        var fa = await RenderAsync(Body(), null, "fa", TranslatableText.Literal("!"));
        Assert.Contains("متن", System.Net.WebUtility.HtmlDecode(fa));
        Assert.Contains("aria-invalid=\"true\"", fa);
        Assert.Contains("pems-input--invalid pems-rte", fa);
    }

    [Fact]
    public async Task A_read_only_field_stays_read_only()
    {
        var html = await RenderAsync(Body(readOnly: true), "<p>x</p>");
        Assert.Contains("readonly", html);
    }
}
