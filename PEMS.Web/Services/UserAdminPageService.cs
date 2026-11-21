using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Web.Helpers;

namespace PEMS.Web.Services;

/// <summary>SCRUM-650: the Admin Users pages as operations (the code of the former UsersController, without any view or request type). Every refusal is a resource key; nothing here shows a page.</summary>
public sealed class UserAdminPageService(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    PEMSDbContext db,
    IUserAuthorizationSummaryService summary,
    IUserRoleAssignmentSynchronizer roleSync,
    IOrganizationHierarchyService hierarchy,
    IRecoveryEventService recoveryEvents,
    IAdminAccountService adminAccounts) : IUserAdminPageService
{
    private const int MaxExportRows = 20000;
    private const int RecentAuditLogCount = 20;
    private const string LastAdminKey = "This is the last active administrator. It cannot be deactivated or removed from the SystemAdmin role. Create or activate another administrator first.";
    private const string CouldNotSaveKey = "The user could not be saved: {0}";

    private static UserSaveResult Refuse(string key, string? detail = null) => new(false, key, detail);

    // ---- reads

    private async Task<Dictionary<int, List<string>>> RolesByUserAsync(IEnumerable<int>? ids = null)
    {
        var query = db.UserRoles.AsQueryable();
        if (ids != null) { var list = ids.ToList(); query = query.Where(ur => list.Contains(ur.UserId)); }
        return (await query.Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name }).ToListAsync())
            .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Name!).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    public async Task<List<UserRowDto>> RowsAsync()
    {
        var list = await users.Users.OrderBy(u => u.UserName).ThenBy(u => u.Id).ToListAsync();
        var byUser = await RolesByUserAsync();
        return list.Select(u =>
        {
            var names = byUser.GetValueOrDefault(u.Id) ?? [];
            return new UserRowDto
            {
                Id = u.Id, UserName = u.UserName ?? string.Empty, FullName = u.FullName, Email = u.Email ?? string.Empty, Roles = string.Join(", ", names),
                IsActive = u.IsActive, LockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
                IsAdmin = names.Contains(SystemRoles.SystemAdmin, StringComparer.OrdinalIgnoreCase), LastLoginDate = u.LastLoginDate
            };
        }).ToList();
    }

    public async Task<List<UserFormRowDto>> UserOfAsync(int id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user == null) return [];
        var names = await users.GetRolesAsync(user);
        return [new UserFormRowDto
        {
            Id = user.Id.ToString(), UserName = user.UserName ?? string.Empty, Email = user.Email ?? string.Empty, FullName = user.FullName, IsActive = user.IsActive,
            Roles = string.Join(",", names.Select(n => n + "|Member")), ExistingEmployeeId = user.EmployeeId?.ToString()
        }];
    }

    public async Task<List<UserSummaryRowDto>> SummaryAsync(int id)
    {
        var user = await users.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return [];
        var names = (await users.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var employee = user.EmployeeId.HasValue
            ? await db.Employees.Where(e => e.EmployeeId == user.EmployeeId.Value).Select(e => new { e.Position, e.DepartmentId, e.DisciplineId }).FirstOrDefaultAsync()
            : null;
        string department = string.Empty, discipline = string.Empty;
        if (employee != null)
        {
            if (employee.DepartmentId != 0) department = await db.Departments.Where(d => d.DepartmentId == employee.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync() ?? string.Empty;
            if (employee.DisciplineId != 0) discipline = await db.Disciplines.Where(d => d.DisciplineId == employee.DisciplineId).Select(d => d.Name).FirstOrDefaultAsync() ?? string.Empty;
        }
        return [new UserSummaryRowDto
        {
            Id = user.Id, UserName = user.UserName ?? string.Empty, FullName = user.FullName, Email = user.Email ?? string.Empty, IsActive = user.IsActive,
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow, LastLoginDate = user.LastLoginDate, Roles = string.Join(", ", names),
            Department = department, Discipline = discipline, Position = employee?.Position ?? string.Empty, HasLinkedEmployee = user.EmployeeId.HasValue
        }];
    }

    public async Task<List<UserOptionRowDto>> RoleOptionsAsync() =>
        (await roles.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync()).Select(n => new UserOptionRowDto { Value = n, Label = n }).ToList();

    public async Task<List<UserSodRowDto>> SodRowsAsync(int id)
    {
        var user = await users.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return [];
        var names = (await users.GetRolesAsync(user)).OrderBy(r => r).ToList();
        return (await summary.GetSoDExposureForRolesAsync(names)).Select(s => new UserSodRowDto
        { PermissionA = s.PermissionAName, PermissionB = s.PermissionBName, Policy = s.PolicyDescription ?? string.Empty, UserHoldsBoth = s.UserHoldsBoth }).ToList();
    }

    public async Task<List<UserAuditRowDto>> AuditRowsAsync(int userId) =>
        await db.AuthorizationAuditLogs.Where(a => a.UserId == userId).OrderByDescending(a => a.Timestamp).Take(RecentAuditLogCount)
            .Select(a => new UserAuditRowDto { Timestamp = a.Timestamp, ResourceName = a.ResourceName, ActionName = a.ActionName, ResourceId = a.ResourceId, Decision = a.Decision, Reason = a.Reason, UserId = userId })
            .ToListAsync();

    // ---- validation shared by create and edit (errors are resource keys, nothing is written)

    private static List<string> RolesOf(string? cells) =>
        (cells ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.Split('|')[0]).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private async Task<UserSaveResult?> ValidateEmployeeLinkAsync(UserSaveInput m, int? currentUserId)
    {
        if (m.ExistingEmployeeId.HasValue)
        {
            var emp = await db.Employees.AsNoTracking().Where(e => e.EmployeeId == m.ExistingEmployeeId.Value).Select(e => new { e.EmployeeId, e.IsActive }).FirstOrDefaultAsync();
            if (emp == null) return Refuse("The selected employee does not exist.");
            if (!emp.IsActive) return Refuse("The selected employee is inactive.");
            if (await db.Users.AnyAsync(u => u.EmployeeId == emp.EmployeeId && (currentUserId == null || u.Id != currentUserId))) return Refuse("This employee is already linked to another user account.");
            return null;
        }
        if (!m.DepartmentId.HasValue) return null;
        if (!await db.Departments.AnyAsync(d => d.DepartmentId == m.DepartmentId.Value && d.IsActive)) return Refuse("Select a valid, active department.");
        if (!m.DisciplineId.HasValue || !await db.Disciplines.AnyAsync(d => d.DisciplineId == m.DisciplineId.Value && d.IsActive)) return Refuse("A valid, active discipline is required to create an employee.");
        if (m.PositionId.HasValue)
        {
            var pos = await db.Positions.AsNoTracking().Where(p => p.PositionId == m.PositionId.Value).Select(p => new { p.IsActive }).FirstOrDefaultAsync();
            if (pos == null || !pos.IsActive) return Refuse("Select a valid, active position.");
            if (await db.EmployeeAssignments.AnyAsync(a => a.PositionId == m.PositionId.Value && a.IsActive)) return Refuse("This position already has a holder.");
        }
        return null;
    }

    /// <summary>The Identity validators (user and password) run before any side effect, so no orphan employee is created and a resubmit does not repeat one.</summary>
    private async Task<UserSaveResult?> ValidateIdentityUpFrontAsync(UserSaveInput m, List<string> wantedRoles)
    {
        var probe = new ApplicationUser { UserName = m.UserName, Email = m.Email, FullName = m.FullName };
        foreach (var validator in users.UserValidators)
        {
            var r = await validator.ValidateAsync(users, probe);
            if (!r.Succeeded) return Refuse(CouldNotSaveKey, string.Join(" ", r.Errors.Select(e => e.Description)));
        }
        if (!string.IsNullOrWhiteSpace(m.Password))
            foreach (var validator in users.PasswordValidators)
            {
                var r = await validator.ValidateAsync(users, probe, m.Password);
                if (!r.Succeeded) return Refuse(CouldNotSaveKey, string.Join(" ", r.Errors.Select(e => e.Description)));
            }
        var known = await roles.Roles.Select(r => r.Name!).ToListAsync();
        var unknown = wantedRoles.Where(r => !known.Contains(r)).ToList();
        return unknown.Count > 0 ? Refuse("Unknown role(s): {0}", string.Join(", ", unknown)) : null;
    }

    private async Task<int> CreateEmployeeAsync(UserSaveInput m)
    {
        var parts = m.FullName.Trim().Split(' ', 2);
        string? title = m.PositionId.HasValue ? await db.Positions.Where(p => p.PositionId == m.PositionId.Value).Select(p => p.Title).FirstOrDefaultAsync() : null;
        var employee = new PEMS.Domain.Entities.Employee
        {
            FirstName = parts.Length > 0 ? parts[0] : m.FullName, LastName = parts.Length > 1 ? parts[1] : string.Empty, Position = title, Email = m.Email,
            DepartmentId = m.DepartmentId!.Value, DisciplineId = m.DisciplineId!.Value, IsActive = true, CreatedDate = DateTime.Now
        };
        await db.Employees.AddAsync(employee);
        await db.SaveChangesAsync();
        if (m.PositionId.HasValue)
            await hierarchy.AssignAsync(new OrganizationAssignmentRequest(m.PositionId.Value, employee.EmployeeId, true, DateTime.Now, null, "Created from Administration/Users"));
        return employee.EmployeeId;
    }

    private async Task RollbackEmployeeAsync(int? employeeId)
    {
        if (!employeeId.HasValue) return;
        try
        {
            db.ChangeTracker.Clear();
            db.EmployeeAssignments.RemoveRange(await db.EmployeeAssignments.Where(a => a.EmployeeId == employeeId.Value).ToListAsync());
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value);
            if (employee != null) db.Employees.Remove(employee);
            await db.SaveChangesAsync();
        }
        catch (Exception) { db.ChangeTracker.Clear(); }
    }

    // ---- writes

    public async Task<UserSaveResult> CreateAsync(UserSaveInput input)
    {
        var wanted = RolesOf(input.Roles);
        if (string.IsNullOrWhiteSpace(input.Password)) return Refuse("Password is required for a new user.");
        if (!input.ExistingEmployeeId.HasValue && !input.DepartmentId.HasValue) return Refuse("Select an existing employee or specify a department to create one.");
        var refusal = await ValidateEmployeeLinkAsync(input, null) ?? await ValidateIdentityUpFrontAsync(input, wanted);
        if (refusal != null) return refusal;

        int? employeeId = input.ExistingEmployeeId, createdEmployeeId = null;
        if (!employeeId.HasValue && input.DepartmentId.HasValue) employeeId = createdEmployeeId = await CreateEmployeeAsync(input);

        var user = new ApplicationUser
        {
            UserName = input.UserName, Email = input.Email, EmailConfirmed = true, FullName = input.FullName, IsActive = input.IsActive, EmployeeId = employeeId, CreatedDate = DateTime.Now
        };
        IdentityResult result;
        try { result = await users.CreateAsync(user, input.Password!); }
        catch (Exception ex) { result = IdentityResult.Failed(new IdentityError { Description = ex.GetBaseException().Message }); }
        if (!result.Succeeded)
        {
            await RollbackEmployeeAsync(createdEmployeeId);
            return Refuse(CouldNotSaveKey, string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        if (wanted.Count > 0)
        {
            var roleResult = await users.AddToRolesAsync(user, wanted);
            if (!roleResult.Succeeded)
            {
                await users.DeleteAsync(user);          // never leave the account without the chosen roles
                await RollbackEmployeeAsync(createdEmployeeId);
                return Refuse(CouldNotSaveKey, string.Join(" ", roleResult.Errors.Select(e => e.Description)));
            }
        }
        await roleSync.SyncAsync(user.Id);
        return new UserSaveResult(true, null);
    }

    public async Task<UserSaveResult> UpdateAsync(UserSaveInput input, int actorId, string? actorName, string? ip)
    {
        var wanted = RolesOf(input.Roles);
        var refusal = await ValidateEmployeeLinkAsync(input, input.Id);
        if (refusal != null) return refusal;
        var user = await users.FindByIdAsync(input.Id.ToString());
        if (user == null) return Refuse("The account was not found.");

        // SCRUM-354: the last active administrator is neither deactivated nor taken out of SystemAdmin (the server decides, not the screen)
        if (await adminAccounts.WouldLeaveNoActiveAdminAsync(user.Id, input.IsActive, wanted.Contains(SystemRoles.SystemAdmin, StringComparer.OrdinalIgnoreCase))) return Refuse(LastAdminKey);

        user.Email = input.Email;
        user.FullName = input.FullName;
        user.IsActive = input.IsActive;
        if (input.ExistingEmployeeId.HasValue) user.EmployeeId = input.ExistingEmployeeId.Value;
        else if (input.DepartmentId.HasValue) user.EmployeeId = await CreateEmployeeAsync(input);
        await users.UpdateAsync(user);

        string? passwordError = null;
        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            // SCRUM-357: the reset of an administrator's password has the audit, the session revocation and the notice of the recovery flow
            var targetIsAdmin = await users.IsInRoleAsync(user, SystemRoles.SystemAdmin);
            var attempt = new RecoveryAttempt(RecoveryMethods.AdminConsole, user.Id, user.UserName, actorId > 0 ? actorId : null, actorName, ip);
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, input.Password);
            if (!reset.Succeeded)
            {
                passwordError = string.Join(", ", reset.Errors.Select(e => e.Description));
                if (targetIsAdmin) await recoveryEvents.RecordFailureAsync(attempt, RecoveryFailureReasons.PasswordRejected);
            }
            else if (targetIsAdmin)
            {
                try { await recoveryEvents.CompleteSuccessAsync(attempt); }
                catch (InvalidOperationException) { passwordError = "The password was changed but recording or session revocation failed."; }
            }
        }

        var current = await users.GetRolesAsync(user);
        var toRemove = current.Except(wanted).ToList();
        var toAdd = wanted.Except(current).ToList();
        if (toRemove.Count > 0) await users.RemoveFromRolesAsync(user, toRemove);
        IdentityResult? roleResult = toAdd.Count > 0 ? await users.AddToRolesAsync(user, toAdd) : null;
        await roleSync.SyncAsync(user.Id);          // a removed role must not keep giving access through its mirror

        if (roleResult is { Succeeded: false }) return Refuse("User updated, but some role assignments failed: {0}", string.Join(", ", roleResult.Errors.Select(e => e.Description)) + (passwordError == null ? string.Empty : " / " + passwordError));
        if (passwordError != null) return Refuse("User updated, but the password was not changed: {0}", passwordError);
        return new UserSaveResult(true, null);
    }

    public async Task<UserSaveResult> ToggleActiveAsync(int id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user == null) return Refuse("The account was not found.");
        if (user.IsActive && await adminAccounts.WouldLeaveNoActiveAdminAsync(user.Id, willBeActive: false, willBeAdmin: await users.IsInRoleAsync(user, SystemRoles.SystemAdmin))) return Refuse(LastAdminKey);
        user.IsActive = !user.IsActive;
        await users.UpdateAsync(user);
        return new UserSaveResult(true, null);
    }

    public async Task<UserResetResult> ResetAdminAsync(int id, int actorId, string? ip)
    {
        var result = await adminAccounts.ResetAdminAsync(actorId, id, ip);
        if (!result.Succeeded || string.IsNullOrEmpty(result.TemporaryPassword))
            return new UserResetResult(false, result.Status switch
            {
                AdminResetStatus.SelfResetNotAllowed => "You cannot reset your own account this way. Ask another administrator.",
                AdminResetStatus.TargetNotFound => "The account was not found.",
                AdminResetStatus.TargetNotAdmin => "Only another administrator's account can be reset this way.",
                AdminResetStatus.ActorNotAdmin => "Only an active administrator can reset an administrator account.",
                AdminResetStatus.AuditFailed => "The password was changed but recording or session revocation failed. No temporary password is shown; run the reset again.",
                _ => "The reset failed. Nothing was shown; try again."
            });
        return new UserResetResult(true, null, result.TemporaryPassword, "Administrator Account Reset", "Temporary password",
            "Copy or print this password now. It cannot be shown again; if it is lost, reset the account again.", "/p/users", "Back to users");
    }

    public async Task<UserExportResult> ExportAsync(bool onlyActive)
    {
        var query = onlyActive ? users.Users.Where(u => u.IsActive) : users.Users;
        var total = await query.CountAsync();
        if (total > MaxExportRows) return new UserExportResult(false, "There are more users than the safe export limit allows.", null, null, null);
        var list = await query.ToListAsync();
        var byUser = await RolesByUserAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Users");
        sheet.AddStyledHeaderRow("UserName", "FullName", "Email", "Roles", "IsActive", "LastLoginDate");
        var row = 2;
        foreach (var u in list)
        {
            sheet.Cell(row, 1).Value = u.UserName;
            sheet.Cell(row, 2).Value = u.FullName;
            sheet.Cell(row, 3).Value = u.Email;
            sheet.Cell(row, 4).Value = string.Join(", ", byUser.GetValueOrDefault(u.Id) ?? []);
            sheet.Cell(row, 5).Value = u.IsActive;
            if (u.LastLoginDate.HasValue) sheet.Cell(row, 6).Value = u.LastLoginDate.Value;
            row++;
        }
        sheet.FinalizeExportSheet();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new UserExportResult(true, null, stream.ToArray(), $"Users_{DateTime.Now:yyyyMMdd}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
