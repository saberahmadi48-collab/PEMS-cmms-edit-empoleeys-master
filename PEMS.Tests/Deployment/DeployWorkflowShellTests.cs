using System.Text.RegularExpressions;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-555: the deploy run could not show the text of the error page when the site did not start, because the sed
/// expression in deploy-site.yml was <c>s/&lt;style...&lt;/style&gt;//g</c>: the '/' of the closing tag ends the expression and
/// sed fails ("unknown option to `s'") on every poll. Reads the repository files only.
/// </summary>
public class DeployWorkflowShellTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void TheErrorPageFilter_UsesADelimiterThatIsNotInThePattern()
    {
        var wf = File.ReadAllText(Path.Combine(Repo(), ".github", "workflows", "deploy-site.yml"));
        var line = wf.Split('\n').Single(l => l.Contains("page_text() {"));
        Assert.Contains("s#<style[^>]*>[^<]*</style>##g", line);
        Assert.Contains("s#<[^>]*># #g", line);
    }

    [Fact]
    public void NoSlashDelimitedSedExpression_ContainsAClosingTag()
    {
        var files = Directory.GetFiles(Path.Combine(Repo(), ".github", "workflows"), "*.yml")
            .Concat(Directory.GetFiles(Path.Combine(Repo(), "scripts", "ci"), "*.sh"));
        var broken = new Regex(@"'s/[^']*</[^']*'");   // s/.../ with "</" inside: the '/' ends the pattern early
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => x.l.Contains("sed") && broken.IsMatch(x.l))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}")
            .ToList();
        Assert.Empty(hits);
    }
}
