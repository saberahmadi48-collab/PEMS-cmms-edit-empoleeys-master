using System.Text.Json.Nodes;
using PEMS.Application.Expressions;

namespace PEMS.Tests.Expressions;

/// <summary>SCRUM-39 (ADR-0015): the server engine gives the answers of tools/js-tests/expression-cases.json, the file the browser engine is tested with, so one expression gives one result everywhere; hostile expressions are stopped.</summary>
public sealed class ExpressionEngineTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static JsonObject Fixture() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), "tools", "js-tests", "expression-cases.json")))!;

    private static ExpressionContext Context(JsonObject fixture)
    {
        var roots = ((JsonObject)fixture["context"]!).ToDictionary(p => p.Key, p => ExpressionValues.FromJson(p.Value), StringComparer.Ordinal);
        return new ExpressionContext(roots, DateTime.ParseExact((string)fixture["now"]!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static JsonNode? ToJson(object? v) => v switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        System.Collections.IEnumerable list => new JsonArray(list.Cast<object?>().Select(ToJson).ToArray()),
        _ => JsonValue.Create(v.ToString())
    };

    [Fact]
    public void Every_shared_case_gives_the_same_answer_as_in_the_browser()
    {
        var fixture = Fixture();
        var ctx = Context(fixture);
        var roots = ((JsonArray)fixture["roots"]!).Select(n => (string)n!).ToArray();
        var failures = new List<string>();
        foreach (var c in (JsonArray)fixture["cases"]!)
        {
            var expr = (string)c!["expr"]!;
            var expectedError = (string?)c["error"];
            try
            {
                var actual = ExpressionCompiler.Compile(expr, roots).Evaluate(ctx);
                if (expectedError is not null) failures.Add($"{expr}: expected the error {expectedError}, got {System.Text.Json.JsonSerializer.Serialize(ToJson(actual))}");
                else if (!JsonNode.DeepEquals(c["expect"], ToJson(actual))) failures.Add($"{expr}: expected {c["expect"]?.ToJsonString() ?? "null"}, got {ToJson(actual)?.ToJsonString() ?? "null"}");
            }
            catch (ExpressionException ex)
            {
                if (ex.Code != expectedError) failures.Add($"{expr}: {ex.Code} ({ex.Message}), expected {expectedError ?? "a value"}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_hostile_or_oversized_expression_is_stopped_at_compile_time()
    {
        Assert.Equal("limit-depth", Assert.Throws<ExpressionException>(() => ExpressionCompiler.Compile(new string('(', 40) + "1" + new string(')', 40))).Code);
        Assert.Equal("limit-nodes", Assert.Throws<ExpressionException>(() => ExpressionCompiler.Compile("1" + string.Concat(Enumerable.Repeat("+1", 400)))).Code);
        Assert.Equal("limit-length", Assert.Throws<ExpressionException>(() => ExpressionCompiler.Compile(new string('x', 2001))).Code);
    }

    [Fact]
    public void The_step_budget_stops_an_expression_that_is_too_expensive()
    {
        var limits = new ExpressionLimits { MaxSteps = 20 };
        var e = ExpressionCompiler.Compile("1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1+1", limits: limits);
        Assert.Equal("limit-steps", Assert.Throws<ExpressionException>(() => e.Evaluate(new ExpressionContext(new Dictionary<string, object?>(), DateTime.Today))).Code);
        // there is nothing that could run forever: no loop, no recursion, no user functions
        Assert.Equal("unknown-function", Assert.Throws<ExpressionException>(() => ExpressionCompiler.Compile("while(true)")).Code);
    }

    [Fact]
    public void A_condition_gives_true_false_or_nothing_and_nothing_else()
    {
        var ctx = new ExpressionContext(new Dictionary<string, object?> { ["values"] = new Dictionary<string, object?> { ["n"] = 5, ["x"] = null } }, DateTime.Today);
        Assert.True(ExpressionCompiler.Compile("values.n > 3").EvaluateCondition(ctx));
        Assert.False(ExpressionCompiler.Compile("values.x").EvaluateCondition(ctx));
        Assert.Equal("type", Assert.Throws<ExpressionException>(() => ExpressionCompiler.Compile("values.n").EvaluateCondition(ctx)).Code);
    }

    [Fact]
    public void The_variables_an_expression_reads_are_listed_for_the_references_of_a_definition()
    {
        var e = ExpressionCompiler.Compile("values.status == 'Draft' and 'Admin' in user.roles or request.culture == 'fa'");
        Assert.Equal(new[] { "request", "user", "values" }, e.Roots);
        Assert.False(ExpressionCompiler.TryCompile("values.x + ", out _, out var error));
        Assert.Equal("syntax", error!.Code);
    }

    [Fact]
    public void Plain_dotnet_values_are_converted_to_the_value_model()
    {
        var ctx = new ExpressionContext(new Dictionary<string, object?> { ["values"] = new Dictionary<string, object?> { ["qty"] = 3, ["price"] = 2.5m, ["on"] = new DateTime(2026, 3, 20), ["tags"] = new[] { "a", "b" } } }, DateTime.Today);
        Assert.Equal(7.5, ExpressionCompiler.Compile("values.qty * values.price").Evaluate(ctx));
        Assert.Equal(2026d, ExpressionCompiler.Compile("year(values.on)").Evaluate(ctx));
        Assert.Equal(true, ExpressionCompiler.Compile("'b' in values.tags").Evaluate(ctx));
    }

    [Fact]
    public void The_browser_engine_passes_the_same_cases()
    {
        // tools/js-tests/pems-expr.test.js; needs Node 18+ on the PATH (PEMS_REQUIRE_NODE=1 fails instead of skipping when it is missing)
        var script = Path.Combine(Root(), "tools", "js-tests", "pems-expr.test.js");
        System.Diagnostics.Process? process;
        try { process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "\"" + script + "\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.False(Environment.GetEnvironmentVariable("PEMS_REQUIRE_NODE") == "1", "node is required on this machine");
            return;
        }
        using (process!)
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "the script did not finish");
            Assert.True(process.ExitCode == 0, output);
        }
    }

    [Fact]
    public void A_page_instance_with_a_bad_visible_expression_is_refused_when_the_definition_is_checked()
    {
        var registry = new PEMS.UI.Rendering.PemsComponentRegistry();
        var kind = new PEMS.Web.Definitions.PageDefinitionKind(registry, new PEMS.UI.Manifest.ComponentManifestCatalog(registry));
        string Page(string visible) => "{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"a\",\"component\":\"divider\",\"visible\":" + System.Text.Json.JsonSerializer.Serialize(visible) + ",\"options\":{}}]}}";
        Assert.DoesNotContain(kind.Validate(Page("'Admin' in user.roles and request.culture == 'fa'"), new PEMS.Application.Definitions.DefinitionLimits()), e => e.Pointer.EndsWith("/visible"));
        Assert.Contains(kind.Validate(Page("values.x >"), new PEMS.Application.Definitions.DefinitionLimits()), e => e.Pointer.EndsWith("/visible") && e.Code == "syntax");
        Assert.Contains(kind.Validate(Page("secret.x == 1"), new PEMS.Application.Definitions.DefinitionLimits()), e => e.Pointer.EndsWith("/visible") && e.Code == "unknown-variable");
    }
}
