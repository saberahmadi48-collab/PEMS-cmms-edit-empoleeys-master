using PEMS.Application.Definitions;
using PEMS.Persistence.Seed;

namespace PEMS.Web.Definitions;

// SCRUM-797 (gap of SCRUM-515): the ownership preflight of a product update (which customised forms the shipped package would collide with) is shown to an administrator before the update runs: on the
// Database Update page (a stored page over the grid data source database-update-ownership-grid) and on /health/database. It only reads: nothing is written.

/// <summary>The preflight of the shipped package against the stored definitions, cached for a minute so a monitor that polls /health/database does not read the package every time.</summary>
public sealed class OwnershipPreflightReport(IServiceScopeFactory scopes)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<OwnershipProblem>? _cached;
    private DateTime _at;

    public async Task<IReadOnlyList<OwnershipProblem>> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null && DateTime.UtcNow - _at < Lifetime) return _cached;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTime.UtcNow - _at < Lifetime) return _cached;
            _cached = await CheckAsync(ct);
            _at = DateTime.UtcNow;
            return _cached;
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<OwnershipProblem>> CheckAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var kinds = scope.ServiceProvider.GetServices<IDefinitionKind>().ToList();
        var limits = scope.ServiceProvider.GetService<DefinitionLimits>() ?? new DefinitionLimits();
        var package = SystemDefinitionSeeder.LoadPackage(kinds, limits, Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName));
        return await scope.ServiceProvider.GetRequiredService<IDefinitionPreflight>().CheckOwnershipAsync(package, ct);
    }
}

/// <summary>
/// One row per reason the next product update cannot be applied (SCRUM-797): <c>form</c> (<c>app/key</c>), <c>field</c>, <c>code</c> (<c>name-taken</c>, <c>kind-change</c>) and <c>reason</c>. No rows means the update can be applied.
/// </summary>
public sealed class OwnershipPreflightGridProvider(OwnershipPreflightReport report) : IGridDataProvider
{
    public string Key => "ownership-preflight";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var problems = await report.GetAsync(ct);
        IReadOnlyList<object> items = problems.Select(p => (object)new Dictionary<string, object?>
            { ["form"] = $"{p.AppKey}/{p.Key}", ["field"] = p.Field, ["code"] = p.Code, ["reason"] = p.Message }).ToList();
        return (items, items.Count);
    }
}
