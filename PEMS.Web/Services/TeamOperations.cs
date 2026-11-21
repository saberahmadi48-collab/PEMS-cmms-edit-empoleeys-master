using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Services;

/// <summary>SCRUM-757: what TeamController read and did, as operations. The discipline scope is the old rule (SystemAdmin and EngineeringManager are cross-discipline; everybody else only in the discipline of their own employee, fail closed).</summary>
public sealed class TeamOperations(PEMSDbContext context, ITeamService teams, IOrganizationStructureService organization) : ITeamOperations
{
    private static readonly TeamResult Refused = new(false, "You may not do this.");

    private static bool Cross(List<string> roles) => roles.Contains(SystemRoles.SystemAdmin) || roles.Contains(SystemRoles.EngineeringManager);

    private async Task<int?> MyDisciplineAsync(string userName)
    {
        if (string.IsNullOrEmpty(userName)) return null;
        var employeeId = await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        return employeeId.HasValue ? await context.Employees.AsNoTracking().Where(e => e.EmployeeId == employeeId.Value).Select(e => (int?)e.DisciplineId).FirstOrDefaultAsync() : null;
    }

    private async Task<bool> TeamInScopeAsync(int teamId, int? mine)
        => mine.HasValue && await context.Teams.AsNoTracking().Where(x => x.TeamId == teamId).Select(x => (int?)x.MaintenanceGroup!.DisciplineId).FirstOrDefaultAsync() is { } d && d == mine.Value;

    private async Task<bool> EmployeeInScopeAsync(int employeeId, int? mine)
        => mine.HasValue && await context.Employees.AsNoTracking().Where(x => x.EmployeeId == employeeId).Select(x => (int?)x.DisciplineId).FirstOrDefaultAsync() is { } d && d == mine.Value;

    private async Task<bool> GroupInScopeAsync(int groupId, int? mine)
        => mine.HasValue && await context.MaintenanceGroups.AsNoTracking().Where(x => x.MaintenanceGroupId == groupId).Select(x => (int?)x.DisciplineId).FirstOrDefaultAsync() is { } d && d == mine.Value;

    private async Task<bool> PositionInScopeAsync(int positionId, int? mine)
    {
        if (!mine.HasValue) return false;
        var teamId = await context.Positions.AsNoTracking().Where(x => x.PositionId == positionId).Select(x => x.TeamId).FirstOrDefaultAsync();
        return teamId.HasValue && await TeamInScopeAsync(teamId.Value, mine);
    }

    private async Task<bool> CanSeeTeamAsync(List<string> roles, string userName, int teamId) => Cross(roles) || await TeamInScopeAsync(teamId, await MyDisciplineAsync(userName));

    private static async Task<TeamResult> RunAsync(Func<Task> work, string ok)
    {
        try { await work(); return new(true, ok); }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public async Task<List<TeamRow>> ListAsync()
        => (await context.Teams.AsNoTracking().Include(x => x.MaintenanceGroup).ThenInclude(g => g!.Discipline).Include(x => x.SupervisorEmployee)
                .Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new
                {
                    x.TeamId, x.Code, x.Name, Group = x.MaintenanceGroup != null ? x.MaintenanceGroup.Name : "", Discipline = x.MaintenanceGroup != null && x.MaintenanceGroup.Discipline != null ? x.MaintenanceGroup.Discipline.Name : "",
                    x.Shift, Supervisor = x.SupervisorEmployee != null ? x.SupervisorEmployee.FirstName + " " + x.SupervisorEmployee.LastName : "", Members = x.Members.Count(m => m.IsActive)
                }).ToListAsync())
            .Select(x => new TeamRow(x.TeamId, x.Code, x.Name, x.Group, x.Discipline, x.Shift ?? "", x.Supervisor, x.Members)).ToList();

    public async Task<List<TeamProfileRow>> ProfileAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!await CanSeeTeamAsync(userRoles, userName, teamId)) return [];
        var t = await organization.GetTeamDetailsAsync(teamId);
        return t is null ? [] : [new TeamProfileRow(t.Name, t.Code, t.NodeKind, t.DisciplineName ?? "", t.SupervisorName ?? "", t.MemberCount, t.PositionCount, t.VacantPositionCount)];
    }

    public async Task<List<TeamMemberRow>> MembersAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!await CanSeeTeamAsync(userRoles, userName, teamId)) return [];
        return (await context.TeamMembers.AsNoTracking().Include(x => x.Employee).Include(x => x.Role).Where(x => x.TeamId == teamId && x.IsActive).OrderBy(x => x.Employee!.FirstName).ToListAsync())
            .Select(x => new TeamMemberRow(x.TeamMemberId, x.EmployeeId, x.Employee!.FirstName + " " + x.Employee.LastName, x.Role?.Name ?? "", x.JoinedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList();
    }

    public async Task<List<TeamNodeRow>> SubNodesAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!await CanSeeTeamAsync(userRoles, userName, teamId)) return [];
        var t = await organization.GetTeamDetailsAsync(teamId);
        return t?.Children.Select(c => new TeamNodeRow(c.TeamId, c.Code, c.Name, c.NodeKind, c.SupervisorName ?? "", c.MemberCount, c.PositionCount, c.VacantPositionCount)).ToList() ?? [];
    }

    public async Task<List<TeamPositionRow>> PositionsAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!await CanSeeTeamAsync(userRoles, userName, teamId)) return [];
        var t = await organization.GetTeamDetailsAsync(teamId);
        return t?.Positions.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .Select(p => new TeamPositionRow(p.PositionId, p.Title, p.Description ?? "", p.SortOrder, p.CurrentAssignment?.EmployeeName ?? "", p.IsVacant ? "Vacant" : "Occupied",
                p.CurrentAssignment is null ? "" : p.CurrentAssignment.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList() ?? [];
    }

    public async Task<List<TeamAssignmentRow>> AssignmentHistoryAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!await CanSeeTeamAsync(userRoles, userName, teamId)) return [];
        var t = await organization.GetTeamDetailsAsync(teamId);
        return t?.Positions.SelectMany(p => p.History.Select(h => new { p.Title, h })).OrderByDescending(x => x.h.StartDate)
            .Select(x => new TeamAssignmentRow(x.Title, x.h.EmployeeName, x.h.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.h.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", x.h.Notes ?? "")).ToList() ?? [];
    }

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    public async Task<List<TeamChoiceRow>> GroupsAsync()
        => (await context.MaintenanceGroups.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.MaintenanceGroupId, x.Name, Discipline = x.Discipline!.Name }).ToListAsync())
            .Select(x => new TeamChoiceRow(Id(x.MaintenanceGroupId), $"{x.Name} ({x.Discipline})")).ToList();

    public async Task<List<TeamChoiceRow>> EmployeesAsync()
        => (await context.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Select(x => new { x.EmployeeId, Name = x.FirstName + " " + x.LastName }).ToListAsync())
            .Select(x => new TeamChoiceRow(Id(x.EmployeeId), x.Name)).ToList();

    public async Task<List<TeamChoiceRow>> MaintenanceRolesAsync()
        => (await context.MaintenanceRoles.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.MaintenanceRoleId, x.Name, Discipline = x.Discipline!.Name }).ToListAsync())
            .Select(x => new TeamChoiceRow(Id(x.MaintenanceRoleId), $"{x.Name} ({x.Discipline})")).ToList();

    private static IEnumerable<OrganizationTeamNodeDto> Flatten(IEnumerable<OrganizationGroupNodeDto> groups)
    {
        foreach (var g in groups)
        {
            foreach (var t in FlattenNodes(g.Teams)) yield return t;
            foreach (var t in Flatten(g.Children)) yield return t;
        }
    }

    private static IEnumerable<OrganizationTeamNodeDto> FlattenNodes(IEnumerable<OrganizationTeamNodeDto> nodes)
    {
        foreach (var t in nodes)
        {
            yield return t;
            foreach (var c in FlattenNodes(t.Children)) yield return c;
        }
    }

    public async Task<List<TeamChoiceRow>> TeamNodesAsync()
        => Flatten(await organization.GetOrganizationTreeAsync()).Where(t => t.IsActive).OrderBy(t => t.Name).Select(t => new TeamChoiceRow(Id(t.TeamId), $"{t.Name} ({t.NodeKind})")).ToList();

    public async Task<List<TeamChoiceRow>> VacantPositionsAsync()
        => Flatten(await organization.GetOrganizationTreeAsync()).Where(t => t.IsActive).SelectMany(t => t.Positions.Where(p => p.IsActive && p.IsVacant).Select(p => new TeamChoiceRow(Id(p.PositionId), $"{t.Name} — {p.Title}"))).OrderBy(x => x.Label).ToList();

    public async Task<TeamResult> CreateTeamAsync(List<string> userRoles, string userName, string code, string name, int maintenanceGroupId, string? shift = null, int? supervisorEmployeeId = null)
    {
        if (!Cross(userRoles) && !await GroupInScopeAsync(maintenanceGroupId, await MyDisciplineAsync(userName))) return Refused;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code)) return new(false, "کد و نام تیم الزامی هستند.");
        if (await context.Teams.AnyAsync(x => x.Name == name.Trim() || x.Code == code.Trim())) return new(false, "تیمی با همین کد یا نام از قبل وجود دارد.");
        await context.Teams.AddAsync(new Team { Code = code.Trim(), Name = name.Trim(), MaintenanceGroupId = maintenanceGroupId, Shift = shift, SupervisorEmployeeId = supervisorEmployeeId, IsActive = true, CreatedDate = DateTime.Now });
        await context.SaveChangesAsync();
        return new(true, "تیم ساخته شد.");
    }

    public async Task<TeamResult> AddMemberAsync(List<string> userRoles, string userName, int teamId, int employeeId, int roleId)
    {
        if (!Cross(userRoles)) { var mine = await MyDisciplineAsync(userName); if (!await TeamInScopeAsync(teamId, mine) || !await EmployeeInScopeAsync(employeeId, mine)) return Refused; }
        return await RunAsync(() => teams.AddOrReactivateMemberAsync(teamId, employeeId, roleId), "عضو اضافه شد.");
    }

    public async Task<TeamResult> RemoveMemberAsync(List<string> userRoles, string userName, int teamMemberId)
    {
        if (!Cross(userRoles))
        {
            var teamId = await context.TeamMembers.AsNoTracking().Where(x => x.TeamMemberId == teamMemberId).Select(x => (int?)x.TeamId).FirstOrDefaultAsync();
            if (!teamId.HasValue || !await TeamInScopeAsync(teamId.Value, await MyDisciplineAsync(userName))) return Refused;
        }
        return await RunAsync(() => teams.RemoveMemberAsync(teamMemberId), "عضو از تیم خارج شد.");
    }

    public async Task<TeamResult> MoveMemberAsync(List<string> userRoles, string userName, int employeeId, int fromTeamId, int toTeamId, int roleId)
    {
        if (!Cross(userRoles))
        {
            var mine = await MyDisciplineAsync(userName);
            if (!await TeamInScopeAsync(fromTeamId, mine) || !await TeamInScopeAsync(toTeamId, mine) || !await EmployeeInScopeAsync(employeeId, mine)) return Refused;
        }
        return await RunAsync(() => teams.MoveMemberAsync(employeeId, fromTeamId, toTeamId, roleId), "کارمند به تیم جدید منتقل شد.");
    }

    public async Task<TeamResult> CreateSubTeamAsync(List<string> userRoles, string userName, int maintenanceGroupId, string code, string name, int? parentTeamId = null, string? nodeKind = null, int? supervisorEmployeeId = null)
    {
        if (!Cross(userRoles) && !await GroupInScopeAsync(maintenanceGroupId, await MyDisciplineAsync(userName))) return Refused;
        return await RunAsync(() => organization.CreateTeamNodeAsync(code, name, maintenanceGroupId, parentTeamId, nodeKind ?? "Team", supervisorEmployeeId), "زیرمجموعه/شیفت ساخته شد.");
    }

    public async Task<TeamResult> MoveTeamNodeAsync(List<string> userRoles, string userName, int teamId, int? newParentTeamId = null, int? newMaintenanceGroupId = null)
    {
        if (!Cross(userRoles))
        {
            var mine = await MyDisciplineAsync(userName);
            if (!await TeamInScopeAsync(teamId, mine)) return Refused;
            if (newParentTeamId.HasValue && !await TeamInScopeAsync(newParentTeamId.Value, mine)) return Refused;
            if (newMaintenanceGroupId.HasValue && !await GroupInScopeAsync(newMaintenanceGroupId.Value, mine)) return Refused;
        }
        return await RunAsync(() => organization.MoveTeamNodeAsync(teamId, newParentTeamId, newMaintenanceGroupId), "تیم/زیرمجموعه جابه‌جا شد.");
    }

    public async Task<TeamResult> DeactivateTeamNodeAsync(List<string> userRoles, string userName, int teamId)
    {
        if (!Cross(userRoles) && !await TeamInScopeAsync(teamId, await MyDisciplineAsync(userName))) return Refused;
        return await RunAsync(() => organization.DeactivateTeamNodeAsync(teamId), "تیم/زیرمجموعه غیرفعال شد.");
    }

    public async Task<TeamResult> CreatePositionAsync(List<string> userRoles, string userName, string title, int sortOrder, int? teamId = null, string? description = null)
    {
        if (!Cross(userRoles) && (!teamId.HasValue || !await TeamInScopeAsync(teamId.Value, await MyDisciplineAsync(userName)))) return Refused;
        return await RunAsync(() => organization.CreatePositionAsync(teamId, null, title, description, sortOrder), "سمت سازمانی ساخته شد.");
    }

    public async Task<TeamResult> UpdatePositionAsync(List<string> userRoles, string userName, int positionId, string title, int sortOrder, string? description = null)
    {
        if (!Cross(userRoles) && !await PositionInScopeAsync(positionId, await MyDisciplineAsync(userName))) return Refused;
        return await RunAsync(() => organization.UpdatePositionAsync(positionId, title, description, sortOrder), "سمت به‌روزرسانی شد.");
    }

    public async Task<TeamResult> MovePositionAsync(List<string> userRoles, string userName, int positionId, int? newTeamId = null)
    {
        if (!Cross(userRoles))
        {
            var mine = await MyDisciplineAsync(userName);
            if (!await PositionInScopeAsync(positionId, mine) || (newTeamId.HasValue && !await TeamInScopeAsync(newTeamId.Value, mine))) return Refused;
        }
        return await RunAsync(() => organization.MovePositionAsync(positionId, newTeamId), "سمت جابه‌جا شد.");
    }

    public async Task<TeamResult> DeactivatePositionAsync(List<string> userRoles, string userName, int positionId, string? notes = null)
    {
        if (!Cross(userRoles) && !await PositionInScopeAsync(positionId, await MyDisciplineAsync(userName))) return Refused;
        return await RunAsync(() => organization.DeactivatePositionAsync(positionId, notes), "سمت غیرفعال شد.");
    }

    public async Task<TeamResult> AssignEmployeeAsync(List<string> userRoles, string userName, int positionId, int employeeId, string? notes = null)
    {
        if (!Cross(userRoles)) { var mine = await MyDisciplineAsync(userName); if (!await PositionInScopeAsync(positionId, mine) || !await EmployeeInScopeAsync(employeeId, mine)) return Refused; }
        return await RunAsync(() => organization.AssignEmployeeAsync(positionId, employeeId, notes), "کارمند به سمت تخصیص یافت.");
    }

    public async Task<TeamResult> RemoveAssignmentAsync(List<string> userRoles, string userName, int positionId, string? notes = null)
    {
        if (!Cross(userRoles) && !await PositionInScopeAsync(positionId, await MyDisciplineAsync(userName))) return Refused;
        return await RunAsync(() => organization.RemoveAssignmentAsync(positionId, notes), "سمت خالی (Vacant) شد.");
    }

    public async Task<TeamResult> MoveEmployeeAsync(List<string> userRoles, string userName, int fromPositionId, int toPositionId, string? notes = null)
    {
        if (!Cross(userRoles))
        {
            var mine = await MyDisciplineAsync(userName);
            var employeeId = await context.EmployeeAssignments.AsNoTracking().Where(x => x.PositionId == fromPositionId && x.IsActive).Select(x => (int?)x.EmployeeId).FirstOrDefaultAsync();
            if (!await PositionInScopeAsync(fromPositionId, mine) || !await PositionInScopeAsync(toPositionId, mine) || !employeeId.HasValue || !await EmployeeInScopeAsync(employeeId.Value, mine)) return Refused;
        }
        return await RunAsync(() => organization.MoveEmployeeAsync(fromPositionId, toPositionId, notes), "کارمند به سمت جدید منتقل شد.");
    }
}
