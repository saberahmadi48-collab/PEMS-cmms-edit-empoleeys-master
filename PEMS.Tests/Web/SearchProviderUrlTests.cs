using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-548: the help article results of the Ctrl+K palette and global search linked to <c>/Help/Article/{code}</c>, a 404,
/// because <c>HelpController.Article(string code)</c> reads the code from the query string. Every URL a search provider
/// builds must name an existing action, and a value in the path is only bound when that action has an <c>id</c> parameter.
/// </summary>
public class SearchProviderUrlTests
{
    private static readonly Regex UrlLiteral = new(@"Url\s*=\s*(?:[^;]*\?\s*)?\$?""(?<path>/[A-Za-z]+(?:/[A-Za-z]+)+)(?<id>/\{[^}""]+\})?(?<query>\?[^""]*)?""");

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public static IEnumerable<object[]> ProviderUrls()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Repo(), "PEMS.Persistence", "Servises", "SearchProviders"), "*SearchProvider.cs"))
            foreach (Match m in UrlLiteral.Matches(File.ReadAllText(file)))
                yield return new object[] { Path.GetFileName(file), m.Groups["path"].Value, m.Groups["id"].Success, m.Groups["query"].Value };
    }

    [Theory]
    [MemberData(nameof(ProviderUrls))]
    public void EverySearchResultUrl_NamesAnActionThatBindsIt(string provider, string path, bool hasPathValue, string query)
    {
        var segments = path.Trim('/').Split('/');
        var controllers = typeof(PEMS.Web.Controllers.HelpController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        // /Controller/Action or /Area/Controller/Action
        var (area, controller, action) = segments.Length == 3 ? (segments[0], segments[1], segments[2]) : ((string?)null, segments[0], segments[1]);
        var type = controllers.FirstOrDefault(t => t.Name == controller + "Controller"
            && (area == null || string.Equals(t.GetCustomAttribute<AreaAttribute>()?.RouteValue, area, StringComparison.OrdinalIgnoreCase)));
        Assert.True(type != null, $"{provider}: no controller for {path}");

        var methods = type!.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == action).ToList();
        Assert.True(methods.Count > 0, $"{provider}: {type.Name} has no action {action}");

        if (hasPathValue)
            Assert.True(methods.Any(m => m.GetParameters().Any(p => p.Name == "id")),
                $"{provider}: {path}/{{...}} puts a value in the path, but {type.Name}.{action} has no 'id' parameter to bind it");

        foreach (Match q in Regex.Matches(query, @"[?&](?<name>[A-Za-z]+)="))
            Assert.True(methods.Any(m => m.GetParameters().Any(p => string.Equals(p.Name, q.Groups["name"].Value, StringComparison.OrdinalIgnoreCase))),
                $"{provider}: {type.Name}.{action} has no parameter '{q.Groups["name"].Value}'");
    }

    [Fact]
    public void TheScan_FindsTheProviders()
        => Assert.True(ProviderUrls().Count() >= 15, "the URL scan found too few search providers");
}
