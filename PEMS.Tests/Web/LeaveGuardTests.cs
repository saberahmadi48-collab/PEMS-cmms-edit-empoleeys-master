using System.Runtime.CompilerServices;

namespace PEMS.Tests.Web;

/// <summary>SCRUM-488: the product has one leave guard (PemsForm.guard in pems-form.js); a page script must not add its own beforeunload handler.</summary>
public class LeaveGuardTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));

    [Fact]
    public void Only_the_shared_form_script_listens_to_beforeunload()
    {
        var offenders = Directory.GetFiles(Path.Combine(Root(), "PEMS.Web", "wwwroot", "js"), "*.js", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".min.js") && File.ReadAllText(f).Contains("beforeunload"))
            .Select(Path.GetFileName).ToList();
        Assert.True(offenders.Count == 0, "use PemsForm.guard(...) instead of beforeunload in: " + string.Join(", ", offenders));
        Assert.Contains("PemsForm", File.ReadAllText(Path.Combine(Root(), "PEMS.UI", "wwwroot", "pems-form.js")));
    }
}
