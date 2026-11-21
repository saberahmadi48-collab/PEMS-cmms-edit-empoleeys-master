using PEMS.Tests.TestSupport;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-549: no script or view shows a browser pop-up. Messages are a PEMS toast (PemsErrors.show / PemsFetch),
/// questions are the PEMS confirm dialog (data-pems-confirm / PemsDialog). Third-party files under lib/ are not ours.
/// </summary>
public class NoBrowserPopupScriptTests
{
    private static readonly Regex PopupCall = new(@"(\bwindow\.(alert|confirm|prompt)|(?<![\w.$'""-])(alert|confirm|prompt))\s*\(", RegexOptions.Compiled);
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;

    private static IEnumerable<string> OurFiles()
    {
        var root = Root();
        var scripts = new[] { Path.Combine(root, "PEMS.Web", "wwwroot", "js"), Path.Combine(root, "PEMS.UI", "wwwroot") }
            .SelectMany(d => SourceTree.Files(d, "*.js"));
        var views = new[] { Path.Combine(root, "PEMS.Web", "Views"), Path.Combine(root, "PEMS.Web", "Areas") }
            .SelectMany(d => SourceTree.Files(d, "*.cshtml"));
        return scripts.Concat(views).Where(f => !f.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar) && !f.EndsWith(".min.js"));
    }

    [Fact]
    public void NoScriptOrView_CallsAlertConfirmOrPrompt()
    {
        var hits = new List<string>();
        foreach (var file in OurFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*") || code.StartsWith("@*")) continue;   // comments
                var cut = code.IndexOf(" // ", StringComparison.Ordinal);
                if (cut >= 0) code = code[..cut];
                if (PopupCall.IsMatch(code)) hits.Add($"{Path.GetRelativePath(Root(), file)}:{i + 1}");
            }
        }
        Assert.True(hits.Count == 0, "Browser pop-up calls (use PemsErrors.show or the PEMS confirm dialog): " + string.Join(", ", hits));
    }

    [Fact]
    public void TheTaskBoard_RichTextUpload_AndCalendar_ShowAToast()
    {
        Assert.Contains("PemsErrors.show", SourceTree.Text(Path.Combine(Root(), "PEMS.Web", "wwwroot", "js", "pems-task-manager.js")));
        Assert.Contains("PemsErrors.show(t('uploadFailed'))", SourceTree.Text(Path.Combine(Root(), "PEMS.UI", "wwwroot", "pems-rte.js")));
        Assert.Contains("PemsErrors.show(message)", SourceTree.Text(Path.Combine(Root(), "PEMS.Web", "wwwroot", "js", "pems-maintenance-calendar.js")));
    }
}
