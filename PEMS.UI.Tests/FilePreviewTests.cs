using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-530: <c>pems-file-preview</c> shows a stored file inline through the same-site preview URL.</summary>
public sealed class FilePreviewTests
{
    private static string Run(PemsFilePreviewTagHelper helper, TagHelperAttributeList? attributes = null)
    {
        attributes ??= [];
        var context = new TagHelperContext("pems-file-preview", attributes, new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput("pems-file-preview", attributes, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void A_source_is_shown_in_a_named_lazy_frame()
    {
        var html = Run(new PemsFilePreviewTagHelper(UiTestRenderer.Instance) { Src = "/Files/Preview/12", Title = "Drawing <A>.pdf" }, [new("id", "pv")]);
        Assert.Contains("<div class=\"pems-file-preview\" id=\"pv\">", html);
        Assert.Contains("<iframe class=\"pems-file-preview__frame\" src=\"/Files/Preview/12\" title=\"Drawing &lt;A&gt;.pdf\" loading=\"lazy\" referrerpolicy=\"same-origin\"></iframe>", html);
    }

    [Fact]
    public void No_source_shows_the_empty_text_and_no_frame()
    {
        var html = Run(new PemsFilePreviewTagHelper(UiTestRenderer.Instance) { EmptyText = "Nothing to show" });
        Assert.Contains("pems-file-preview--empty", html);
        Assert.Contains("Nothing to show", html);
        Assert.DoesNotContain("<iframe", html);
    }

    [Fact]
    public void Styles_use_the_theme_tokens()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.UI", "PEMS.UI.csproj"))) dir = dir.Parent;
        var css = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.UI", "wwwroot", "pems-ui.css"));
        Assert.Contains(".pems-file-preview { border: 1px solid var(--pems-surface-border);", css);
        Assert.Contains(".pems-file-preview__frame {", css);
    }
}
