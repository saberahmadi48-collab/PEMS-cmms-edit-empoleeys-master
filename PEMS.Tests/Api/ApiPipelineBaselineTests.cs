using System.Diagnostics;
using System.Net;
using Xunit.Abstractions;

namespace PEMS.Tests.Api;

/// <summary>
/// In-process baseline of the API pipeline (SCRUM-436): routing, authentication, the permission policy, the filters, JSON in and out, over the in-memory
/// database. It is a guard against gross regressions (a sync-over-async call, an accidental scan), not a capacity figure: real database latency is measured
/// by tools/loadtest/pems-api-baseline.js. The budget is deliberately far above the usual few milliseconds so a slow CI machine does not fail it.
/// </summary>
public class ApiPipelineBaselineTests : IAsyncLifetime
{
    private const int Requests = 200;
    private const double BudgetP95Milliseconds = 250;

    private readonly ITestOutputHelper _output;
    private ApiConformanceHost _host = null!;

    public ApiPipelineBaselineTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _host = await ApiConformanceHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<(double P50, double P95, int Failed)> MeasureAsync(string path)
    {
        await _host.SendAsync("GET", path);                                          // warm-up: JIT, caches
        var times = new List<double>();
        var failed = 0;
        for (var i = 0; i < Requests; i++)
        {
            var sw = Stopwatch.StartNew();
            var res = await _host.SendAsync("GET", path);
            await res.Content.ReadAsStringAsync();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
            if (res.StatusCode != HttpStatusCode.OK) failed++;
        }
        times.Sort();
        return (times[Requests / 2], times[(int)(Requests * 0.95)], failed);
    }

    [Theory]
    [InlineData("/api/v1/disciplines?pageSize=25&sort=name")]
    [InlineData("/api/v1/disciplines/1")]
    [InlineData("/api/v1/definitions?pageSize=25&sort=key")]
    [InlineData("/api/v1/openapi.json")]
    public async Task Read_endpoints_stay_inside_the_in_process_budget_without_errors(string path)
    {
        for (var i = 0; i < 30; i++) _host.Disciplines.Seed("D" + i, "Discipline " + i);
        var (p50, p95, failed) = await MeasureAsync(path);
        _output.WriteLine($"{path}: p50 {p50:F2} ms, p95 {p95:F2} ms, failed {failed}/{Requests}");
        Assert.Equal(0, failed);
        Assert.True(p95 < BudgetP95Milliseconds, $"{path}: p95 {p95:F1} ms is over the {BudgetP95Milliseconds} ms in-process budget");
    }
}
