using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-628 (#628, guideline 04 section 7): every controller, hosted service and SignalR hub of the application is in the checked table <c>plan/sheets/classification.json</c> as
/// <c>pending</c> (still to convert, with the issue range), <c>host</c> or <c>static</c> (code by decision, with the reason). A type that is not in the table fails here, so nothing is forgotten
/// and nothing new is added silently; a <c>converted</c> entry is the receipt of a type that no longer exists. To add the types a change introduced, run the tests with
/// <c>PEMS_WRITE_CLASSIFICATION=1</c> and review the new lines (a new controller is <c>pending</c> by default: the ratchet gate SCRUM-611 fails it anyway when a definition can express it).
/// </summary>
public sealed class ClassificationClosureTests
{
    private static readonly string[] Statuses = ["pending", "host", "static", "converted"];

    // the generic hosts, auth and infrastructure controllers of tools/definition-audit/audit.py (HOSTS): code by decision
    private static readonly HashSet<string> HostControllers = new(StringComparer.Ordinal)
    {
        "FormHost", "PageHost", "DataHost", "Culture", "Theme", "NavigationBadge", "Files", "WebAuthn", "RecoveryCodes", "BreakGlass", "AccountRecovery", "Account", "ComingSoon", "SearchApi",
    };

    private static string TablePath => Path.Combine(SupportedReleases.RepoRoot(), "plan", "sheets", "classification.json");

    private sealed record Found(string Kind, string Type);

    private static List<Found> Discover()
    {
        var web = typeof(PEMS.Web.Definitions.RouteTable).Assembly;
        var found = new List<Found>();
        foreach (var t in web.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true } && !t.IsNested))
        {
            if (typeof(ControllerBase).IsAssignableFrom(t)) found.Add(new Found("controller", t.FullName!));
            else if (typeof(IHostedService).IsAssignableFrom(t)) found.Add(new Found("hostedService", t.FullName!));
            else if (typeof(Hub).IsAssignableFrom(t)) found.Add(new Found("hub", t.FullName!));
        }
        return found.OrderBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Type, StringComparer.Ordinal).ToList();
    }

    private static JsonObject Defaults(Found f)
    {
        var simple = f.Type[(f.Type.LastIndexOf('.') + 1)..];
        var name = f.Kind == "controller" && simple.EndsWith("Controller", StringComparison.Ordinal) ? simple[..^"Controller".Length] : simple;
        var (status, reason) = f.Kind switch
        {
            "hub" => ("host", "SignalR hub: real-time transport, code by decision"),
            "hostedService" => ("static", "background job: scheduled work is code by decision"),
            "controller" when HostControllers.Contains(name) => ("host", "generic host, sign-in or infrastructure controller: code by decision"),
            "controller" when name.EndsWith("Api", StringComparison.Ordinal) => ("static", "REST API (guideline 08): code by decision"),
            _ => ("pending", "converted to definitions by the conversion batches (SCRUM-629 to SCRUM-765)"),
        };
        return new JsonObject { ["kind"] = f.Kind, ["type"] = f.Type, ["status"] = status, ["reason"] = reason };
    }

    private static JsonObject Load() => (JsonObject)JsonNode.Parse(File.ReadAllText(TablePath))!;

    [Fact]
    public void Every_controller_hosted_service_and_hub_is_in_the_classification_table_and_every_entry_is_true()
    {
        var found = Discover();
        if (Environment.GetEnvironmentVariable("PEMS_WRITE_CLASSIFICATION") == "1") Write(found);

        var entries = ((JsonArray)Load()["entries"]!).OfType<JsonObject>().ToList();
        var problems = new List<string>();
        foreach (var e in entries)
        {
            var type = (string?)e["type"] ?? "";
            var status = (string?)e["status"] ?? "";
            if (!Statuses.Contains(status)) problems.Add($"{type}: status '{status}' is not one of {string.Join(", ", Statuses)}");
            if (string.IsNullOrWhiteSpace((string?)e["reason"])) problems.Add($"{type}: a reason is required (why it is {status})");
        }
        foreach (var dup in entries.GroupBy(e => (string?)e["type"]).Where(g => g.Count() > 1)) problems.Add($"{dup.Key}: listed {dup.Count()} times");

        var table = entries.ToDictionary(e => (string)e["type"]!, StringComparer.Ordinal);
        foreach (var f in found)
            if (!table.ContainsKey(f.Type)) problems.Add($"{f.Type} ({f.Kind}) is not in plan/sheets/classification.json: classify it (run with PEMS_WRITE_CLASSIFICATION=1 to add it as {Defaults(f)["status"]})");
        var existing = found.Select(f => f.Type).ToHashSet(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var type = (string)e["type"]!;
            var status = (string?)e["status"];
            if (status == "converted" && existing.Contains(type)) problems.Add($"{type} is marked converted but the type still exists: delete it or mark it pending");
            if (status != "converted" && !existing.Contains(type)) problems.Add($"{type} is in the table as {status} but no such type exists: mark it converted (receipt) or remove the line");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void Write(List<Found> found)
    {
        var old = File.Exists(TablePath) ? ((JsonArray)Load()["entries"]!).OfType<JsonObject>().ToList() : [];
        var byType = old.ToDictionary(e => (string)e["type"]!, StringComparer.Ordinal);
        var entries = new JsonArray();
        foreach (var f in found) entries.Add(byType.TryGetValue(f.Type, out var kept) ? JsonNode.Parse(kept.ToJsonString()) : Defaults(f));
        var existing = found.Select(f => f.Type).ToHashSet(StringComparer.Ordinal);
        foreach (var e in old.Where(e => !existing.Contains((string)e["type"]!)))
        {
            // a type that is gone is a receipt: converted (kept as the proof that it was handled)
            var receipt = (JsonObject)JsonNode.Parse(e.ToJsonString())!;
            if ((string?)receipt["status"] != "converted") { receipt["status"] = "converted"; receipt["reason"] = "the type was deleted: converted to definitions"; }
            entries.Add(receipt);
        }
        var root = new JsonObject
        {
            ["note"] = "Checked by PEMS.Tests/Definitions/ClassificationClosureTests (SCRUM-628). status: pending (to convert), host or static (code by decision, reason required), converted (receipt, the type is gone).",
            ["entries"] = entries,
        };
        File.WriteAllText(TablePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
