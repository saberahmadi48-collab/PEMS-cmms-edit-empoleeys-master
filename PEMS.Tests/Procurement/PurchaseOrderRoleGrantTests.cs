using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-470, owner decision 2026-10-30: Procurement Manager issues approved purchase orders (PurchaseOrder Approve).
/// RoleCatalog defaults reach only newly created roles, so databases where the procurement roles already exist
/// must get the PO rows from the permission seeding, without overwriting a row an admin already set.
/// </summary>
public class PurchaseOrderRoleGrantTests
{
    [Fact]
    public void RoleCatalog_ProcurementManager_HasPurchaseOrderApprove()
    {
        var grant = RoleCatalog.Find(RoleCatalog.ProcurementManager)!.DefaultPermissions!
            .Single(g => g.Module == "PurchaseOrder");

        Assert.True(grant.Approve);
        Assert.False(grant.Create);
    }

    [Fact]
    public async Task ExistingProcurementRoles_GetPurchaseOrderRows()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(ExistingProcurementRoles_GetPurchaseOrderRows));
        // the PurchaseOrder module already has rows for other roles (an earlier seeding), but none for the procurement roles
        ctx.RolePermissions.Add(new RolePermission { RoleName = SystemRoles.ContractManager, ModuleName = "PurchaseOrder", CanView = true, CanCreate = true, CanEdit = true, CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();

        await new PermissionService(ctx, new AccessControlService(ctx)).SeedDefaultsAsync();

        var manager = await ctx.RolePermissions.SingleAsync(x => x.RoleName == RoleCatalog.ProcurementManager && x.ModuleName == "PurchaseOrder");
        Assert.True(manager.CanView);
        Assert.True(manager.CanApprove);
        Assert.False(manager.CanCreate);
        Assert.False(manager.CanDelete);

        var buyer = await ctx.RolePermissions.SingleAsync(x => x.RoleName == RoleCatalog.ProcurementBuyer && x.ModuleName == "PurchaseOrder");
        Assert.True(buyer.CanCreate);
        Assert.True(buyer.CanEdit);
        Assert.False(buyer.CanApprove);
    }

    [Fact]
    public async Task AdminSetRow_IsKept()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(AdminSetRow_IsKept));
        ctx.RolePermissions.Add(new RolePermission { RoleName = RoleCatalog.ProcurementManager, ModuleName = "PurchaseOrder", CanView = true, CanApprove = false, CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();

        await new PermissionService(ctx, new AccessControlService(ctx)).SeedDefaultsAsync();

        var rows = await ctx.RolePermissions.Where(x => x.RoleName == RoleCatalog.ProcurementManager && x.ModuleName == "PurchaseOrder").ToListAsync();
        Assert.Single(rows);
        Assert.False(rows[0].CanApprove);
    }
}
