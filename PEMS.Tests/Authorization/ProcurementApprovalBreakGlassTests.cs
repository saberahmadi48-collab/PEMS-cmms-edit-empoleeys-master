using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// Procurement Lifecycle - Work Package B: (۱) PO دستی جدید Draft است و فقط با انتقال
/// اعتبارسنجی‌شده‌ی IssueAsync (مجوز Approve + SoD) صادر می‌شود؛ (۲) بای‌پس سراسری SystemAdmin
/// روی تاییدیه‌های حساس تدارکات (ProcurementPackage/Tender/PurchaseOrder/Contracts با Approve)
/// اعمال نمی‌شود، ولی بای‌پس برای سایر اقدامات و Grant صریح اضطراری حفظ می‌شود.
/// </summary>
public class ProcurementApprovalBreakGlassTests
{
    private static async Task<PEMSDbContext> CreateSeededDbAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"ProcBreakGlass_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        await new PermissionService(ctx, accessControl).SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "Project One", IsActive = true });
        ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "Vendor A", IsActive = true });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static async Task<ApplicationUser> AddUserAsync(PEMSDbContext ctx, string userName, string role)
    {
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@pems.local", FullName = userName };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var roleEntity = ctx.Roles.First(r => r.Name == role);
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = roleEntity.Id });
        await ctx.SaveChangesAsync();
        return user;
    }

    /// <summary>Grant صریح (مسیر مدیریتی) برای نقش؛ Permission را در صورت نبود می‌سازد.</summary>
    private static async Task GrantAsync(PEMSDbContext ctx, string role, string resource, string action)
    {
        var permission = await ctx.Permissions.FirstOrDefaultAsync(p => p.ResourceName == resource && p.ActionName == action);
        if (permission == null)
        {
            permission = new Permission { Name = $"{resource}.{action}", ResourceName = resource, ActionName = action, IsActive = true };
            ctx.Permissions.Add(permission);
            await ctx.SaveChangesAsync();
        }
        var roleEntity = ctx.Roles.First(r => r.Name == role);
        ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = roleEntity.Id, PermissionId = permission.PermissionId, IsActive = true });
        await ctx.SaveChangesAsync();
    }

    // ───────────────────── Policy table ─────────────────────

    [Theory]
    [InlineData("ProcurementPackage", "Approve", true)]
    [InlineData("Tender", "Approve", true)]
    [InlineData("PurchaseOrder", "Approve", true)]
    [InlineData("Contracts", "Approve", true)]
    [InlineData("WorkflowManagement", "Edit", true)]            // Break-Glass موجود حفظ شده
    [InlineData("ExecutionGate", "EmergencyOverride", true)]    // Break-Glass موجود حفظ شده
    [InlineData("ProcurementPackage", "Edit", false)]
    [InlineData("PurchaseOrder", "Create", false)]
    [InlineData("Tender", "Edit", false)]
    [InlineData("Equipment", "Approve", false)]                  // بای‌پس سراسری برای ماژول‌های دیگر دست‌نخورده
    public void RequiresExplicitGrant_CoversExactlyTheVerifiedSensitiveApprovals(string resource, string action, bool expected)
        => Assert.Equal(expected, AccessControlService.RequiresExplicitGrant(resource, action));

    // ───────────────────── SystemAdmin bypass scope (role-only path) ─────────────────────

    [Theory]
    [InlineData("ProcurementPackage")]
    [InlineData("Tender")]
    [InlineData("PurchaseOrder")]
    [InlineData("Contracts")]
    public async Task SystemAdmin_WithoutExplicitGrant_CannotApprove_SensitiveProcurementResources(string resource)
    {
        var ctx = await CreateSeededDbAsync(nameof(SystemAdmin_WithoutExplicitGrant_CannotApprove_SensitiveProcurementResources) + resource);
        var permissions = new PermissionService(ctx, new AccessControlService(ctx));

        var allowed = await permissions.HasPermissionAsync(new List<string> { SystemRoles.SystemAdmin }, resource, "Approve");

        Assert.False(allowed);
    }

    [Theory]
    [InlineData("ProcurementPackage", "Edit")]
    [InlineData("PurchaseOrder", "Create")]
    [InlineData("Tender", "Edit")]
    [InlineData("Contracts", "Edit")]
    public async Task SystemAdmin_StillBypasses_NonApprovalActions(string resource, string action)
    {
        var ctx = await CreateSeededDbAsync(nameof(SystemAdmin_StillBypasses_NonApprovalActions) + resource + action);
        var permissions = new PermissionService(ctx, new AccessControlService(ctx));

        Assert.True(await permissions.HasPermissionAsync(new List<string> { SystemRoles.SystemAdmin }, resource, action));
    }

    [Fact]
    public async Task SystemAdmin_WithExplicitGrant_CanApprove_DocumentedEmergencyAccess()
    {
        var ctx = await CreateSeededDbAsync(nameof(SystemAdmin_WithExplicitGrant_CanApprove_DocumentedEmergencyAccess));
        await GrantAsync(ctx, SystemRoles.SystemAdmin, "ProcurementPackage", "Approve");
        var permissions = new PermissionService(ctx, new AccessControlService(ctx));

        Assert.True(await permissions.HasPermissionAsync(new List<string> { SystemRoles.SystemAdmin }, "ProcurementPackage", "Approve"));
        // و Grant صریح برای یک منبع، منبع دیگر را باز نمی‌کند
        Assert.False(await permissions.HasPermissionAsync(new List<string> { SystemRoles.SystemAdmin }, "Tender", "Approve"));
    }

    [Fact]
    public async Task OrdinaryRole_WithoutApprove_IsDenied_AndAuthorizedApprover_IsAllowed()
    {
        var ctx = await CreateSeededDbAsync(nameof(OrdinaryRole_WithoutApprove_IsDenied_AndAuthorizedApprover_IsAllowed));
        var permissions = new PermissionService(ctx, new AccessControlService(ctx));

        Assert.False(await permissions.HasPermissionAsync(new List<string> { SystemRoles.DisciplineEngineer }, "ProcurementPackage", "Approve"));
        Assert.True(await permissions.HasPermissionAsync(new List<string> { SystemRoles.EngineeringManager }, "ProcurementPackage", "Approve"));
        Assert.True(await permissions.HasPermissionAsync(new List<string> { SystemRoles.ContractManager }, "Tender", "Approve"));
    }

    // ───────────────────── SystemAdmin bypass scope (user-aware path + SoD + audit) ─────────────────────

    [Fact]
    public async Task SystemAdmin_UserAware_WithoutGrant_IsDenied_AndTheDenialIsAudited()
    {
        var ctx = await CreateSeededDbAsync(nameof(SystemAdmin_UserAware_WithoutGrant_IsDenied_AndTheDenialIsAudited));
        var admin = await AddUserAsync(ctx, "admin", SystemRoles.SystemAdmin);

        var decision = await new AccessControlService(ctx).AuthorizeAsync(new AccessControlRequest
        {
            UserId = admin.Id, ResourceName = "ProcurementPackage", ActionName = "Approve", ResourceId = "1"
        });

        Assert.False(decision.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedByRole, decision.Reason);
        Assert.Contains(ctx.AuthorizationAuditLogs, a => a.UserId == admin.Id && a.ActionName == "Approve" && a.Decision != nameof(AccessDecisionReason.Allow));
    }

    [Fact]
    public async Task SystemAdmin_WithExplicitGrant_IsStillSubjectToSoD_OnTheSameRecord()
    {
        var ctx = await CreateSeededDbAsync(nameof(SystemAdmin_WithExplicitGrant_IsStillSubjectToSoD_OnTheSameRecord));
        await GrantAsync(ctx, SystemRoles.SystemAdmin, "ProcurementPackage", "Approve");
        var admin = await AddUserAsync(ctx, "admin", SystemRoles.SystemAdmin);
        var access = new AccessControlService(ctx);

        // ادمین (با بای‌پس Create) رکورد را می‌سازد و سپس سعی می‌کند همان را تایید کند
        var create = await access.AuthorizeAsync(new AccessControlRequest
        { UserId = admin.Id, ResourceName = "ProcurementPackage", ActionName = "Create", ResourceId = "7" });
        Assert.True(create.Allowed);

        var approveSame = await access.AuthorizeAsync(new AccessControlRequest
        { UserId = admin.Id, ResourceName = "ProcurementPackage", ActionName = "Approve", ResourceId = "7" });
        var approveOther = await access.AuthorizeAsync(new AccessControlRequest
        { UserId = admin.Id, ResourceName = "ProcurementPackage", ActionName = "Approve", ResourceId = "8" });

        Assert.False(approveSame.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, approveSame.Reason);
        Assert.True(approveOther.Allowed);
    }

    // ───────────────────── PurchaseOrder: Draft + Issue ─────────────────────

    [Fact]
    public async Task ManualPo_CreateAsync_StartsAsDraft()
    {
        var ctx = await CreateSeededDbAsync(nameof(ManualPo_CreateAsync_StartsAsDraft));

        var id = await new PurchaseOrderService(ctx).CreateAsync("PO-1", 1, 1, null, "d", 100m);

        Assert.Equal("Draft", ctx.PurchaseOrders.Single(x => x.PoId == id).Status);
    }

    [Fact]
    public async Task ImportHelper_FindOrCreateByNumber_KeepsLegacyIssued()
    {
        var ctx = await CreateSeededDbAsync(nameof(ImportHelper_FindOrCreateByNumber_KeepsLegacyIssued));

        var id = await new PurchaseOrderService(ctx).FindOrCreateByNumberAsync("PO-IMP-1", 1, 1);

        Assert.Equal("Issued", ctx.PurchaseOrders.Single(x => x.PoId == id).Status);
    }

    [Fact]
    public async Task Entity_DefaultStatus_StaysIssued_ForLegacyAndImportCallers()
        => Assert.Equal("Issued", new PurchaseOrder().Status);

    // SCRUM-470 (owner decision 2026-10-29): only an approved PO is issued; a Draft is refused like every other status.
    [Fact]
    public async Task IssueAsync_MovesApprovedToIssued_AndRejectsEverythingElse_IncludingDraft()
    {
        var ctx = await CreateSeededDbAsync(nameof(IssueAsync_MovesApprovedToIssued_AndRejectsEverythingElse_IncludingDraft));
        var service = new PurchaseOrderService(ctx);
        var approvedId = await service.CreateAsync("PO-A", 1, 1, null, null, null);
        ctx.PurchaseOrders.Single(x => x.PoId == approvedId).Status = "Approved";   // approval itself: PurchaseOrderApprovalServiceTests
        await service.CreateAsync("PO-D", 1, 1, null, null, null);
        ctx.PurchaseOrders.Add(new PurchaseOrder { PoNumber = "PO-LEGACY", ProjectId = 1, VendorId = 1 }); // پیش‌فرض Issued
        ctx.PurchaseOrders.Add(new PurchaseOrder { PoNumber = "PO-CLOSED", ProjectId = 1, VendorId = 1, Status = "Closed" });
        ctx.PurchaseOrders.Add(new PurchaseOrder { PoNumber = "PO-OFF", ProjectId = 1, VendorId = 1, Status = "Approved", IsActive = false });
        ctx.PurchaseOrders.Add(new PurchaseOrder { PoNumber = "PO-SUB", ProjectId = 1, VendorId = 1, Status = "Submitted" });
        await ctx.SaveChangesAsync();

        await service.IssueAsync(approvedId);
        Assert.Equal("Issued", ctx.PurchaseOrders.Single(x => x.PoId == approvedId).Status);

        foreach (var number in new[] { "PO-A", "PO-D", "PO-LEGACY", "PO-CLOSED", "PO-OFF", "PO-SUB" })
        {
            var po = ctx.PurchaseOrders.Single(x => x.PoNumber == number);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(po.PoId));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(99999));
        Assert.Equal("Closed", ctx.PurchaseOrders.Single(x => x.PoNumber == "PO-CLOSED").Status);
    }
}
