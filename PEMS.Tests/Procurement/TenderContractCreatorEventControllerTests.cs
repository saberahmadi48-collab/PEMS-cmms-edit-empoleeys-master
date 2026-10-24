using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0.3: این تست‌ها رفتار «بدون تراکنش» (InMemory) و پیام‌های کنترلر را می‌سنجند؛ اتمیک‌بودن/Rollback روی SQL Server فقط در
/// ContractCreationAtomicitySqlTests اثبات می‌شود، نه اینجا.
/// P0.1 / R-01 (لایه‌ی کنترلر، موتور مجوز واقعی، دیتابیس InMemory): پیام‌ها دقیق‌اند (Commit شده ≠ ذخیره نشد)، تلاش مجدد توسط کاربر مجاز
/// دیگر رویداد را برای ایجادکننده‌ی اصلی ترمیم می‌کند، رویداد گارد (Tender.Approve) و رویداد ایجادکننده (Contracts.Create) متمایزند، و رد مجوز
/// نه قرارداد می‌سازد نه رویدادی به نام کسی می‌نویسد. InMemory تراکنش/قید یکتا/RowVersion را اعمال نمی‌کند.
/// </summary>
public class TenderContractCreatorEventControllerTests
{
    private const string TenderOnlyRole = "TENDER_ONLY";

    private sealed class FailOnNthSave : SaveChangesInterceptor
    {
        public int Calls;
        public int FailOn;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailOn > 0 && Calls == FailOn) throw new DbUpdateException("injected failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public void Arm(int failOn) { Calls = 0; FailOn = failOn; }
        public void Disarm() { Calls = 0; FailOn = 0; }
    }

    private sealed record Actor(int UserId, string UserName, string Role);

    private sealed record Arrange(
        TenderController Controller, PEMSDbContext Ctx, FailOnNthSave Faults, FakeCurrentUserService User, string Db, int TenderId, int BidId);

    private static PEMSDbContext NewContext(string db, FailOnNthSave? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase(db)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return new PEMSDbContext(builder.Options);
    }

    private static async Task<Actor> AddUserAsync(PEMSDbContext ctx, string name, string role)
    {
        var user = new ApplicationUser { UserName = name, Email = $"{name}@pems.local", FullName = name };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = ctx.Roles.First(r => r.Name == role).Id });
        await ctx.SaveChangesAsync();
        return new Actor(user.Id, name, role);
    }

    private static void ActAs(Arrange a, Actor actor)
    {
        a.User.UserId = actor.UserId;
        a.User.Roles = new[] { actor.Role };
        a.Ctx.CurrentUserName = actor.UserName;       // در برنامه‌ی واقعی Middleware این مقدار را برای هر درخواست تنظیم می‌کند
    }

    private static async Task<(Arrange Arrange, Actor Manager1, Actor Manager2, Actor Unauthorized, Actor TenderOnly)> CreateAsync(string name)
    {
        var db = $"TenderEvtCtrl_{name}";
        var faults = new FailOnNthSave();
        var ctx = NewContext(db, faults);
        foreach (var roleName in SystemRoles.All.Append(TenderOnlyRole))
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();
        ctx.RolePermissions.Add(new RolePermission { RoleName = TenderOnlyRole, ModuleName = "Tender", CanView = true, CanApprove = true, CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        var m1 = await AddUserAsync(ctx, "manager.one", SystemRoles.ContractManager);
        var m2 = await AddUserAsync(ctx, "manager.two", SystemRoles.ContractManager);
        var unauthorized = await AddUserAsync(ctx, "engineer.x", SystemRoles.DisciplineEngineer);
        var tenderOnly = await AddUserAsync(ctx, "tender.only", TenderOnlyRole);

        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();
        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = project.ProjectId, Title = "Pumps", IsActive = true };
        var vendor = new Vendor { Name = "Qualified Vendor", IsActive = true, QualificationStatus = "Qualified", QualificationExpiryDate = DateTime.Now.AddYears(1) };
        ctx.ProcurementPackages.Add(package);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var tenderService = new TenderService(ctx, new FakeNumberingService());
        var tenderId = await tenderService.CreateAsync(package.ProcurementPackageId, "Pump Tender", null, null, false, null, null, null, null);
        await tenderService.InviteVendorAsync(tenderId, vendor.VendorId);
        await tenderService.OpenForBiddingAsync(tenderId);
        var bid = await tenderService.SubmitBidAsync(tenderId, vendor.VendorId, 100000m, "USD", null, null);
        await tenderService.CloseBiddingAsync(tenderId);
        await tenderService.RecordBidEvaluationAsync(bid.BidId!.Value, "Technical", 1, "Engineering", "Passed", null);
        await tenderService.RecordBidEvaluationAsync(bid.BidId!.Value, "Commercial", 1, "Finance", "Passed", null);

        var user = new FakeCurrentUserService { UserId = m1.UserId, Roles = new[] { m1.Role } };
        var controller = new TenderController(tenderService, new ProcurementPackageService(ctx), permissionService, user, ctx, accessControl).WithTempData();
        var arrange = new Arrange(controller, ctx, faults, user, db, tenderId, bid.BidId!.Value);
        ActAs(arrange, m1);
        return (arrange, m1, m2, unauthorized, tenderOnly);
    }

    private static async Task<List<AuthorizationAuditLog>> EventsAsync(string db, string resource, string action)
    {
        using var fresh = NewContext(db);
        return await fresh.AuthorizationAuditLogs.AsNoTracking().Where(l => l.ResourceName == resource && l.ActionName == action).ToListAsync();
    }

    private static async Task<int> ContractCountAsync(string db)
    {
        using var fresh = NewContext(db);
        return await fresh.Contracts.CountAsync();
    }

    // ------------------------------------------------------------------ پیام دقیق: Commit شده ≠ ذخیره نشد
    [Fact]
    public async Task EventFailureAfterCommit_ShowsACommittedMessage_NeverANothingSavedMessage()
    {
        var (a, m1, _, _, _) = await CreateAsync(nameof(EventFailureAfterCommit_ShowsACommittedMessage_NeverANothingSavedMessage));
        Assert.IsType<RedirectToActionResult>(await a.Controller.SelectWinner(a.TenderId, a.BidId));
        // در مسیر کنترلر، رویداد Allow گاردِ Tender.Approve خودش دو ذخیره‌ی فیزیکی (ردیف + AuditLog عمومی) مصرف می‌کند؛ پس: ۳ = قرارداد،
        // ۴ = AuditLog عمومیِ آن، ۵ = ردیف رویداد Contracts.Create
        a.Faults.Arm(failOn: 5);

        var result = await a.Controller.CreateContract(a.TenderId, "CT-C1", null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Contract", redirect.ControllerName);                       // قرارداد وجود دارد؛ به آن هدایت می‌شود
        var message = Assert.IsType<string>(a.Controller.TempData["Error"]);
        Assert.Contains("ساخته و ذخیره شد", message);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", message);
        Assert.Equal(1, await ContractCountAsync(a.Db));
        Assert.Empty(await EventsAsync(a.Db, "Contracts", "Create"));
    }

    [Fact]
    public async Task Retry_ByAnotherAuthorizedUser_RepairsTheEventForTheOriginalCreator()
    {
        var (a, m1, m2, _, _) = await CreateAsync(nameof(Retry_ByAnotherAuthorizedUser_RepairsTheEventForTheOriginalCreator));
        Assert.IsType<RedirectToActionResult>(await a.Controller.SelectWinner(a.TenderId, a.BidId));
        a.Faults.Arm(failOn: 5);                       // ردیف رویداد Contracts.Create (۱-۲ = گارد، ۳ = قرارداد، ۴ = AuditLog عمومی، ۵ = رویداد)
        await a.Controller.CreateContract(a.TenderId, "CT-C2", null, null);
        a.Faults.Disarm();

        ActAs(a, m2);                                                           // کاربر مجاز دیگری تلاش مجدد می‌کند
        var retry = await a.Controller.CreateContract(a.TenderId, "CT-C2", null, null);

        Assert.IsType<RedirectToActionResult>(retry);
        Assert.Contains("ترمیم شد", Assert.IsType<string>(a.Controller.TempData["Message"]));
        Assert.Equal(1, await ContractCountAsync(a.Db));
        var ev = Assert.Single(await EventsAsync(a.Db, "Contracts", "Create"));
        Assert.Equal(m1.UserId, ev.UserId);                                     // ایجادکننده‌ی اصلی، نه کاربر تلاش مجدد
        Assert.NotEqual(m2.UserId, ev.UserId);
    }

    [Fact]
    public async Task Retry_AfterAFullySuccessfulCreation_AddsNoSecondCreatorEvent_AndNoSecondContract()
    {
        var (a, m1, _, _, _) = await CreateAsync(nameof(Retry_AfterAFullySuccessfulCreation_AddsNoSecondCreatorEvent_AndNoSecondContract));
        Assert.IsType<RedirectToActionResult>(await a.Controller.SelectWinner(a.TenderId, a.BidId));
        await a.Controller.CreateContract(a.TenderId, "CT-C3", null, null);

        await a.Controller.CreateContract(a.TenderId, "CT-C3", null, null);
        await a.Controller.CreateContract(a.TenderId, "CT-C3", null, null);

        Assert.Equal(1, await ContractCountAsync(a.Db));
        var ev = Assert.Single(await EventsAsync(a.Db, "Contracts", "Create"));
        Assert.Equal(m1.UserId, ev.UserId);
    }

    // ------------------------------------------------------------------ رویداد گارد و رویداد ایجادکننده متمایزند
    [Fact]
    public async Task GuardEvents_And_CreatorEvents_AreSemanticallyDistinct()
    {
        var (a, m1, _, _, _) = await CreateAsync(nameof(GuardEvents_And_CreatorEvents_AreSemanticallyDistinct));
        await a.Controller.SelectWinner(a.TenderId, a.BidId);
        await a.Controller.CreateContract(a.TenderId, "CT-C4", null, null);

        // (در InMemory شناسه‌ی مناقصه و قرارداد هر دو ۱ است؛ پس تمایز با ResourceName/ActionName سنجیده می‌شود، نه مقدار شناسه)
        using var fresh = NewContext(a.Db);
        var contractId = (await fresh.Contracts.SingleAsync()).ContractId.ToString();
        var all = await fresh.AuthorizationAuditLogs.AsNoTracking().ToListAsync();
        var guardEvents = all.Where(e => e.ResourceName == "Tender" && e.ActionName == "Approve").ToList();
        var creatorEvents = all.Where(e => e.ResourceName == "Contracts" && e.ActionName == "Create").ToList();

        Assert.NotEmpty(guardEvents);                                                              // رویداد گارد: Tender.Approve با شناسه‌ی مناقصه
        Assert.All(guardEvents, e => Assert.Equal(a.TenderId.ToString(), e.ResourceId));
        var creator = Assert.Single(creatorEvents);                                                // رویداد ایجادکننده: Contracts.Create با شناسه‌ی قرارداد
        Assert.Equal(contractId, creator.ResourceId);
        Assert.Equal(m1.UserId, creator.UserId);
        Assert.Empty(guardEvents.Intersect(creatorEvents));                                        // هیچ ردیفی هر دو نقش را ندارد
    }

    // ------------------------------------------------------------------ رد مجوز
    [Fact]
    public async Task Denial_CreatesNoContract_AndWritesNoCreatorEventForAnyone()
    {
        var (a, _, _, unauthorized, tenderOnly) = await CreateAsync(nameof(Denial_CreatesNoContract_AndWritesNoCreatorEventForAnyone));
        Assert.IsType<RedirectToActionResult>(await a.Controller.SelectWinner(a.TenderId, a.BidId));    // M1 برنده را انتخاب می‌کند

        ActAs(a, unauthorized);                                                  // Tender.Approve ندارد
        Assert.IsType<ForbidResult>(await a.Controller.CreateContract(a.TenderId, "CT-C5", null, null));
        ActAs(a, tenderOnly);                                                    // Tender.Approve دارد، Contracts.Create ندارد
        Assert.IsType<ForbidResult>(await a.Controller.CreateContract(a.TenderId, "CT-C5", null, null));

        Assert.Equal(0, await ContractCountAsync(a.Db));
        Assert.Empty(await EventsAsync(a.Db, "Contracts", "Create"));
    }
}
