using System.Globalization;
using System.Text.Json;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-729 and SCRUM-697: the signature pad and the script island (the host of a front-end bundle) write the markup their scripts need and nothing a definition can use to load code from elsewhere.</summary>
public sealed class SignatureAndIslandTests
{
    private static string Render(string key, string optionsJson, string culture = "en")
    {
        var type = key == "signature-pad" ? typeof(SignaturePadOptions) : typeof(ScriptIslandOptions);
        var options = JsonSerializer.Deserialize(optionsJson, type, PemsJson.Options);
        return UiTestRenderer.Instance.RenderAsync(key, new Dictionary<string, object?> { ["Options"] = options }, new CultureInfo(culture)).GetAwaiter().GetResult();
    }

    [Fact]
    public void The_signature_pad_writes_the_canvas_the_two_buttons_and_the_action_the_script_posts_to()
    {
        var html = System.Net.WebUtility.HtmlDecode(Render("signature-pad", ComponentSamples.Get("signature-pad")!.ToJsonString()));
        Assert.Contains("data-pems-signature=\"\"", html);
        Assert.Contains("data-action=\"/a/profile-signature-save\"", html);
        Assert.Contains("data-field=\"signatureDataUrl\"", html);
        Assert.Contains("<canvas class=\"pems-signature-pad\"", html);
        Assert.Contains("data-pems-signature-clear", html);
        Assert.Contains("data-pems-signature-save", html);
        Assert.Contains("Current signature", html);
    }

    [Fact]
    public void The_signature_pad_speaks_the_language_of_the_request()
    {
        var html = System.Net.WebUtility.HtmlDecode(Render("signature-pad", ComponentSamples.Get("signature-pad")!.ToJsonString(), "fa"));
        Assert.Contains("پاک کردن", html);
        Assert.Contains("امضای فعلی", html);
    }

    [Fact]
    public void The_signature_pad_shows_the_current_image_only_for_a_path_of_this_site()
    {
        var other = ComponentSamples.Get("signature-pad")!.ToJsonString().Replace("/Files/Image/signature.png", "//evil.example/x.png");
        Assert.DoesNotContain("evil.example", Render("signature-pad", other));
    }

    [Fact]
    public void The_island_writes_the_root_the_boot_data_and_the_module_under_dist()
    {
        var html = Render("script-island", ComponentSamples.Get("script-island")!.ToJsonString());
        Assert.Contains("id=\"island\"", html);
        Assert.Contains("<script type=\"application/json\" id=\"island-boot\">", html);
        Assert.Contains("\"dataUrl\":\"/Data\"", html);
        Assert.Contains("\"locale\":\"en\"", html);
        Assert.Contains("\"Title\":\"Executive\"", html);
        Assert.Contains("<script type=\"module\" src=\"/dist/dashboards/dashboards.js\">", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/dist/dashboards/dashboards.css\"", html);
    }

    [Theory]
    [InlineData("https://evil.example/x.js")]
    [InlineData("//evil.example/x.js")]
    [InlineData("/js/site.js")]
    [InlineData("/dist/../js/site.js")]
    [InlineData("/dist/app.css")]
    public void The_island_refuses_a_module_that_is_not_a_js_file_under_dist(string module)
    {
        var json = ComponentSamples.Get("script-island")!.ToJsonString().Replace("/dist/dashboards/dashboards.js", module);
        var html = Render("script-island", json);
        Assert.DoesNotContain("type=\"module\"", html);
    }

    [Fact]
    public void The_island_boot_data_can_never_close_its_script_element()
    {
        var json = ComponentSamples.Get("script-island")!.ToJsonString().Replace("\"Executive\"", "\"</script><b>x\"");
        var html = Render("script-island", json);
        Assert.DoesNotContain("</script><b>", html);
        Assert.True(html.Contains("\\u003C/script"), html);
    }
}
