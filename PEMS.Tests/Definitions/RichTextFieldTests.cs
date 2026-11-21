using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PEMS.Persistence.Services;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-59: rich text is a standard field type of the form builder and its HTML is sanitized on the server.
/// Covers the sanitizer (XSS vectors, RTL/LTR formatting kept), the form value step and storing a form with a rich-text field.
/// </summary>
public class RichTextFieldTests
{
    private static readonly RichTextSanitizerService Sanitizer = new();

    // ---- the sanitizer removes everything that can run or redress the page

    [Theory]
    [InlineData("<p>ok</p><script>alert(1)</script>", "<script")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript:")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>", "alert(1)")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>", "vbscript:")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zz4=\">", "data:")]
    [InlineData("<svg onload=\"alert(1)\"></svg>", "<svg")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>", "<iframe")]
    [InlineData("<form action=\"/x\"><input name=\"p\"></form>", "<input")]
    [InlineData("<p style=\"background:url(javascript:alert(1))\">x</p>", "javascript")]
    [InlineData("<p style=\"position:fixed;top:0\">x</p>", "position")]
    [InlineData("<div onclick=\"alert(1)\">x</div>", "onclick")]
    [InlineData("<object data=\"x.swf\"></object>", "<object")]
    [InlineData("<p>x</p><style>body{display:none}</style>", "<style")]
    public void Sanitize_removes_scripts_handlers_and_unsafe_urls(string html, string forbidden)
    {
        var clean = Sanitizer.Sanitize(html);
        Assert.DoesNotContain(forbidden, clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_keeps_only_the_editor_classes()
    {
        var clean = Sanitizer.Sanitize("<p class=\"ql-align-right position-fixed ql-direction-rtl modal-backdrop\">x</p><span class=\"btn btn-danger\">y</span>");
        Assert.Contains("class=\"ql-align-right ql-direction-rtl\"", clean);
        Assert.DoesNotContain("position-fixed", clean);
        Assert.DoesNotContain("modal-backdrop", clean);
        Assert.DoesNotContain("btn", clean);
        Assert.Contains("<span>y</span>", clean);
    }

    [Fact]
    public void Sanitize_adds_noopener_to_links_that_open_a_new_window()
    {
        var clean = Sanitizer.Sanitize("<a href=\"https://example.com\" target=\"_blank\" rel=\"opener\">x</a><a href=\"/Letter/Details/1\">y</a>");
        Assert.Contains("rel=\"noopener noreferrer\"", clean);
        Assert.DoesNotContain("rel=\"opener\"", clean);
        Assert.Contains("href=\"/Letter/Details/1\"", clean);           // a relative link of PEMS stays
    }

    [Fact]
    public void Sanitize_keeps_right_to_left_and_left_to_right_formatting()
    {
        const string html = "<p class=\"ql-direction-rtl ql-align-right\" dir=\"rtl\">سلام <strong>PEMS</strong></p>"
                            + "<p dir=\"ltr\" style=\"text-align: left; direction: ltr\">Hello <em>world</em></p>"
                            + "<ol><li class=\"ql-indent-1\">یک</li></ol><a href=\"mailto:a@b.c\">mail</a><img src=\"/uploads/editor/a.png\" alt=\"a\">";
        var clean = Sanitizer.Sanitize(html);
        Assert.Contains("class=\"ql-direction-rtl ql-align-right\"", clean);
        Assert.Contains("dir=\"rtl\"", clean);
        Assert.Contains("سلام <strong>PEMS</strong>", clean);
        Assert.Contains("dir=\"ltr\"", clean);
        Assert.Contains("direction: ltr", clean);
        Assert.Contains("<em>world</em>", clean);
        Assert.Contains("class=\"ql-indent-1\"", clean);
        Assert.Contains("href=\"mailto:a@b.c\"", clean);
        Assert.Contains("src=\"/uploads/editor/a.png\"", clean);
    }

    // ---- the form value step

    private static FormDefinition Form() => new()
    {
        Id = "note",
        Title = TranslatableText.Literal("Note"),
        Action = "/notes",
        SubmitLabel = TranslatableText.Literal("Save"),
        Fields =
        [
            new FieldDescriptor { Name = "Title", Label = TranslatableText.Literal("Title") },
            new FieldDescriptor { Name = "Body", Label = TranslatableText.Literal("Body"), Kind = FieldKind.RichText, Required = true },
            new FieldDescriptor { Name = "Remark", Label = TranslatableText.Literal("Remark"), Kind = FieldKind.TextArea }
        ]
    };

    [Fact]
    public void Form_values_sanitize_rich_text_and_leave_every_other_value_as_posted()
    {
        var values = new FormValueSanitizer(Sanitizer).Sanitize(Form(), new Dictionary<string, string?>
        {
            ["Title"] = "<b>as typed</b> ۱۲",
            ["body"] = "<p dir=\"rtl\">متن<img src=x onerror=alert(1)></p><script>alert(2)</script>",
            ["Remark"] = "<script>shown encoded, never as HTML</script>",
            ["IsAdmin"] = "true"
        });

        Assert.Equal("<b>as typed</b> ۱۲", values["Title"]);
        Assert.Equal("<script>shown encoded, never as HTML</script>", values["Remark"]);
        var body = values["Body"]!;
        Assert.Contains("dir=\"rtl\"", body);
        Assert.Contains("متن", body);
        Assert.DoesNotContain("onerror", body);
        Assert.DoesNotContain("<script", body);
        Assert.False(values.ContainsKey("IsAdmin"));                    // a name the form does not declare is dropped
    }

    [Theory]
    [InlineData("<p><br></p>")]
    [InlineData("<p>   </p><p><br></p>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public void An_empty_editor_value_becomes_empty_so_a_required_check_sees_it(string posted)
    {
        var values = new FormValueSanitizer(Sanitizer).Sanitize(Form(), new Dictionary<string, string?> { ["Body"] = posted });
        Assert.Equal(string.Empty, values["Body"]);
    }

    [Fact]
    public void An_editor_value_with_only_an_image_is_kept()
    {
        var values = new FormValueSanitizer(Sanitizer).Sanitize(Form(), new Dictionary<string, string?> { ["Body"] = "<p><img src=\"/uploads/editor/a.png\"></p>" });
        Assert.Contains("<img", values["Body"]);
    }

    [Fact]
    public void An_mvc_post_is_sanitized_the_same_way()
    {
        var posted = new FormCollection(new Dictionary<string, StringValues>
        {
            ["Title"] = "T",
            ["Body"] = "<p>x</p><a href=\"javascript:alert(1)\">y</a>",
            ["__RequestVerificationToken"] = "token"
        });
        var values = new FormValueSanitizer(Sanitizer).Sanitize(Form(), posted);
        Assert.Equal(new[] { "Body", "Title" }, values.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.DoesNotContain("javascript", values["Body"]);
    }

    // ---- the form builder stores a form with a rich-text field

    private const string RichTextForm = """{"id":"note","title":{"en":"Note","fa":"یادداشت"},"action":"/notes","fields":[{"name":"body","label":{"en":"Body","fa":"متن"},"kind":"richText","required":true}],"submitLabel":{"en":"Save","fa":"ذخیره"}}""";

    [Fact]
    public void Rich_text_is_a_field_kind_in_the_stored_json()
    {
        var form = PemsJson.Deserialize<FormDefinition>(RichTextForm);
        Assert.Equal(FieldKind.RichText, form.Fields[0].Kind);
        Assert.Contains("\"kind\":\"richText\"", PemsJson.Serialize(form));
        Assert.Empty(new FormDefinitionKind().Validate(RichTextForm, new()));
    }

    [Fact]
    public async Task A_form_with_a_rich_text_field_is_saved_and_published_by_the_definition_store()
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var created = await s.CreateAsync(DefinitionTestWorld.CreateForm("notes.new", RichTextForm), "tester");
        Assert.True(created.Success, string.Join(";", created.Errors?.Select(e => e.Pointer + " " + e.Code) ?? []));
        var draft = await s.GetDraftAsync(created.Value!.Summary.Id);
        var published = await s.PublishAsync(created.Value.Summary.Id, draft.Value!.Info.Revision, "tester");
        Assert.True(published.Success, string.Join(";", published.Errors?.Select(e => e.Pointer + " " + e.Code) ?? []));
    }
}
