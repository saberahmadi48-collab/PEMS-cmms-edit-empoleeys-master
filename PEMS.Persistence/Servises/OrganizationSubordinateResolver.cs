using Microsoft.EntityFrameworkCore;
using PEMS.Application.Organization;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class OrganizationSubordinateResolver : IOrganizationSubordinateResolver
{
    private const int MaxDepth = 12;
    private readonly PEMSDbContext _ctx;

    public OrganizationSubordinateResolver(PEMSDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<SubordinateInfo>> GetSubordinatesAsync(int employeeId, bool includeIndirect = true, DateTime? asOf = null, CancellationToken ct = default)
        => (await GetRelationsAsync(employeeId, asOf, ct)).Subordinates.Where(s => includeIndirect || s.Depth == 1).ToList();

    public async Task<OrgRelations> GetRelationsAsync(int employeeId, DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = asOf ?? DateTime.Now;
        var positions = await _ctx.Positions.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.PositionId, p.Title, p.ParentPositionId }).ToListAsync(ct);
        var byId = positions.ToDictionary(p => p.PositionId);

        var assignments = (await _ctx.EmployeeAssignments.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct))
            .Where(a => OrganizationValidationService.IsEffective(a, now)).ToList();
        var activeEmployees = await _ctx.Employees.AsNoTracking().Where(e => e.IsActive)
            .Select(e => new { e.EmployeeId, e.FirstName, e.LastName }).ToDictionaryAsync(e => e.EmployeeId, ct);

        // سمت اصلیِ معتبر؛ بدون آن هیچ رابطه‌ای حدس زده نمی‌شود
        var primaries = assignments.Where(a => a.EmployeeId == employeeId && a.IsPrimary).ToList();
        if (primaries.Count != 1 || !byId.TryGetValue(primaries[0].PositionId, out var mine)) return OrgRelations.Empty;

        List<int> HoldersOf(int positionId) => assignments.Where(a => a.PositionId == positionId && activeEmployees.ContainsKey(a.EmployeeId))
            .Select(a => a.EmployeeId).Distinct().ToList();
        string NameOf(int id) => activeEmployees.TryGetValue(id, out var e) ? (e.FirstName + " " + e.LastName).Trim() : "#" + id;

        // مافوق‌ها: از سمت اصلی به بالا (با محافظ دور)
        var managers = new List<int>();
        var seen = new HashSet<int> { mine.PositionId };
        var cur = mine;
        for (var i = 0; i < MaxDepth && cur.ParentPositionId.HasValue; i++)
        {
            if (!seen.Add(cur.ParentPositionId.Value) || !byId.TryGetValue(cur.ParentPositionId.Value, out var par)) break;
            foreach (var h in HoldersOf(par.PositionId)) if (h != employeeId && !managers.Contains(h)) managers.Add(h);
            cur = par;
        }

        // زیردستان: BFS روی سمت‌های فرزند
        var children = positions.Where(p => p.ParentPositionId.HasValue).GroupBy(p => p.ParentPositionId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.PositionId).ToList());
        var subs = new List<SubordinateInfo>();
        var visited = new HashSet<int> { mine.PositionId };
        var frontier = new List<int> { mine.PositionId };
        for (var depth = 1; depth <= MaxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var pid in frontier)
                foreach (var child in children.GetValueOrDefault(pid) ?? new List<int>())
                {
                    if (!visited.Add(child)) continue;
                    next.Add(child);
                    foreach (var h in HoldersOf(child))
                        if (h != employeeId && subs.All(s => s.EmployeeId != h)) subs.Add(new SubordinateInfo(h, NameOf(h), byId[child].Title, depth));
                }
            frontier = next;
        }

        // هم‌ترازها: دیگر فرزندان همان سمت مافوق (و سایر دارندگان همین سمت)
        var peers = new List<int>();
        if (mine.ParentPositionId.HasValue)
            foreach (var sibling in children.GetValueOrDefault(mine.ParentPositionId.Value) ?? new List<int>())
                foreach (var h in HoldersOf(sibling)) if (h != employeeId && !peers.Contains(h)) peers.Add(h);
        foreach (var h in HoldersOf(mine.PositionId)) if (h != employeeId && !peers.Contains(h)) peers.Add(h);

        return new OrgRelations(managers, subs.OrderBy(s => s.Depth).ThenBy(s => s.Name).ToList(), peers);
    }
}
