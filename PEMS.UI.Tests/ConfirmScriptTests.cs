using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-483 / #483: the shared confirm dialog (pems-confirm.js) is loaded once by both layouts, its texts are inputs from the layout, and it stays inline-handler free.</summary>
public sealed class ConfirmScriptTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Theory]
    [InlineData("_Layout.cshtml")]
    [InlineData("_LayoutEmbed.cshtml")]
    public void The_layouts_load_the_script_once_after_the_overlay_script_and_carry_the_texts(string layout)
    {
        var html = Read("PEMS.Web", "Views", "Shared", layout);
        Assert.Equal(1, Regex.Matches(html, Regex.Escape("_content/PEMS.UI/pems-confirm.js")).Count);
        Assert.True(html.IndexOf("pems-overlay.js", StringComparison.Ordinal) < html.IndexOf("pems-confirm.js", StringComparison.Ordinal));
        foreach (var name in new[] { "title", "ok", "cancel" }) Assert.Contains($"data-pems-confirm-{name}=\"@Localizer[\"Confirm_", html);
    }

    [Theory]
    [InlineData("SharedResource.resx")]
    [InlineData("SharedResource.fa.resx")]
    public void The_dialog_texts_exist_in_both_languages(string resource)
    {
        var xml = Read("PEMS.Web", "Resources", resource);
        foreach (var key in new[] { "Confirm_Title", "Confirm_Ok", "Confirm_Cancel" }) Assert.Contains($"name=\"{key}\"", xml);
    }

    [Fact]
    public void The_script_asks_before_a_click_or_a_submit_and_exposes_a_promise_api()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-confirm.js");
        Assert.Contains("data-pems-confirm", js);
        Assert.Contains("addEventListener('click'", js);
        Assert.Contains("addEventListener('submit'", js);
        Assert.Contains("requestSubmit", js);
        Assert.Contains("window.PemsDialog", js);
        Assert.DoesNotContain("window.confirm", js);
        Assert.DoesNotContain("onclick=", js);
    }

    [Fact]
    public void No_view_asks_with_the_browser_confirm_dialog_in_a_simple_inline_handler()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root(), "PEMS.Web"), "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"on(click|submit)=""return confirm\('"))
            .Select(f => Path.GetRelativePath(Root(), f)).ToList();
        Assert.True(offenders.Count == 0, "Use data-pems-confirm=\"text\" instead of an inline confirm in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_script_checks_a_required_field_before_it_asks()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-confirm.js");
        Assert.Contains("data-pems-require", js);
        Assert.Contains("is-invalid", js);
        // blank confirm text means no question (a view may pass an empty attribute)
        Assert.Contains("function wants(node)", js);
    }

    [Fact]
    public void No_script_of_the_web_project_uses_the_browser_confirm_dialog()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root(), "PEMS.Web", "wwwroot", "js"), "*.js", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"(?<![\w.])confirm\(|window\.confirm\("))
            .Select(f => Path.GetRelativePath(Root(), f)).ToList();
        Assert.True(offenders.Count == 0, "Use data-pems-confirm or window.PemsDialog.confirm in: " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("WorkOrder")]
    [InlineData("WorkRequest")]
    public void The_lifecycle_buttons_declare_their_required_comment_and_question_without_inline_script(string folder)
    {
        var html = Read("PEMS.Web", "Views", folder, "Details.cshtml");
        // SCRUM-58: the Work Request page uses <pems-workflow-action-bar>, which writes the same two attributes (the component's own test checks them)
        if (folder == "WorkRequest") { Assert.Contains("<pems-workflow-action-bar", html); html += Read("PEMS.UI", "Components", "PemsWorkflowActionBar.razor"); }
        Assert.Contains("data-pems-require=", html);
        Assert.Contains("data-pems-confirm=", html);
        Assert.DoesNotContain("this.form.comment", html);
        Assert.DoesNotContain("return confirm(", html);
    }

    [Fact]
    public void The_shared_modal_script_fills_the_modal_from_the_opening_button_and_resets_it_first()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-modal-fill.js");
        Assert.Contains("show.bs.modal", js);
        Assert.Contains("relatedTarget", js);
        Assert.Contains("data-fill-", js);
        Assert.Contains("data-fill-text", js);
        Assert.Contains("form.reset()", js);
        Assert.DoesNotContain("innerHTML", js);   // values are text, never markup
    }

    [Theory]
    [InlineData("_Layout.cshtml")]
    [InlineData("_LayoutEmbed.cshtml")]
    public void The_layouts_load_the_shared_modal_script_once(string layout)
        => Assert.Equal(1, Regex.Matches(Read("PEMS.Web", "Views", "Shared", layout), Regex.Escape("_content/PEMS.UI/pems-modal-fill.js")).Count);

    // ---- SCRUM-467: the one client error script (fetch wrapper, toast, global capture)
    private static readonly string[] ErrorKeys = { "Generic", "Offline", "Timeout", "SignIn", "Forbidden", "NotFound", "Conflict", "Invalid", "TooMany", "Unavailable", "Server", "Reference", "Copy", "Copied", "Close" };

    [Theory]
    [InlineData("_Layout.cshtml")]
    [InlineData("_LayoutEmbed.cshtml")]
    public void The_layouts_load_the_error_script_and_style_once_and_carry_every_text(string layout)
    {
        var html = Read("PEMS.Web", "Views", "Shared", layout);
        Assert.Equal(1, Regex.Matches(html, Regex.Escape("_content/PEMS.UI/pems-errors.js")).Count);
        Assert.Equal(1, Regex.Matches(html, Regex.Escape("_content/PEMS.UI/pems-errors.css")).Count);
        foreach (var key in ErrorKeys) Assert.Contains($"=\"@Localizer[\"Err_{key}\"]\"", html);
    }

    [Theory]
    [InlineData("SharedResource.resx")]
    [InlineData("SharedResource.fa.resx")]
    public void The_error_texts_exist_in_both_languages(string resource)
    {
        var xml = Read("PEMS.Web", "Resources", resource);
        foreach (var key in ErrorKeys) Assert.Contains($"name=\"Err_{key}\"", xml);
    }

    [Fact]
    public void The_error_script_wraps_fetch_reads_problem_json_and_the_trace_id_and_retries_only_safe_requests()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-errors.js");
        Assert.Contains("window.PemsFetch", js);
        Assert.Contains("X-Trace-Id", js);
        Assert.Contains("RequestVerificationToken", js);
        Assert.Contains("AbortController", js);
        Assert.Contains("pagehide", js);
        Assert.Contains("unhandledrejection", js);
        Assert.Contains("/^(GET|HEAD)$/", js);              // retry is for idempotent verbs only
        Assert.Contains("role=\"alert\"", Read("PEMS.UI", "Components", "PemsScriptTemplates.razor"));   // the toast template is announced to assistive technology (SCRUM-512)
        Assert.DoesNotContain("innerHTML", js);              // messages and references are text, never markup
        Assert.DoesNotContain("alert(", js.Replace("role', 'alert'", ""));
        Assert.DoesNotContain("onclick=", js);
        var css = Read("PEMS.UI", "wwwroot", "pems-errors.css");
        Assert.DoesNotContain("#", css.Replace("/* PEMS.UI client error toast (SCRUM-467)", ""));   // tokens only, no literal colours
        Assert.DoesNotMatch(@"(^|[^-a-z])(left|right)\s*:|margin-(left|right)|padding-(left|right)", css);
    }

    [Theory]
    [InlineData("Views/VDR/Details.cshtml", "replyModal")]
    [InlineData("Views/EngineeringDocuments/Details.cshtml", "engReplyModal")]
    [InlineData("Views/QC/ItpDetails.cshtml", "inspectModal")]
    [InlineData("Views/ProductionRun/Details.cshtml", "postInputModal")]
    [InlineData("Views/ProductionRun/Details.cshtml", "postOutputModal")]
    [InlineData("Views/RcmFmea/ClassDetail.cshtml", "applyRcmModal")]
    [InlineData("Areas/Warehouse/Views/InventoryMasterData/Index.cshtml", "addLocationModal")]
    [InlineData("Views/SparePart/Index.cshtml", "editPartModal")]
    public void A_list_has_one_shared_modal_and_its_openers_carry_the_row_values(string view, string modal)
    {
        var html = Read(new[] { "PEMS.Web" }.Concat(view.Split('/')).ToArray());
        Assert.Equal(1, Regex.Matches(html, $"<pems-modal id=\"{modal}\"").Count);
        Assert.DoesNotContain($"id=\"{modal}_@", html);
        Assert.DoesNotContain($"id=\"{modal}-@", html);
        Assert.Matches($"data-bs-target=\"#{modal}\"[^>]*data-fill-", html);
    }
}
