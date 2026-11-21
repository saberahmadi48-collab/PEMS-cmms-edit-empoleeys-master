using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Services;

public sealed class AuthorizationReportService(PEMSDbContext context, IAccessControlService access, IUserAuthorizationSummaryService summary, UserManager<ApplicationUser> users) : IAuthorizationReportService
{
    public async Task<List<EffectivePermissionRowDto>> EffectiveForRoleAsync(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return [];
        return await RowsAsync([roleName.Trim()], null, roleName.Trim());
    }

    public async Task<List<EffectivePermissionRowDto>> EffectiveForUserAsync(int userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user == null) return [];
        var roles = (await users.GetRolesAsync(user)).OrderBy(r => r).ToArray();
        return await RowsAsync(roles, userId, null);
    }

    private async Task<List<EffectivePermissionRowDto>> RowsAsync(string[] roles, int? userId, string? roleName)
    {
        var dtos = await summary.GetEffectivePermissionsForRolesAsync(roles);
        return dtos.Select(d => new EffectivePermissionRowDto { RoleName = roleName, UserId = userId, ResourceName = d.ResourceName, ActionName = d.ActionName, Allowed = d.Allowed, IsDestructive = d.IsDestructive, Detail = d.Detail }).ToList();
    }

    public async Task<List<RoleAssignmentReportRowDto>> AssignmentsOfUserAsync(int userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user == null) return [];
        var roleNames = (await users.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var rows = await context.RoleAssignments.Where(a => a.UserId == userId && a.IsActive)
            .Join(context.Roles, a => a.RoleId, r => r.Id, (a, r) => new RoleAssignmentReportRowDto { UserId = userId, RoleName = r.Name!, ScopeType = a.ScopeType, ScopeValue = a.ScopeValue, AssignmentType = a.AssignmentType, EffectiveFrom = a.EffectiveFrom, EffectiveTo = a.EffectiveTo })
            .ToListAsync();
        // direct roles (AspNetUserRoles) are global assignments, as AccessControlService resolves them
        rows.AddRange(roleNames.Where(rn => rows.All(a => a.RoleName != rn)).Select(rn => new RoleAssignmentReportRowDto { UserId = userId, RoleName = rn, ScopeType = "Global", AssignmentType = "Direct (AspNetUserRoles)" }));
        return rows;
    }

    public async Task<List<AccessSimulationRowDto>> SimulateAsync(int userId, string resourceName, string actionName, string? resourceId = null)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || string.IsNullOrWhiteSpace(actionName)) return [];
        var decision = await access.AuthorizeAsync(new AccessControlRequest { UserId = userId, ResourceName = resourceName.Trim(), ActionName = actionName.Trim(), ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim() });
        return [new AccessSimulationRowDto { UserId = userId, ResourceName = resourceName.Trim(), ActionName = actionName.Trim(), Allowed = decision.Allowed, Reason = decision.Reason.ToString(), Detail = decision.Detail, MatchedRoleName = decision.MatchedRoleName, MatchedScopeType = decision.MatchedScopeType, MatchedScopeValue = decision.MatchedScopeValue }];
    }
}
