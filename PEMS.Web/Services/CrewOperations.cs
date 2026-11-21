using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-677: what CrewController showed and did, over the crew service and the work order section guard.</summary>
public sealed class CrewOperations(PEMSDbContext context, ICrewService crews, IWorkOrderSectionGuard guard) : ICrewOperations
{
    private static string S(int id) => id.ToString(CultureInfo.InvariantCulture);

    public async Task<List<CrewRow>> CrewsAsync()
    {
        var list = await context.Crews.AsNoTracking().Include(x => x.WorkOrder).Include(x => x.Members.Where(m => m.IsActive)).ThenInclude(m => m.Employee).Include(x => x.Members.Where(m => m.IsActive)).ThenInclude(m => m.Role)
            .Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        return list.Select(c => new CrewRow(c.CrewId, c.Code, c.Name, c.WorkOrder?.WorkOrderNumber ?? "", c.Members.Count(m => m.IsActive),
            string.Join(", ", c.Members.Where(m => m.IsActive).Select(m => (m.Employee?.FirstName + " " + m.Employee?.LastName).Trim() + " (" + (m.Role?.Name ?? "") + ")")))).ToList();
    }

    public async Task<List<CrewMemberRow>> MembersAsync()
    {
        var list = await context.CrewMembers.AsNoTracking().Include(x => x.Crew).Include(x => x.Employee).Include(x => x.Role).Where(x => x.IsActive && x.Crew != null && x.Crew.IsActive).OrderBy(x => x.Crew!.Name).ToListAsync();
        return list.Select(m => new CrewMemberRow(m.CrewMemberId, m.Crew!.Name, (m.Employee?.FirstName + " " + m.Employee?.LastName).Trim(), m.Role?.Name ?? "")).ToList();
    }

    public async Task<List<ReportChoiceRow>> EmployeeChoicesAsync() => (await context.Employees.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.EmployeeId, x.FirstName, x.LastName }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EmployeeId), x.FirstName + " " + x.LastName)).ToList();
    public async Task<List<ReportChoiceRow>> RoleChoicesAsync() => (await context.MaintenanceRoles.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.MaintenanceRoleId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaintenanceRoleId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> WorkOrderChoicesAsync() => (await context.WorkOrders.AsNoTracking().Where(x => x.IsActive && x.Status != "Closed").OrderByDescending(x => x.CreatedDate).Select(x => new { x.WorkOrderId, x.WorkOrderNumber }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.WorkOrderId), x.WorkOrderNumber)).ToList();

    // the work order section guard (phase and permission) for a crew tied to a work order; the denial text is the answer
    private async Task<string?> DeniedAsync(int? workOrderId, WoSectionAction action, string? recordRef, WoCaller caller)
    {
        if (workOrderId is null) return null;
        var result = await guard.AuthorizeAsync(workOrderId.Value, WoSection.Crew, action, caller, null, recordRef);
        return result.Allowed ? null : result.Message(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa");
    }

    private async Task RecordAsync(int? workOrderId, WoSectionAction action, string? recordRef, string? before, string? after, WoCaller caller)
    {
        if (workOrderId is null) return;
        try { await guard.RecordChangeAsync(workOrderId.Value, WoSection.Crew, action, caller, recordRef, null, before, after); }
        catch (Exception) { context.ChangeTracker.Clear(); /* the audit is best effort: the change is already saved */ }
    }

    public async Task<(bool Success, string? Error)> CreateCrewAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, string code, string name, int? workOrderId = null)
    {
        var caller = new WoCaller(userId, userName, employeeId, userRoles, ip);
        var denied = await DeniedAsync(workOrderId, WoSectionAction.Add, code, caller);
        if (denied is not null) return (false, denied);
        var (success, error, crewId) = await crews.CreateAsync(code, name, workOrderId);
        if (success) await RecordAsync(workOrderId, WoSectionAction.Add, crewId?.ToString(), null, $"Code={code}; Name={name}", caller);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> AddMemberAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, int crewId, int employeeIdToAdd, int roleId)
    {
        var caller = new WoCaller(userId, userName, employeeId, userRoles, ip);
        var workOrderId = await context.Crews.Where(x => x.CrewId == crewId).Select(x => x.WorkOrderId).FirstOrDefaultAsync();
        var denied = await DeniedAsync(workOrderId, WoSectionAction.Add, crewId.ToString(), caller);
        if (denied is not null) return (false, denied);
        try
        {
            await crews.AddOrReactivateMemberAsync(crewId, employeeIdToAdd, roleId);
            await RecordAsync(workOrderId, WoSectionAction.Add, crewId.ToString(), null, $"EmployeeId={employeeIdToAdd}; RoleId={roleId}", caller);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> RemoveMemberAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, int crewMemberId)
    {
        var caller = new WoCaller(userId, userName, employeeId, userRoles, ip);
        var workOrderId = await context.CrewMembers.Where(x => x.CrewMemberId == crewMemberId).Select(x => x.Crew!.WorkOrderId).FirstOrDefaultAsync();
        var denied = await DeniedAsync(workOrderId, WoSectionAction.Remove, crewMemberId.ToString(), caller);
        if (denied is not null) return (false, denied);
        try
        {
            await crews.RemoveMemberAsync(crewMemberId);
            await RecordAsync(workOrderId, WoSectionAction.Remove, crewMemberId.ToString(), $"CrewMemberId={crewMemberId}; Active=true", "Active=false", caller);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
