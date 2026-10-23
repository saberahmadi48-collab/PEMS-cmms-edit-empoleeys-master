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
using PEMS.Web.Areas.Warehouse.Controllers;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Procurement;

/// <summary>
/// تصمیم‌های مالک ۲۰۲۶-۱۰-۱۴ برای گیت P0:
///  * P0-09 / D-OPEN-02: صفحات خواندنی خرید/انبار مجوز View می‌خواهند؛ نقش‌هایی که روی همان ماژول مجوز دیگری دارند View می‌گیرند.
///  * P0-10 / D-OPEN-04: ایجادکننده‌ی مناقصه نمی‌تواند آن را تأیید کند؛ SystemAdmin مستثنا با ثبت ممیزی.
/// (D-OPEN-05، ویرایشگر قرارداد ≠ تأییدکننده، در ContractWorkflowServiceTests با پیشوند P010_ است.)
/// </summary>
public class ProcurementP0ReadAndSodDecisionTests
{
    private static async Task<(PEMSDbContext Ctx, PermissionService Permissions, AccessControlService Access)> CreateAsync(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"P0Read_{name}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var access = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, access);
        await permissions.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());
        return (ctx, permissions, access);
    }

    private static FakeCurrentUserService User(params string[] roles) => new() { Roles = roles.ToList() };

    // ==============================================================================
    // P0-09: هر صفحه‌ی خواندنی بدون View رد می‌شود (قبل از هر دسترسی به داده؛ وابستگی‌های دیگر عمداً null هستند)
    // ==============================================================================

    [Fact]
    public async Task Contract_Index_Details_CreateForm_WithoutPermission_AreForbidden()
    {
        var (ctx, permissions, access) = await CreateAsync(nameof(Contract_Index_Details_CreateForm_WithoutPermission_AreForbidden));
        var c = new ContractController(null!, null!, null!, access, permissions, User(SystemRoles.Viewer), ctx).WithTempData();

        Assert.IsType<ForbidResult>(await c.Index());
        Assert.IsType<ForbidResult>(await c.Details(1));
        Assert.IsType<ForbidResult>(await c.Create());
    }

    [Fact]
    public async Task Irn_StoreRequisition_MaterialItem_ReadPages_WithoutAnyRole_AreForbidden()
    {
        var (ctx, permissions, _) = await CreateAsync(nameof(Irn_StoreRequisition_MaterialItem_ReadPages_WithoutAnyRole_AreForbidden));
        var none = User();

        var irn = new IrnReceiptController(ctx, null!, none, permissions, null!, null!);
        Assert.IsType<ForbidResult>(await irn.Index(null));
        Assert.IsType<ForbidResult>(await irn.Details(1));

        var sr = new StoreRequisitionController(ctx, null!, none, permissions, null!);
        Assert.IsType<ForbidResult>(await sr.Index(null));
        Assert.IsType<ForbidResult>(await sr.Details(1));

        var mi = new MaterialItemController(ctx, null!, null!, none, permissions, null!);
        Assert.IsType<ForbidResult>(await mi.Index(null, null));
        Assert.IsType<ForbidResult>(await mi.Details(1));
    }

    [Fact]
    public async Task Inventory_Dashboard_Ledger_And_ProcurementReport_WithoutPermission_AreForbidden()
    {
        var (ctx, permissions, _) = await CreateAsync(nameof(Inventory_Dashboard_Ledger_And_ProcurementReport_WithoutPermission_AreForbidden));
        var none = User();

        Assert.IsType<ForbidResult>(await new InventoryController(ctx, null!, permissions, none).Index());
        Assert.IsType<ForbidResult>(await new InventoryTransactionController(null!, ctx, permissions, none).Index(null, null, null, null, null));
        Assert.IsType<ForbidResult>(await new ReportController(null!, null!, null!, null!, null!, null!, ctx, permissions, none).Procurement(null, null, null, null));
    }

    [Fact]
    public async Task Viewer_KeepsReadAccess_WhereTheSeedAlreadyGrantedView()
    {
        var (ctx, permissions, _) = await CreateAsync(nameof(Viewer_KeepsReadAccess_WhereTheSeedAlreadyGrantedView));
        var viewer = User(SystemRoles.Viewer);

        // سید موجود: Viewer روی IRN / StoreRequisition / MaterialItem / ProcurementPackage فقط View دارد
        Assert.IsType<ViewResult>(await new IrnReceiptController(ctx, null!, viewer, permissions, null!, null!).Index(null));
        Assert.IsType<ViewResult>(await new StoreRequisitionController(ctx, null!, viewer, permissions, null!).Index(null));
        Assert.True(await permissions.HasPermissionAsync(viewer.Roles, "MaterialItem", "View"));
        Assert.True(await permissions.HasPermissionAsync(viewer.Roles, "ProcurementPackage", "View"));
        // Vendors و Contracts هیچ ردیفی برای Viewer ندارند (تصمیم مالک: فقط نقش‌های دارای مجوز آن ماژول)
        Assert.False(await permissions.HasPermissionAsync(viewer.Roles, "Vendors", "View"));
        Assert.False(await permissions.HasPermissionAsync(viewer.Roles, "Contracts", "View"));
    }

    [Theory]
    [InlineData("Contracts", SystemRoles.Legal)]
    [InlineData("Contracts", SystemRoles.QAManager)]
    [InlineData("Contracts", SystemRoles.EngineeringManager)]
    [InlineData("Vendors", SystemRoles.ContractManager)]
    [InlineData("IRN", SystemRoles.QAManager)]
    [InlineData("MaterialItem", SystemRoles.MaintenanceEngineer)]
    public async Task EverySeededRoleWithAnotherPermission_OnTheModule_HasView(string module, string role)
    {
        var (_, permissions, _) = await CreateAsync($"seeded_{module}_{role}");

        Assert.True(await permissions.HasPermissionAsync(new[] { role }, module, "View"));
    }

    // ==============================================================================
    // P0-09: ProcurementReadViewSeeder - فقط افزایشی، Idempotent، و پایدار در برابر همگام‌سازی Legacy
    // ==============================================================================

    [Fact]
    public async Task Seeder_GivesViewToRowsThatHoldAnotherPermission_AndTheGrantSurvivesTheLegacySync()
    {
        var (ctx, permissions, _) = await CreateAsync(nameof(Seeder_GivesViewToRowsThatHoldAnotherPermission_AndTheGrantSurvivesTheLegacySync));
        var row = ctx.RolePermissions.Single(r => r.RoleName == SystemRoles.QAManager && r.ModuleName == "IRN");
        row.CanView = false;                                   // ردیفی که مدیر View آن را برداشته ولی Edit/Approve دارد
        await ctx.SaveChangesAsync();
        var services = new ServiceCollection(); services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());
        Assert.False(await permissions.HasPermissionAsync(new[] { SystemRoles.QAManager }, "IRN", "View"));

        var changed = await ProcurementReadViewSeeder.EnsureAsync(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        Assert.Equal(1, changed);
        Assert.True(row.CanView);
        Assert.True(await permissions.HasPermissionAsync(new[] { SystemRoles.QAManager }, "IRN", "View"));
        Assert.Equal(0, await ProcurementReadViewSeeder.EnsureAsync(ctx));   // Idempotent
    }

    [Fact]
    public async Task Seeder_DoesNotTouchRowsWithoutPermissions_OrModulesOutsideTheDecision()
    {
        var (ctx, _, _) = await CreateAsync(nameof(Seeder_DoesNotTouchRowsWithoutPermissions_OrModulesOutsideTheDecision));
        // CanView پیش‌فرض موجودیت true است؛ این‌جا صراحتاً false
        ctx.RolePermissions.Add(new RolePermission { RoleName = SystemRoles.HSE, ModuleName = "IRN", CanView = false, CreatedDate = DateTime.Now });                       // هیچ مجوزی
        ctx.RolePermissions.Add(new RolePermission { RoleName = SystemRoles.HSE, ModuleName = "WorkRequest", CanView = false, CanEdit = true, CreatedDate = DateTime.Now }); // خارج از محدوده
        await ctx.SaveChangesAsync();

        await ProcurementReadViewSeeder.EnsureAsync(ctx);

        Assert.False(ctx.RolePermissions.Single(r => r.RoleName == SystemRoles.HSE && r.ModuleName == "IRN").CanView);
        Assert.False(ctx.RolePermissions.Single(r => r.RoleName == SystemRoles.HSE && r.ModuleName == "WorkRequest").CanView);
    }

    [Fact]
    public async Task Seeder_AddsViewForRolesThatOnlyHoldARoleModuleAction_OnceOnly()
    {
        var (ctx, _, _) = await CreateAsync(nameof(Seeder_AddsViewForRolesThatOnlyHoldARoleModuleAction_OnceOnly));
        ctx.RoleModuleActions.Add(new RoleModuleAction { RoleName = SystemRoles.Finance, Module = "StoreRequisition", Action = "Approve" });
        await ctx.SaveChangesAsync();

        await ProcurementReadViewSeeder.EnsureAsync(ctx);
        await ProcurementReadViewSeeder.EnsureAsync(ctx);

        Assert.Single(ctx.RoleModuleActions.Where(a => a.RoleName == SystemRoles.Finance && a.Module == "StoreRequisition" && a.Action == "View"));
    }

    // ==============================================================================
    // P0-10 / D-OPEN-04: ایجادکننده‌ی مناقصه ≠ تأییدکننده (SystemAdmin مستثنا با ثبت ممیزی)
    // ==============================================================================

    private static async Task<(PEMSDbContext Ctx, TenderService Service, int TenderId, int NegotiationId)> TenderAsync(string name, string? creator)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"P0Tender_{name}");
        ctx.CurrentUserName = creator;                                // همان مسیری که DbContext در برنامه CreatedBy را می‌نویسد
        var tender = new Tender { TenderNumber = "TN-1", Title = "Pumps", Status = "Evaluation", ProcurementPackageId = 1 };
        ctx.Tenders.Add(tender);
        await ctx.SaveChangesAsync();
        var bid = new Bid { BidNumber = "B-1", TenderId = tender.TenderId, VendorId = 1 };
        ctx.Bids.Add(bid);
        await ctx.SaveChangesAsync();
        var negotiation = new BidNegotiation { BidId = bid.BidId, RoundNo = 1, NegotiatedValue = 90m };
        ctx.BidNegotiations.Add(negotiation);
        await ctx.SaveChangesAsync();
        ctx.CurrentUserName = null;
        return (ctx, new TenderService(ctx, new FakeNumberingService()), tender.TenderId, negotiation.BidNegotiationId);
    }

    [Fact]
    public async Task Tender_Creator_CannotApproveIt()
    {
        var (_, svc, tenderId, _) = await TenderAsync(nameof(Tender_Creator_CannotApproveIt), "alice");

        Assert.Equal(TenderApprovalSeparation.DeniedCreator, await svc.CheckCreatorApproverSeparationAsync(tenderId, null, 7, "alice", false));
        Assert.Equal(TenderApprovalSeparation.DeniedCreator, await svc.CheckCreatorApproverSeparationAsync(tenderId, null, 7, "ALICE", false));
    }

    [Fact]
    public async Task Tender_Creator_ApprovingThroughANegotiation_IsAlsoDenied()
    {
        var (_, svc, _, negotiationId) = await TenderAsync(nameof(Tender_Creator_ApprovingThroughANegotiation_IsAlsoDenied), "alice");

        Assert.Equal(TenderApprovalSeparation.DeniedCreator, await svc.CheckCreatorApproverSeparationAsync(null, negotiationId, 7, "alice", false));
    }

    [Fact]
    public async Task Tender_AnotherUser_IsAllowed_WithoutAnExemptionRow()
    {
        var (ctx, svc, tenderId, _) = await TenderAsync(nameof(Tender_AnotherUser_IsAllowed_WithoutAnExemptionRow), "alice");

        Assert.Equal(TenderApprovalSeparation.Allowed, await svc.CheckCreatorApproverSeparationAsync(tenderId, null, 8, "bob", false));
        Assert.Empty(ctx.AuthorizationAuditLogs);
    }

    [Fact]
    public async Task Tender_SystemAdminCreator_IsExempt_AndTheExemptionIsAudited()
    {
        var (ctx, svc, tenderId, _) = await TenderAsync(nameof(Tender_SystemAdminCreator_IsExempt_AndTheExemptionIsAudited), "admin");

        var result = await svc.CheckCreatorApproverSeparationAsync(tenderId, null, 1, "admin", true, "10.0.0.5");

        Assert.Equal(TenderApprovalSeparation.AllowedBySystemAdminExemption, result);
        var row = Assert.Single(ctx.AuthorizationAuditLogs);
        Assert.Equal(1, row.UserId);
        Assert.Equal("Tender", row.ResourceName);
        Assert.Equal(tenderId.ToString(), row.ResourceId);
        Assert.Contains("D-OPEN-04", row.Reason);
        Assert.Equal("10.0.0.5", row.IpAddress);
    }

    [Fact]
    public async Task Tender_UnknownCurrentUser_FailsClosed()
    {
        var (_, svc, tenderId, _) = await TenderAsync(nameof(Tender_UnknownCurrentUser_FailsClosed), "alice");

        Assert.Equal(TenderApprovalSeparation.DeniedUnverifiable, await svc.CheckCreatorApproverSeparationAsync(tenderId, null, null, null, false));
    }

    [Fact]
    public async Task Tender_LegacyRecordWithoutCreator_IsNotBlocked()
    {
        var (_, svc, tenderId, _) = await TenderAsync(nameof(Tender_LegacyRecordWithoutCreator_IsNotBlocked), null);

        Assert.Equal(TenderApprovalSeparation.Allowed, await svc.CheckCreatorApproverSeparationAsync(tenderId, null, 7, "alice", false));
    }

    [Fact]
    public async Task TenderController_ApproveNegotiation_ByTheTenderCreator_IsRefused_AndNothingChanges()
    {
        var (ctx, svc, tenderId, negotiationId) = await TenderAsync(nameof(TenderController_ApproveNegotiation_ByTheTenderCreator_IsRefused_AndNothingChanges), "alice");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var access = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, access);
        await permissions.SeedDefaultsAsync();
        var services = new ServiceCollection(); services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var user = new FakeCurrentUserService { UserId = 7, UserName = "alice", Roles = new List<string> { SystemRoles.ContractManager } };
        var controller = new TenderController(svc, null!, permissions, user, ctx, access).WithTempData();

        var result = await controller.ApproveNegotiation(tenderId, negotiationId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("تفکیک وظایف", (string)controller.TempData["Error"]!);
        Assert.Equal("Proposed", ctx.BidNegotiations.Single().Status);
    }
}
