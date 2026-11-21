using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// The rows of the Part Catalogue a user may see, in one place for the request list and the reports (SCRUM-478).
/// Admin, Review and Approve see every active request. Everyone else sees their own requests and the ones they are,
/// or were, an approver on (an assigned step, a step of their approver pool, or a candidate of the workflow instance).
/// </summary>
public sealed class PartCatalogueRequestScope
{
    public const string Module = "PartCatalogue";

    private readonly PEMSDbContext _ctx;
    private readonly IRoleModuleActionService _actions;

    public PartCatalogueRequestScope(PEMSDbContext ctx, IRoleModuleActionService actions)
    {
        _ctx = ctx;
        _actions = actions;
    }

    public async Task<bool> CanSeeAllAsync(PcActor a) =>
        await _actions.HasActionAsync(a.Roles, Module, "Admin")
        || await _actions.HasActionAsync(a.Roles, Module, "Review")
        || await _actions.HasActionAsync(a.Roles, Module, "Approve");

    public async Task<HashSet<string>> PoolKeysAsync(int employeeId) =>
        (await _ctx.PartCatalogueApproverAssignments
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .Select(x => x.StepKey).ToListAsync()).ToHashSet();

    public IQueryable<PartCatalogueRequest> Query(bool seeAll, PcActor actor, HashSet<string> pool)
    {
        var q = _ctx.PartCatalogueRequests.AsNoTracking().Where(r => r.IsActive);
        if (seeAll) return q;
        return q.Where(r => r.RequesterEmployeeId == actor.EmployeeId
                            || (r.WorkflowInstanceId == null && r.Steps.Any(st => st.AssignedEmployeeId == actor.EmployeeId || pool.Contains(st.StepKey)))
                            || (r.WorkflowInstanceId != null && _ctx.WorkflowInstanceStageCandidates.Any(c => c.EmployeeId == actor.EmployeeId && c.InstanceStage!.WorkflowInstanceId == r.WorkflowInstanceId)));
    }

    public async Task<IQueryable<PartCatalogueRequest>> QueryAsync(PcActor actor) =>
        Query(await CanSeeAllAsync(actor), actor, await PoolKeysAsync(actor.EmployeeId));
}
