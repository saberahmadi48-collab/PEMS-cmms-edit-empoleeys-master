using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0 / F-01 - ثبت فروشنده نباید وضعیت صلاحیت، ریسک یا فعال‌بودن را از ورودی کلاینت بپذیرد (Over-posting). صلاحیت فقط از
/// مسیر کنترل‌شده‌ی VendorQualificationService (با تاریخچه) تغییر می‌کند. ایمنی در دو لایه آزموده می‌شود: سرویس (مستقل از Binding) و Bind.
/// </summary>
public class VendorCreateHardeningTests
{
    private static (VendorController Controller, PEMSDbContext Ctx, VendorService Service) Create(string db, bool allow = true)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"VendorP0_{db}");
        var service = new VendorService(ctx);
        var perm = new FakePermissionService { AllowAll = allow };
        var user = new FakeCurrentUserService { UserId = 1, Roles = new[] { "ContractManager" } };
        var controller = new VendorController(service, new VendorRiskService(ctx), new VendorQualificationService(ctx), perm, user, ctx).WithTempData();
        return (controller, ctx, service);
    }

    private static Vendor Forged(string name = "Forged Vendor", string? code = "FRG") => new()
    {
        Name = name,
        VendorCode = code,
        Country = "IR",
        ContactName = "Contact",
        QualificationStatus = "Qualified",
        QualificationCategory = "Critical",
        QualificationEffectiveDate = DateTime.Today,
        QualificationExpiryDate = DateTime.Today.AddYears(5),
        QualificationEvidenceReference = "fake-certificate",
        QualificationReviewedById = 99,
        QualificationNotes = "forged",
        RiskLevel = "Low",
        RiskScore = 0.01,
        RiskAssessedDate = DateTime.Today,
        IsActive = false
    };

    private static void AssertSanitized(Vendor stored)
    {
        Assert.Equal("NotQualified", stored.QualificationStatus);
        Assert.Null(stored.QualificationCategory);
        Assert.Null(stored.QualificationEffectiveDate);
        Assert.Null(stored.QualificationExpiryDate);
        Assert.Null(stored.QualificationEvidenceReference);
        Assert.Null(stored.QualificationReviewedById);
        Assert.Null(stored.QualificationNotes);
        Assert.Equal("Unknown", stored.RiskLevel);
        Assert.Null(stored.RiskScore);
        Assert.Null(stored.RiskAssessedDate);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Service_Create_IgnoresForgedQualificationRiskAndActiveFields_ButKeepsLegitimateOnes()
    {
        var (_, ctx, service) = Create(nameof(Service_Create_IgnoresForgedQualificationRiskAndActiveFields_ButKeepsLegitimateOnes));

        var id = await service.CreateAsync(Forged("  Acme Pumps  ", " ACM-1 "));

        var stored = await ctx.Vendors.SingleAsync(v => v.VendorId == id);
        AssertSanitized(stored);
        Assert.Equal("Acme Pumps", stored.Name);        // نام Trim می‌شود
        Assert.Equal("ACM-1", stored.VendorCode);
        Assert.Equal("IR", stored.Country);
        Assert.Equal("Contact", stored.ContactName);
    }

    [Fact]
    public async Task Controller_Create_WithForgedFields_StoresSanitizedVendor()
    {
        var (controller, ctx, _) = Create(nameof(Controller_Create_WithForgedFields_StoresSanitizedVendor));

        var result = await controller.Create(Forged());

        Assert.IsType<RedirectToActionResult>(result);
        AssertSanitized(await ctx.Vendors.SingleAsync());
    }

    [Fact]
    public void Controller_Create_Post_BindsOnlyTheSixAllowedFields()
    {
        var method = typeof(VendorController).GetMethods()
            .Single(m => m.Name == nameof(VendorController.Create)
                         && m.GetCustomAttribute<HttpPostAttribute>() != null
                         && m.GetParameters().Length == 1
                         && m.GetParameters()[0].ParameterType == typeof(Vendor));

        var bind = method.GetParameters()[0].GetCustomAttribute<BindAttribute>();

        Assert.NotNull(bind);
        var included = bind!.Include.OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "ContactEmail", "ContactName", "ContactPhone", "Country", "Name", "VendorCode" }, included);
        foreach (var protectedField in new[] { "QualificationStatus", "RiskLevel", "RiskScore", "IsActive", "QualificationExpiryDate", "QualificationReviewedById" })
            Assert.DoesNotContain(protectedField, included);
    }

    [Fact]
    public async Task Create_WithBlankName_IsRejected_ReturnsViewWithInput_AndStoresNothing()
    {
        var (controller, ctx, _) = Create(nameof(Create_WithBlankName_IsRejected_ReturnsViewWithInput_AndStoresNothing));

        var result = await controller.Create(new Vendor { Name = "   ", Country = "DE" });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("DE", ((Vendor)view.Model!).Country);       // ورودی کاربر حفظ می‌شود
        Assert.Equal(0, await ctx.Vendors.CountAsync());
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Create_WithDuplicateName_IgnoringCaseAndSpaces_IsRejected()
    {
        var (controller, ctx, service) = Create(nameof(Create_WithDuplicateName_IgnoringCaseAndSpaces_IsRejected));
        await service.CreateAsync(new Vendor { Name = "Acme Pumps" });

        var result = await controller.Create(new Vendor { Name = "  ACME pumps " });

        Assert.IsType<ViewResult>(result);
        Assert.Equal(1, await ctx.Vendors.CountAsync());
    }

    [Fact]
    public async Task Service_Create_WithDuplicateVendorCode_IsRejected_ButNullCodesDoNotCollide()
    {
        var (_, ctx, service) = Create(nameof(Service_Create_WithDuplicateVendorCode_IsRejected_ButNullCodesDoNotCollide));
        await service.CreateAsync(new Vendor { Name = "A", VendorCode = "V-1" });
        await service.CreateAsync(new Vendor { Name = "B" });
        await service.CreateAsync(new Vendor { Name = "C", VendorCode = "  " });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new Vendor { Name = "D", VendorCode = "v-1" }));

        Assert.Equal(3, await ctx.Vendors.CountAsync());
    }

    [Fact]
    public async Task Create_WithoutPermission_StoresNothing()
    {
        var (controller, ctx, _) = Create(nameof(Create_WithoutPermission_StoresNothing), allow: false);

        var result = await controller.Create(Forged());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, await ctx.Vendors.CountAsync());
    }

    [Fact]
    public async Task Edit_CannotChangeQualificationOrRisk()
    {
        var (controller, ctx, service) = Create(nameof(Edit_CannotChangeQualificationOrRisk));
        var id = await service.CreateAsync(new Vendor { Name = "Acme" });

        var forgedEdit = Forged("Acme Renamed", "ACM");
        forgedEdit.VendorId = id;
        await controller.Edit(forgedEdit);

        var stored = await ctx.Vendors.SingleAsync(v => v.VendorId == id);
        AssertSanitized(stored);
        Assert.Equal("Acme Renamed", stored.Name);   // فیلدهای قابل‌ویرایش عادی همچنان تغییر می‌کنند
    }
}
