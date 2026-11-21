using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-611 (#611, guideline 04 section 7): no new Razor view, no new controller and no new controller action is added when a definition can express it. The baseline <c>plan/sheets/definition-ratchet.json</c> lists every
/// view and the number of actions of every controller; the gate fails with the file or controller that is new. A view or action of a controller that is a host or static by decision (classification table, SCRUM-628) is allowed.
/// When code really is the right answer (a new generic host, a sign-in page) run this test once with <c>PEMS_WRITE_RATCHET=1</c> and say why in the pull request: the baseline diff is what the reviewer reads.
/// Converting a controller to definitions only lowers the numbers, and the baseline is rewritten the same way.
/// </summary>
public sealed class DefinitionRatchetTests
{
    private static string Root => SupportedReleases.RepoRoot();
    private static string BaselinePath => Path.Combine(Root, "plan", "sheets", "definition-ratchet.json");

    public sealed record Snapshot(IReadOnlyList<string> Views, IReadOnlyDictionary<string, int> Actions);

    /// <summary>Everything the gate compares: the views under PEMS.Web and the public action methods of every controller.</summary>
    public static Snapshot Current()
    {
        var web = Path.Combine(Root, "PEMS.Web");
        var views = Directory.GetFiles(web, "*.cshtml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var actions = typeof(PEMS.Web.Definitions.RouteTable).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && !t.IsNested && typeof(ControllerBase).IsAssignableFrom(t))
            .ToDictionary(t => t.FullName!, t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null), StringComparer.Ordinal);
        return new Snapshot(views, actions);
    }

    /// <summary>What is new compared with the baseline, as messages that name the file or controller. <paramref name="exemptControllers"/> are controller type names (host or static by decision).</summary>
    public static IReadOnlyList<string> Compare(Snapshot baseline, Snapshot current, ISet<string> exemptControllers)
    {
        var problems = new List<string>();
        var known = baseline.Views.ToHashSet(StringComparer.Ordinal);
        foreach (var view in current.Views.Where(v => !known.Contains(v)))
        {
            if (IsExemptView(view, exemptControllers)) continue;
            problems.Add($"new view {view}: a page or form definition can express a screen (guideline 04 section 7); add a definition instead of a view");
        }
        foreach (var (type, count) in current.Actions)
        {
            if (exemptControllers.Contains(type)) continue;
            if (!baseline.Actions.TryGetValue(type, out var before)) problems.Add($"new controller {type}: a definition can express it; add definitions, not a controller");
            else if (count > before) problems.Add($"controller {type} has {count - before} new action(s) ({before} -> {count}): add an action or data source definition instead of a controller action");
        }
        return problems;
    }

    private static bool IsExemptView(string view, ISet<string> exemptControllers)
    {
        var parts = view.Split('/');
        var at = Array.IndexOf(parts, "Views");
        if (at < 0 || at + 1 >= parts.Length) return false;
        var folder = parts[at + 1];
        return exemptControllers.Any(c => c.EndsWith("." + folder + "Controller", StringComparison.Ordinal));
    }

    private static HashSet<string> Exempt()
    {
        var table = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "plan", "sheets", "classification.json")))!;
        return ((JsonArray)table["entries"]!).OfType<JsonObject>()
            .Where(e => (string?)e["kind"] == "controller" && (string?)e["status"] is "host" or "static")
            .Select(e => (string)e["type"]!).ToHashSet(StringComparer.Ordinal);
    }

    private static Snapshot Load()
    {
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(BaselinePath))!;
        return new Snapshot(((JsonArray)root["views"]!).Select(v => (string)v!).ToList(), ((JsonObject)root["actions"]!).ToDictionary(a => a.Key, a => (int)a.Value!, StringComparer.Ordinal));
    }

    private static void Write(Snapshot snapshot)
    {
        var root = new JsonObject
        {
            ["note"] = "Baseline of PEMS.Tests/Definitions/DefinitionRatchetTests (SCRUM-611): every Razor view and the action count of every controller. Only lower it by converting to definitions; raise it with PEMS_WRITE_RATCHET=1 and a reason in the pull request.",
            ["views"] = new JsonArray(snapshot.Views.Select(v => (JsonNode?)v).ToArray()),
            ["actions"] = new JsonObject(snapshot.Actions.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => KeyValuePair.Create(a.Key, (JsonNode?)a.Value))),
        };
        File.WriteAllText(BaselinePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }

    [Fact]
    public void No_new_view_controller_or_action_is_added_where_a_definition_can_express_it()
    {
        var current = Current();
        if (Environment.GetEnvironmentVariable("PEMS_WRITE_RATCHET") == "1") Write(current);
        var problems = Compare(Load(), current, Exempt());
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + Environment.NewLine + "If code is really the right answer, run with PEMS_WRITE_RATCHET=1 and explain in the pull request.");
    }

    // ---- the gate itself (no files needed)

    private static readonly Snapshot Base = new(["PEMS.Web/Views/Task/Index.cshtml", "PEMS.Web/Views/Account/Login.cshtml"], new Dictionary<string, int> { ["PEMS.Web.Controllers.TaskController"] = 3, ["PEMS.Web.Controllers.AccountController"] = 4 });

    [Fact]
    public void A_new_view_is_reported_by_its_file_name()
    {
        var current = new Snapshot([.. Base.Views, "PEMS.Web/Views/Task/Details.cshtml"], Base.Actions);
        var problem = Assert.Single(Compare(Base, current, new HashSet<string>()));
        Assert.Contains("PEMS.Web/Views/Task/Details.cshtml", problem);
    }

    [Fact]
    public void A_new_action_or_controller_is_reported_by_its_type_with_the_numbers()
    {
        var actions = new Dictionary<string, int>(Base.Actions) { ["PEMS.Web.Controllers.TaskController"] = 5, ["PEMS.Web.Controllers.ReportController"] = 1 };
        var problems = Compare(Base, new Snapshot(Base.Views, actions), new HashSet<string>());
        Assert.Contains(problems, p => p.Contains("TaskController") && p.Contains("2 new action(s) (3 -> 5)"));
        Assert.Contains(problems, p => p.Contains("new controller PEMS.Web.Controllers.ReportController"));
    }

    [Fact]
    public void Fewer_views_and_actions_pass_because_converting_to_definitions_only_lowers_the_numbers()
    {
        var current = new Snapshot(Base.Views.Take(1).ToList(), new Dictionary<string, int> { ["PEMS.Web.Controllers.TaskController"] = 1, ["PEMS.Web.Controllers.AccountController"] = 4 });
        Assert.Empty(Compare(Base, current, new HashSet<string>()));
    }

    [Fact]
    public void A_host_or_static_controller_may_add_views_and_actions_without_changing_the_baseline()
    {
        var exempt = new HashSet<string> { "PEMS.Web.Controllers.AccountController" };
        var actions = new Dictionary<string, int>(Base.Actions) { ["PEMS.Web.Controllers.AccountController"] = 9 };
        var current = new Snapshot([.. Base.Views, "PEMS.Web/Views/Account/Recover.cshtml"], actions);
        Assert.Empty(Compare(Base, current, exempt));
        Assert.Single(Compare(Base, new Snapshot([.. Base.Views, "PEMS.Web/Views/Task/New.cshtml"], Base.Actions), exempt));      // the same file in a converted area is still refused
    }
}
