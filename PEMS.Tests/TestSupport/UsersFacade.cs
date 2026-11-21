using Microsoft.AspNetCore.Identity;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Web.Services;
using Xunit;

namespace PEMS.Tests.TestSupport;

/// <summary>SCRUM-650: the Users page service behind the old controller's call shape, so the create/edit rule tests keep reading as before (a form in, a result out).</summary>
public sealed class UserForm
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
    public List<string> SelectedRoles { get; set; } = new();
    public int? ExistingEmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? DisciplineId { get; set; }
    public int? PositionId { get; set; }
}

public sealed class UsersFacade(UserAdminPageService service, int actorId, string? actorName)
{
    public static UsersFacade For(UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, PEMSDbContext ctx,
        IRecoveryEventService? events = null, IAdminAccountService? admins = null, int actorId = 0, string? actorName = null)
    {
        var recovery = events ?? new RecoveryEventService(ctx, users);
        var service = new UserAdminPageService(users, roles, ctx, null!, new UserRoleAssignmentSynchronizer(ctx),
            new OrganizationHierarchyService(ctx, new OrganizationValidationService(ctx)), recovery, admins ?? new AdminAccountService(ctx, users, recovery));
        return new UsersFacade(service, actorId, actorName);
    }

    private static UserSaveInput Input(UserForm f) => new(f.Id, f.UserName, f.Email, f.FullName, f.Password, f.IsActive,
        string.Join(",", f.SelectedRoles.Select(r => r + "|Member")), f.ExistingEmployeeId, f.DepartmentId, f.DisciplineId, f.PositionId);

    public Task<UserSaveResult> Create(UserForm f) => service.CreateAsync(Input(f) with { Id = 0 });
    public Task<UserSaveResult> Edit(UserForm f) => service.UpdateAsync(Input(f), actorId, actorName, null);
    public Task<UserSaveResult> ToggleActive(int id) => service.ToggleActiveAsync(id);
    public Task<UserResetResult> ResetAdmin(int id) => service.ResetAdminAsync(id, actorId, null);

    public static void Refused(UserSaveResult r) => Assert.False(r.Success);
    public static void Saved(UserSaveResult r) => Assert.True(r.Success, r.Error + " " + r.Detail);
}
