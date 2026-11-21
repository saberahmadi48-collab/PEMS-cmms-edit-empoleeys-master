using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Services;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS — P1 Object-Level Authorization &amp; Data Scope Hardening»: داشتن Team.Edit به‌تنهایی
/// نباید یعنی «هر Team ای قابل‌ویرایش است». این تست‌ها ثابت می‌کنند که Discipline-Scope واقعاً
/// اعمال می‌شود - نه فقط Module Permission - و اینکه SystemAdmin/EngineeringManager (که در تمام
/// PEMS اختیار چند-دیسیپلینه دارند) از این محدودیت معاف‌اند.
/// </summary>
public class TeamObjectScopeAuthorizationTests
{
    private const int MechDisciplineId = 1;
    private const int ElecDisciplineId = 2;

    private class Fixture
    {
        public PEMSDbContext Ctx = null!;
        public IPermissionService Permissions = null!;
        public int MechTeamId;
        public int ElecTeamId;
        public int MechEmployeeId;
        public int ElecEmployeeId;
        public int MechRoleId;
    }

    private static async Task<Fixture> BuildAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TeamScope_{dbName}");

        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });

        ctx.Disciplines.Add(new Discipline { DisciplineId = MechDisciplineId, Code = "MECH", Name = "Mechanical" });
        ctx.Disciplines.Add(new Discipline { DisciplineId = ElecDisciplineId, Code = "ELEC", Name = "Electrical" });

        ctx.MaintenanceGroups.Add(new MaintenanceGroup { MaintenanceGroupId = 1, Code = "MG1", Name = "Mech Group", DisciplineId = MechDisciplineId, IsActive = true });
        ctx.MaintenanceGroups.Add(new MaintenanceGroup { MaintenanceGroupId = 2, Code = "MG2", Name = "Elec Group", DisciplineId = ElecDisciplineId, IsActive = true });

        ctx.Teams.Add(new Team { TeamId = 100, Code = "T-MECH", Name = "Mech Team", MaintenanceGroupId = 1, IsActive = true });
        ctx.Teams.Add(new Team { TeamId = 200, Code = "T-ELEC", Name = "Elec Team", MaintenanceGroupId = 2, IsActive = true });

        ctx.Employees.Add(new Employee { EmployeeId = 10, FirstName = "Mech", LastName = "Tech", DepartmentId = 1, DisciplineId = MechDisciplineId, IsActive = true });
        ctx.Employees.Add(new Employee { EmployeeId = 20, FirstName = "Elec", LastName = "Tech", DepartmentId = 1, DisciplineId = ElecDisciplineId, IsActive = true });

        ctx.MaintenanceRoles.Add(new MaintenanceRole { MaintenanceRoleId = 1, Name = "Technician", IsActive = true });

        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        return new Fixture
        {
            Ctx = ctx,
            Permissions = permissionService,
            MechTeamId = 100,
            ElecTeamId = 200,
            MechEmployeeId = 10,
            ElecEmployeeId = 20,
            MechRoleId = 1
        };
    }

    private static (TeamOperations Controller, List<string> Roles, string UserName) CreateController(Fixture f, string role, int? employeeId, int nextUserId)
    {
        var appUser = new ApplicationUser { Id = nextUserId, UserName = $"user{nextUserId}", EmployeeId = employeeId };
        f.Ctx.Users.Add(appUser);
        f.Ctx.SaveChanges();

        var currentUser = new FakeCurrentUserService { UserId = appUser.Id, Roles = new List<string> { role } };
        var controller = new TeamOperations(f.Ctx, new TeamService(f.Ctx), new OrganizationStructureService(f.Ctx));
        return (controller, new List<string> { role }, appUser.UserName!);
    }

    // ================= B. Permission + in scope = ALLOW =================

    [Fact]
    public async Task AddMember_MaintenanceEngineer_SameDisciplineTeamAndEmployee_Allowed()
    {
        var f = await BuildAsync(nameof(AddMember_MaintenanceEngineer_SameDisciplineTeamAndEmployee_Allowed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.AddMemberAsync(roles, userName, f.MechTeamId, f.MechEmployeeId, f.MechRoleId);

        Assert.True(result.Success);
        Assert.Single(f.Ctx.TeamMembers);
    }

    // ================= C. Permission + out of scope = DENY (the core object-scope test) =================

    [Fact]
    public async Task AddMember_MaintenanceEngineer_OutOfScopeTeam_Denied_NoMutation()
    {
        // کاربر (کارمند Discipline مکانیک) سعی می‌کند به تیمِ Discipline برق عضو اضافه کند
        var f = await BuildAsync(nameof(AddMember_MaintenanceEngineer_OutOfScopeTeam_Denied_NoMutation));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.AddMemberAsync(roles, userName, f.ElecTeamId, f.MechEmployeeId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.Empty(f.Ctx.TeamMembers);
    }

    [Fact]
    public async Task AddMember_MaintenanceEngineer_OutOfScopeEmployee_Denied_NoMutation()
    {
        // Team در دسترس است، ولی خودِ کارمند هدف از Discipline دیگری است
        var f = await BuildAsync(nameof(AddMember_MaintenanceEngineer_OutOfScopeEmployee_Denied_NoMutation));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.AddMemberAsync(roles, userName, f.MechTeamId, f.ElecEmployeeId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.Empty(f.Ctx.TeamMembers);
    }

    // ================= D. IDOR: manually supplied arbitrary IDs of real, out-of-scope objects =================

    [Fact]
    public async Task AddMember_IDOR_ArbitraryOutOfScopeTeamId_Denied()
    {
        // شبیه‌سازی حمله‌ی IDOR دقیقاً طبق بند ۱۰: کاربر TeamId=200 را که واقعاً وجود دارد ولی
        // خارج از Scope اوست، مستقیم در فرم/AJAX جای‌گذاری می‌کند.
        var f = await BuildAsync(nameof(AddMember_IDOR_ArbitraryOutOfScopeTeamId_Denied));
        var (controller, roles, userName) = CreateController(f, SystemRoles.EngineeringSupervisor, f.MechEmployeeId, 1);

        var result = await controller.AddMemberAsync(roles, userName, f.ElecTeamId, f.MechEmployeeId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.Empty(f.Ctx.TeamMembers);
    }

    [Fact]
    public async Task AddMember_IDOR_NonExistentTeamId_Denied_FailClosed()
    {
        // TeamId که اصلاً وجود ندارد - باید Fail-Closed رد شود، نه اینکه خطای Service لِیک شود
        var f = await BuildAsync(nameof(AddMember_IDOR_NonExistentTeamId_Denied_FailClosed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.AddMemberAsync(roles, userName, 999999, f.MechEmployeeId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.Empty(f.Ctx.TeamMembers);
    }

    // ================= Fail-Closed: no Employee link at all =================

    [Fact]
    public async Task AddMember_UserWithNoEmployeeLink_Denied_FailClosed()
    {
        // کاربری با نقش معتبر (MaintenanceEngineer) ولی بدون هیچ Employee ای وصل - Scope قابل‌
        // تعیین نیست، پس طبق بند ۱۷ («Fail Closed») باید رد شود، نه اجازه.
        var f = await BuildAsync(nameof(AddMember_UserWithNoEmployeeLink_Denied_FailClosed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, employeeId: null, nextUserId: 1);

        var result = await controller.AddMemberAsync(roles, userName, f.MechTeamId, f.MechEmployeeId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.Empty(f.Ctx.TeamMembers);
    }

    // ================= Cross-discipline authority (EngineeringManager/SystemAdmin) bypass scope =================

    [Fact]
    public async Task AddMember_EngineeringManager_CrossDiscipline_Allowed()
    {
        // طبق ممیزی: EngineeringManager در همه‌ی ماژول‌های دیگر PEMS اختیار چند-دیسیپلینه دارد -
        // این‌جا هم معاف است، حتی وقتی هیچ Employee ای وصل نباشد.
        var f = await BuildAsync(nameof(AddMember_EngineeringManager_CrossDiscipline_Allowed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.EngineeringManager, employeeId: null, nextUserId: 1);

        var result = await controller.AddMemberAsync(roles, userName, f.ElecTeamId, f.ElecEmployeeId, f.MechRoleId);

        Assert.True(result.Success);
        Assert.Single(f.Ctx.TeamMembers);
    }

    // ================= E. Remove: in-scope allowed, out-of-scope denied =================

    [Fact]
    public async Task RemoveMember_InScopeTeam_Allowed()
    {
        var f = await BuildAsync(nameof(RemoveMember_InScopeTeam_Allowed));
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.MechTeamId, EmployeeId = f.MechEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.RemoveMemberAsync(roles, userName, 1);

        Assert.True(result.Success);
        Assert.False(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
    }

    [Fact]
    public async Task RemoveMember_OutOfScopeTeam_Denied_MembershipUnchanged()
    {
        // IDOR روی خودِ RemoveMember: teamMemberId متعلق به تیمِ خارج از Scope است - قبل از این
        // رفع، Controller اصلاً نمی‌دانست این عضویت متعلق به کدام Team است.
        var f = await BuildAsync(nameof(RemoveMember_OutOfScopeTeam_Denied_MembershipUnchanged));
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.ElecTeamId, EmployeeId = f.ElecEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.RemoveMemberAsync(roles, userName, 1);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.True(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
    }

    [Fact]
    public async Task RemoveMember_NonExistentTeamMemberId_Denied_FailClosed()
    {
        var f = await BuildAsync(nameof(RemoveMember_NonExistentTeamMemberId_Denied_FailClosed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.RemoveMemberAsync(roles, userName, 999999);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
    }

    // ================= F. Move — full security matrix (Section 11) =================

    [Fact]
    public async Task MoveMember_SourceInScope_EmployeeInScope_TargetInScope_Allowed()
    {
        var f = await BuildAsync(nameof(MoveMember_SourceInScope_EmployeeInScope_TargetInScope_Allowed));
        // یک تیم دومِ همان Discipline (مکانیک) برای مقصد
        f.Ctx.Teams.Add(new Team { TeamId = 101, Code = "T-MECH-2", Name = "Mech Team 2", MaintenanceGroupId = 1, IsActive = true });
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.MechTeamId, EmployeeId = f.MechEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.MechEmployeeId, f.MechTeamId, 101, f.MechRoleId);

        Assert.True(result.Success);
        Assert.True(f.Ctx.TeamMembers.Any(x => x.TeamId == 101 && x.EmployeeId == f.MechEmployeeId && x.IsActive));
    }

    [Fact]
    public async Task MoveMember_SourceInScope_TargetOutOfScope_Denied_NoMutation()
    {
        var f = await BuildAsync(nameof(MoveMember_SourceInScope_TargetOutOfScope_Denied_NoMutation));
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.MechTeamId, EmployeeId = f.MechEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.MechEmployeeId, f.MechTeamId, f.ElecTeamId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        // عضویت مبدأ باید دست‌نخورده بماند - نه غیرفعال شده، نه به مقصد اضافه شده
        Assert.True(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
        Assert.DoesNotContain(f.Ctx.TeamMembers, x => x.TeamId == f.ElecTeamId);
    }

    [Fact]
    public async Task MoveMember_SourceOutOfScope_TargetInScope_Denied_NoMutation()
    {
        // بند ۶: یک Team مجاز به‌عنوان مقصد نباید بهانه‌ای برای خارج‌کردن کارمند از یک Team
        // غیرمجاز (مبدأ) باشد.
        var f = await BuildAsync(nameof(MoveMember_SourceOutOfScope_TargetInScope_Denied_NoMutation));
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.ElecTeamId, EmployeeId = f.ElecEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.ElecEmployeeId, f.ElecTeamId, f.MechTeamId, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.True(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
        Assert.DoesNotContain(f.Ctx.TeamMembers, x => x.TeamId == f.MechTeamId);
    }

    [Fact]
    public async Task MoveMember_EmployeeOutOfScope_SourceAndTargetInScope_Denied_NoMutation()
    {
        var f = await BuildAsync(nameof(MoveMember_EmployeeOutOfScope_SourceAndTargetInScope_Denied_NoMutation));
        f.Ctx.Teams.Add(new Team { TeamId = 101, Code = "T-MECH-2", Name = "Mech Team 2", MaintenanceGroupId = 1, IsActive = true });
        // کارمند برق به‌طور نامتعارف عضو تیم مکانیک است (سناریوی تاریخی/دستی) - کاربر مکانیک
        // نباید بتواند این کارمند را جابه‌جا کند چون خودِ کارمند خارج از Scope اوست.
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.MechTeamId, EmployeeId = f.ElecEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.ElecEmployeeId, f.MechTeamId, 101, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.True(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
    }

    [Fact]
    public async Task MoveMember_AllOutOfScope_Denied_NoMutation()
    {
        var f = await BuildAsync(nameof(MoveMember_AllOutOfScope_Denied_NoMutation));
        f.Ctx.Teams.Add(new Team { TeamId = 201, Code = "T-ELEC-2", Name = "Elec Team 2", MaintenanceGroupId = 2, IsActive = true });
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.ElecTeamId, EmployeeId = f.ElecEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.ElecEmployeeId, f.ElecTeamId, 201, f.MechRoleId);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.True(f.Ctx.TeamMembers.Single(x => x.TeamMemberId == 1).IsActive);
    }

    [Fact]
    public async Task MoveMember_EngineeringManager_CrossDiscipline_Allowed()
    {
        var f = await BuildAsync(nameof(MoveMember_EngineeringManager_CrossDiscipline_Allowed));
        f.Ctx.Teams.Add(new Team { TeamId = 201, Code = "T-ELEC-2", Name = "Elec Team 2", MaintenanceGroupId = 2, IsActive = true });
        f.Ctx.TeamMembers.Add(new TeamMember { TeamMemberId = 1, TeamId = f.ElecTeamId, EmployeeId = f.ElecEmployeeId, RoleId = f.MechRoleId, IsActive = true });
        await f.Ctx.SaveChangesAsync();
        var (controller, roles, userName) = CreateController(f, SystemRoles.EngineeringManager, f.MechEmployeeId, 1);

        var result = await controller.MoveMemberAsync(roles, userName, f.ElecEmployeeId, f.ElecTeamId, 201, f.MechRoleId);

        Assert.True(result.Success);
        Assert.True(f.Ctx.TeamMembers.Any(x => x.TeamId == 201 && x.EmployeeId == f.ElecEmployeeId && x.IsActive));
    }

    // ================= CreateTeam scope (consistency extension) =================

    [Fact]
    public async Task CreateTeam_UnderOutOfScopeMaintenanceGroup_Denied_NoMutation()
    {
        var f = await BuildAsync(nameof(CreateTeam_UnderOutOfScopeMaintenanceGroup_Denied_NoMutation));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        // MaintenanceGroupId=2 متعلق به Discipline برق است - کاربر مکانیک است
        var result = await controller.CreateTeamAsync(roles, userName, "NEW-T", "New Team", 2, null, null);

        Assert.False(result.Success); Assert.Equal("You may not do this.", result.Error);
        Assert.DoesNotContain(f.Ctx.Teams, x => x.Code == "NEW-T");
    }

    [Fact]
    public async Task CreateTeam_UnderInScopeMaintenanceGroup_Allowed()
    {
        var f = await BuildAsync(nameof(CreateTeam_UnderInScopeMaintenanceGroup_Allowed));
        var (controller, roles, userName) = CreateController(f, SystemRoles.MaintenanceEngineer, f.MechEmployeeId, 1);

        var result = await controller.CreateTeamAsync(roles, userName, "NEW-T", "New Team", 1, null, null);

        Assert.True(result.Success);
        Assert.Contains(f.Ctx.Teams, x => x.Code == "NEW-T");
    }
}
