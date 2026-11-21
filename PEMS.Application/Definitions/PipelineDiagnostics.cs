using System.Globalization;

namespace PEMS.Application.Definitions;

// SCRUM-577 (#577): the page and form engine is measurable before any caching work (SCRUM-574 to 576). Each stage reports its time, the pipeline and the hydrators report counts (definitions read, hydrators run,
// option lists and options loaded), a budget per stage says when a request was too slow, and the host writes one line with the trace id so a slow page can be found from the error page or the request log.

/// <summary>Milliseconds a stage may take for one request; settings <c>Definitions:Budgets:*</c> in env/*.env. The defaults are generous guards against a regression, not a target.</summary>
public sealed class PipelineBudgets
{
    public int ResolveMs { get; init; } = 30;
    public int AuthorizeMs { get; init; } = 30;
    public int HydrateMs { get; init; } = 250;
    public int EvaluateMs { get; init; } = 100;
    public int RenderMs { get; init; } = 400;

    public int For(string stage) => stage switch
    {
        "resolve" => ResolveMs, "authorize" => AuthorizeMs, "hydrate" => HydrateMs, "evaluate" => EvaluateMs, "render" => RenderMs, _ => int.MaxValue,
    };

    public static readonly IReadOnlyList<string> Stages = ["resolve", "authorize", "hydrate", "evaluate", "render"];
}

public static class PipelineDiagnostics
{
    /// <summary>The stages that took longer than their budget, as "stage 312 ms (budget 250 ms)".</summary>
    public static IReadOnlyList<string> Exceeded(IReadOnlyDictionary<string, TimeSpan> timings, PipelineBudgets budgets)
        => PipelineBudgets.Stages.Where(timings.ContainsKey).Where(s => timings[s].TotalMilliseconds > budgets.For(s))
            .Select(s => $"{s} {timings[s].TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms (budget {budgets.For(s)} ms)").ToList();

    /// <summary>One log line: <c>definition page/p.key trace=… resolve=1.2ms authorize=0.4ms hydrate=5.0ms | definitions=1 hydrators=2</c>.</summary>
    public static string Line(string traceId, string kind, string key, IReadOnlyDictionary<string, TimeSpan> timings, IReadOnlyDictionary<string, int> counts)
    {
        var stages = string.Join(' ', PipelineBudgets.Stages.Where(timings.ContainsKey).Select(s => $"{s}={timings[s].TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}ms"));
        var numbers = string.Join(' ', counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}"));
        return $"definition {kind}/{key} trace={traceId} {stages} | {numbers}";
    }
}
