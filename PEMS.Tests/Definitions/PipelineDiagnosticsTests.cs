using System.Diagnostics;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-577: stage timings and counts, the budget check, the log line with the trace id, and a regression guard on a page with 1000 rows and a form with many fields (in memory, no SQL Server).</summary>
public sealed class PipelineDiagnosticsTests
{
    private sealed class Store(string payload) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult<PublishedDefinition?>(new PublishedDefinition(kind, key, appKey, 1, payload, "hash"));
    }

    private sealed class AllowAll : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Hydrates a page with <paramref name="rows"/> rows and reports them as a count, as an entity list would.</summary>
    private sealed class RowsHydrator(int rows) : IDefinitionHydrator
    {
        public int Order => 10;
        public Task HydrateAsync(ResolvedDefinition definition, PipelineRequest request, CancellationToken ct = default)
        {
            definition.Hydrated["rows"] = Enumerable.Range(0, rows).Select(i => new Dictionary<string, object?> { ["id"] = i, ["name"] = "Row " + i, ["status"] = i % 3 == 0 ? "Open" : "Closed" }).ToList();
            definition.Counts["rows"] = rows;
            return Task.CompletedTask;
        }
    }

    private static PipelineRequest Request(string kind = "page") => new(kind, "x.list", "core", "en", ["Admin"], "hamid");

    [Fact]
    public async Task The_pipeline_reports_a_time_for_every_stage_it_runs_and_counts_what_it_loaded()
    {
        var pipeline = new DefinitionPipeline(new Store("""{"layout":"list-page","regions":{}}"""), new AllowAll(), [new RowsHydrator(5)]);
        var result = await pipeline.RunAsync(Request());
        Assert.True(result.Ok);
        Assert.Equal(["authorize", "hydrate", "resolve"], result.Timings.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal((1, 1, 5), (result.Counts["definitions"], result.Counts["hydrators"], result.Counts["rows"]));
        Assert.True(result.Counts["payloadBytes"] > 0);
    }

    [Fact]
    public async Task A_missing_definition_still_reports_what_was_asked_and_how_long_it_took()
    {
        var pipeline = new DefinitionPipeline(new NoneStore(), new AllowAll(), []);
        var result = await pipeline.RunAsync(Request());
        Assert.Equal(PipelineStatus.NotFound, result.Status);
        Assert.Equal(0, result.Counts["definitions"]);
        Assert.Contains("resolve", result.Timings.Keys);
    }

    private sealed class NoneStore : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default) => Task.FromResult<PublishedDefinition?>(null);
    }

    [Fact]
    public void A_stage_over_its_budget_is_listed_and_the_others_are_not()
    {
        var budgets = new PipelineBudgets { HydrateMs = 100, RenderMs = 50 };
        var timings = new Dictionary<string, TimeSpan> { ["resolve"] = TimeSpan.FromMilliseconds(5), ["hydrate"] = TimeSpan.FromMilliseconds(300), ["render"] = TimeSpan.FromMilliseconds(40) };
        var over = PipelineDiagnostics.Exceeded(timings, budgets);
        Assert.Equal(["hydrate 300 ms (budget 100 ms)"], over);
        Assert.Empty(PipelineDiagnostics.Exceeded(new Dictionary<string, TimeSpan>(), budgets));
        Assert.Equal(int.MaxValue, budgets.For("unknown-stage"));
    }

    [Fact]
    public void The_log_line_carries_the_trace_id_the_stage_times_and_the_counts()
    {
        var line = PipelineDiagnostics.Line("0HN123", "page", "stock.list",
            new Dictionary<string, TimeSpan> { ["resolve"] = TimeSpan.FromMilliseconds(1.5), ["render"] = TimeSpan.FromMilliseconds(12) },
            new Dictionary<string, int> { ["rows"] = 1000, ["definitions"] = 1 });
        Assert.Equal("definition page/stock.list trace=0HN123 resolve=1.5ms render=12.0ms | definitions=1 rows=1000", line);   // stages in pipeline order, counts by name
    }

    [Fact]
    public void The_settings_defaults_match_the_budgets_in_every_env_file()
    {
        var defaults = new PipelineBudgets();
        foreach (var file in Directory.GetFiles(Path.Combine(PEMS.Tests.TestSupport.SupportedReleases.RepoRoot(), "env"), "*.env"))
        {
            var text = File.ReadAllText(file);
            Assert.Contains($"Definitions__Budgets__ResolveMs={defaults.ResolveMs}", text);
            Assert.Contains($"Definitions__Budgets__RenderMs={defaults.RenderMs}", text);
        }
    }

    // ---- regression guard: the stages stay far inside their budgets for a representative page and form (p95 of 20 runs, in memory)

    private static double P95(Func<Task> run, int times = 20)
    {
        run().GetAwaiter().GetResult();                                          // warm up
        var samples = new List<double>();
        for (var i = 0; i < times; i++)
        {
            var clock = Stopwatch.StartNew();
            run().GetAwaiter().GetResult();
            samples.Add(clock.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
    }

    [Fact]
    public void A_list_page_with_1000_rows_goes_through_the_pipeline_inside_the_hydrate_budget()
    {
        var pipeline = new DefinitionPipeline(new Store("""{"layout":"list-page","regions":{"main":[{"id":"grid","component":"pems-list"}]}}"""), new AllowAll(), [new RowsHydrator(1000)]);
        var budgets = new PipelineBudgets();
        var p95 = P95(async () =>
        {
            var r = await pipeline.RunAsync(Request());
            Assert.Equal(1000, r.Counts["rows"]);
            Assert.Empty(PipelineDiagnostics.Exceeded(r.Timings, budgets));
        });
        Assert.True(p95 < budgets.ResolveMs + budgets.AuthorizeMs + budgets.HydrateMs, $"p95 {p95:0.0} ms is over the pipeline budget");
    }

    [Fact]
    public void A_form_with_80_fields_is_evaluated_and_validated_inside_the_evaluate_budget()
    {
        var fields = Enumerable.Range(0, 80).Select(i => new FieldDescriptor { Name = "f" + i, Label = "Field " + i, Kind = i % 4 == 0 ? FieldKind.Number : FieldKind.Text, Required = i % 5 == 0, MaxLength = i % 4 == 0 ? null : 50, Min = i % 4 == 0 ? 0 : null }).ToList();
        var form = new FormDefinition { Id = "big", Title = "Big", SubmitLabel = "Save", Fields = fields };
        var posted = fields.ToDictionary(f => f.Name, f => (string?)(f.Kind == FieldKind.Number ? "12" : "text"), StringComparer.Ordinal);
        var ctx = new FormRequestContext("en", "big", ["Admin"], "hamid");
        var p95 = P95(() =>
        {
            var validation = FormEngine.Validate(FormEngine.Effective(form, FormEngine.Evaluate(form, posted, ctx), new Dictionary<string, IReadOnlyList<FieldOption>>()), posted, ctx);
            Assert.True(validation.IsValid);
            return Task.CompletedTask;
        });
        Assert.True(p95 < new PipelineBudgets().EvaluateMs, $"p95 {p95:0.0} ms is over the evaluate budget");
    }
}
