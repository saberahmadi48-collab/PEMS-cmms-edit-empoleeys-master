using System.Diagnostics;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

/// <summary>SCRUM-577: what the generic hosts do with the stage timings of a request: one information line with the trace id (guideline 08 "One trace id"), a warning when a stage went over its budget.</summary>
public static class HostDiagnostics
{
    public static void Report(ILogger log, string traceId, string kind, string key, IReadOnlyDictionary<string, TimeSpan> timings, IReadOnlyDictionary<string, int> counts, PipelineBudgets? budgets)
    {
        log.LogInformation("{Line}", PipelineDiagnostics.Line(traceId, kind, key, timings, counts));
        var over = PipelineDiagnostics.Exceeded(timings, budgets ?? new PipelineBudgets());
        if (over.Count > 0) log.LogWarning("Definition {Kind}/{Key} trace={TraceId} is over budget: {Stages}", kind, key, traceId, string.Join("; ", over));
    }

    /// <summary>Runs <paramref name="stage"/> and returns how long it took (for the stages the hosts own: evaluate and render).</summary>
    public static async Task<(T Result, TimeSpan Elapsed)> TimeAsync<T>(Func<Task<T>> stage)
    {
        var clock = Stopwatch.StartNew();
        var result = await stage();
        return (result, clock.Elapsed);
    }
}
