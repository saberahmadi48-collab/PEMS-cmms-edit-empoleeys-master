using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;
using Microsoft.AspNetCore.Identity;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.SearchProviders;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.PartCatalogue;

/// <summary>گردش‌کار کاتالوگ قطعه روی InMemory: Submit/Approve/Return/Cancel، SoD، گیرنده‌ها، Seed ایدمپوتنت.</summary>
public class PartCatalogueServiceTests
{
    private const int Requester = 1, DirectMgr = 2, Superior = 3, Pool = 10, Outsider = 20;

    private sealed class FakeActions : IRoleModuleActionService
    {
        public HashSet<string> Allowed { get; } = new() { "Create", "Review", "Approve", "Admin", "View" };
        public Task<bool> HasActionAsync(IEnumerable<string> userRoles, string module, string action) =>
            Task.FromResult(module == "PartCatalogue"
                && ((userRoles.Contains("PC") && Allowed.Contains(action)) || (userRoles.Contains("PCV") && action == "View")
                    || (userRoles.Contains("PCC") && action == "Create")));
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private sealed class FakeAuthHook : IWorkflowAuthorizationHook
    {
        private readonly FakeActions _actions;
        public FakeAuthHook(FakeActions actions) => _actions = actions;
        public async Task<WorkflowAuthorizationResult> AuthorizeAsync(WorkflowAuthorizationContext c, CancellationToken ct = default)
        {
            var p = c.Transition.RequiredPermission!;
            var dot = p.IndexOf('.');
            return await _actions.HasActionAsync(c.ActorRoles, p[..dot], p[(dot + 1)..]) ? WorkflowAuthorizationResult.Allow() : WorkflowAuthorizationResult.Deny("denied");
        }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<(int User, string Title)> Sent { get; } = new();
        public List<(int? User, string Type, int Id)> Resolved { get; } = new();
        public Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null)
        { Sent.Add((userId, title)); return Task.CompletedTask; }
        public Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false) => Task.FromResult(new List<NotificationDto>());
        public Task<PagedResult<NotificationDto>> GetInboxAsync(int userId, string? filter, int page, int pageSize) => throw new NotSupportedException();
        public Task<int> GetUnreadCountAsync(int userId) => Task.FromResult(0);
        public Task<bool> MarkAsReadAsync(int userId, int notificationId) => Task.FromResult(true);
        public Task MarkAllAsReadAsync(int userId) => Task.CompletedTask;
        public Task<string?> OpenAsync(int userId, int notificationId) => Task.FromResult<string?>(null);
        public Task<bool> DismissAsync(int userId, int notificationId) => Task.FromResult(true);
        public Task DismissAllAsync(int userId) => Task.CompletedTask;
        public Task MarkRelatedReadAsync(int userId, string relatedType, int relatedId) => Task.CompletedTask;
        public Task ResolveRelatedAsync(int? userId, string relatedType, int relatedId)
        { Resolved.Add((userId, relatedType, relatedId)); return Task.CompletedTask; }
    }

    private sealed class FakeFiles : IFileStorageService
    {
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content) => Task.FromResult(Guid.NewGuid().ToString("N"));
        public Task<Stream> OpenReadAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required PartCatalogueService Svc { get; init; }
        public required FakeNotifications Notes { get; init; }
        public required FakeActions Actions { get; init; }
        public int Tc, G1, G2, Pf, Uom, Mfr, Grade, PlanB, PlanA;
        public PcActor Actor(int emp, int? user = null) => new(emp, user ?? emp + 100, new[] { "PC" });
    }

    private static async Task<Env> NewEnvAsync()
    {
        var db = PemsDbContextFactory.CreateInMemory("pc_" + Guid.NewGuid().ToString("N"));
        await PartCatalogueSeeder.SeedAsync(db, importExcel: false);
        await PartCatalogueWorkflowSeeder.SeedAsync(db);

        foreach (var (id, n) in new[] { (1, "Req"), (2, "Direct"), (3, "Superior"), (10, "Pool"), (11, "Pool2"), (20, "Outsider") })
            db.Employees.Add(new Employee { EmployeeId = id, FirstName = n, LastName = "T" });

        // هر کارمند یک حساب کاربری با نقش «PC» دارد (Resolver مجوز را از نقش‌های حساب کاربری کارمند می‌سنجد)
        var pcRole = new ApplicationRole("PC") { NormalizedName = "PC" };
        db.Roles.Add(pcRole);
        await db.SaveChangesAsync();
        foreach (var id in new[] { 1, 2, 3, 10, 11, 20 })
        {
            var u = new ApplicationUser { UserName = "u" + id, NormalizedUserName = "U" + id, EmployeeId = id, FullName = "u" + id };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = pcRole.Id });
        }
        await db.SaveChangesAsync();

        var tc = new PartCatalogueTechnicalClass { Code = "V-1", Description = "VALVE" };
        var g1 = new PartCatalogueCommodityGroup { Code = "100", Description = "G1" };
        var g2 = new PartCatalogueCommodityGroup { Code = "200", Description = "G2" };
        var pf = new PartCatalogueProductFamily { Code = "PF1", Description = "PF" };
        var uom = new MaterialUOM { Code = "EA", Name = "Each" };
        var mfr = new PartCatalogueManufacturer { Name = "SIEMENS", NormalizedName = "SIEMENS" };
        db.AddRange(tc, g1, g2, pf, uom, mfr);

        await OrgSeed.ChainAsync(db, Requester, DirectMgr, Superior);   // سلسله‌مراتب سازمانی مرکزی (نه جدول محلی ماژول)
        foreach (var key in new[] { "MaintenanceSupervisor", "SparePartsSpecialist", "SourcingUnit", "CommodityGroupReview", "PartCataloguer", "InventoryControl" })
            db.PartCatalogueApproverAssignments.Add(new PartCatalogueApproverAssignment { StepKey = key, EmployeeId = Pool });
        await db.SaveChangesAsync();

        var notes = new FakeNotifications();
        var actions = new FakeActions();
        var resolver = new EnterpriseActorResolver(db, actions);
        var bindings = new WorkflowBindingService(db);
        var runtime = new WorkflowRuntimeService(db, bindings, new WorkflowConditionEvaluator(PartCatalogueWorkflow.Fields), new FakeAuthHook(actions),
            null, null, new ActorResolutionWorkflowHook(resolver), new IWorkflowBusinessContextProvider[] { new PartCatalogueWorkflowContextProvider(db) });
        var svc = new PartCatalogueService(db, new FakeNumberingService(), notes, new FakeFiles(), actions, new PartCatalogueOptions(),
            runtime, runtime, bindings, resolver);
        return new Env
        {
            Db = db, Svc = svc, Notes = notes, Actions = actions,
            Tc = tc.TechnicalClassId, G1 = g1.CommodityGroupId, G2 = g2.CommodityGroupId, Pf = pf.ProductFamilyId,
            Uom = uom.MaterialUOMId, Mfr = mfr.ManufacturerId,
            Grade = db.PartCatalogueStockGrades.First().StockGradeId,
            PlanB = db.PartCataloguePlanningMethods.First(p => p.Code == "B").PlanningMethodId,
            PlanA = db.PartCataloguePlanningMethods.First(p => p.Code == "A").PlanningMethodId
        };
    }

    private static PartCatalogueFormDto Form(Env e, int id, bool planA = false) => new()
    {
        Id = id,
        SectionId = e.Db.PartCatalogueSections.First(s => s.Code == "SERVICES").SectionId,   // بدون بررسی قطعات
        TechnicalClassId = e.Tc, CommodityGroup1Id = e.G1, CommodityGroup2Id = e.G2, ProductFamilyId = e.Pf,
        UnitOfMeasureId = e.Uom, ManufacturerId = e.Mfr, ManufacturerPartNumber = "P-1",
        PlanningMethodId = planA ? e.PlanA : e.PlanB, MinimumStock = planA ? 9 : 2, MaxConsumptionRate = planA ? 9 : 1,
        StockGradeId = e.Grade,
        Attributes = { new PartCatalogueAttributeDto { Name = "FREE FORMAT TEXT", Value = "BALL VALVE" } }
    };

    private static async Task<int> SubmittedAsync(Env e, bool planA = false)
    {
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        var save = await e.Svc.SaveAsync(Form(e, id, planA), e.Actor(Requester));
        Assert.True(save.Success, string.Join(",", save.Result.Errors.Select(x => x.Key + ":" + x.Field)));
        Assert.True((await e.Svc.GenerateNumberAsync(id, null, e.Actor(Requester))).Success);
        var sub = await e.Svc.SubmitAsync(id, null, e.Actor(Requester));
        Assert.True(sub.Success, string.Join(",", sub.Errors.Select(x => x.Key + ":" + x.Field)));
        return id;
    }

    private static async Task<string?> StatusAsync(Env e, int id) =>
        (await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(r => r.PartCatalogueRequestId == id)).Status;

    [Fact]
    public async Task Save_AppliesAutomaticRules_PlanningAZeroesStock_LocalForcesPartNumber()
    {
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        var form = Form(e, id, planA: true);
        Assert.True((await e.Svc.SaveAsync(form, e.Actor(Requester))).Success);
        var r = await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(x => x.PartCatalogueRequestId == id);
        Assert.Equal(0m, r.MinimumStock);
        Assert.Equal(0m, r.MaxConsumptionRate);

        var local = e.Db.PartCatalogueManufacturers.First(m => m.IsLocal);
        form.ManufacturerId = local.ManufacturerId; form.ManufacturerPartNumber = "ignored";
        Assert.True((await e.Svc.SaveAsync(form, e.Actor(Requester))).Success);
        r = await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(x => x.PartCatalogueRequestId == id);
        Assert.Equal("NO_PART_NR", r.ManufacturerPartNumber);
    }

    [Fact]
    public async Task Save_RejectsNonexistentAndInactiveTechnicalClass_ServerSide()
    {
        // نتیجه‌ی تطبیق در مرورگر فقط پیشنهاد است؛ شناسه‌ی ارسالی باید سمت سرور موجود و فعال باشد
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));

        var form = Form(e, id);
        form.TechnicalClassId = 987654;
        var missing = await e.Svc.SaveAsync(form, e.Actor(Requester));
        Assert.False(missing.Success);
        Assert.Contains(missing.Result.Errors, x => x.Field == nameof(form.TechnicalClassId));

        var inactive = new PartCatalogueTechnicalClass { Code = "IR-OLD", Description = "RETIRED CLASS", IsActive = false };
        e.Db.PartCatalogueTechnicalClasses.Add(inactive);
        await e.Db.SaveChangesAsync();
        form.TechnicalClassId = inactive.TechnicalClassId;
        var off = await e.Svc.SaveAsync(form, e.Actor(Requester));
        Assert.False(off.Success);
        Assert.Contains(off.Result.Errors, x => x.Field == nameof(form.TechnicalClassId));
        Assert.Null((await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(r => r.PartCatalogueRequestId == id)).TechnicalClassId);   // nothing persisted
    }

    [Fact]
    public async Task Save_KeepsOriginalFreeFormatText_AndTheSelectedTechnicalClass_ForLaterRetrieval()
    {
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        var form = Form(e, id);
        form.Attributes.Clear();
        const string original = "VALVE,BALL, 2 IN, 150#, A105 (original pasted description)";
        form.Attributes.Add(new PartCatalogueAttributeDto { Name = "FREE FORMAT TEXT", Value = original });
        Assert.True((await e.Svc.SaveAsync(form, e.Actor(Requester))).Success);

        var row = await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(r => r.PartCatalogueRequestId == id);
        Assert.Equal(e.Tc, row.TechnicalClassId);
        var fft = await e.Db.PartCatalogueRequestAttributes.AsNoTracking().SingleAsync(a => a.PartCatalogueRequestId == id && a.Name == "FREE FORMAT TEXT");
        Assert.Equal(original, fft.Value);                                                  // the description is never rewritten by the classification
    }

    [Fact]
    public async Task Submit_RequiresRequestNumber_AndOnlyOwnerCanSave()
    {
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        Assert.True((await e.Svc.SaveAsync(Form(e, id), e.Actor(Requester))).Success);

        var noNumber = await e.Svc.SubmitAsync(id, null, e.Actor(Requester));
        Assert.Contains(noNumber.Errors, x => x.Key == "Pc_Err_NumberRequired");

        var other = await e.Svc.SaveAsync(Form(e, id), e.Actor(Outsider));
        Assert.False(other.Success);
    }

    [Fact]
    public async Task Number_IsGeneratedOnlyOnce()
    {
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        Assert.True((await e.Svc.GenerateNumberAsync(id, null, e.Actor(Requester))).Success);
        var again = await e.Svc.GenerateNumberAsync(id, null, e.Actor(Requester));
        Assert.Contains(again.Errors, x => x.Key == "Pc_Err_NumberExists");
    }

    [Fact]
    public async Task FullChain_ApprovedByRightPeople_CompletesWithPartNumber_AndRequesterCannotApproveOwn()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);

        Assert.Equal("InApproval", await StatusAsync(e, id));
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Requester))).Success);   // SoD
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Outsider))).Success);   // نه گیرنده، نه Admin ← override نیاز به Admin دارد
        // (Outsider همین نقش PC دارد که Admin هم می‌دهد؛ بنابراین بدون توضیح رد می‌شود)

        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Superior))).Success);

        // گام‌های Pool: MaintenanceSupervisor (فقط Critical/Capital) و Sourcing/Spare رد شده‌اند ⇒ CommodityGroupReview
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool))).Success);   // CommodityGroupReview

        // گام ثبت: شماره‌ی قطعه الزامی
        var noPn = await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool));
        Assert.Contains(noPn.Errors, x => x.Key == "Pc_Err_PartNumberRequired");

        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, "PN-1000", null, e.Actor(Pool))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool))).Success);   // Inventory Control

        Assert.Equal("Completed", await StatusAsync(e, id));
        var r = await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(x => x.PartCatalogueRequestId == id);
        Assert.Equal("PN-1000", r.AssignedPartNumber);
        Assert.True(await e.Db.PartCatalogueRequestApprovals.CountAsync(a => a.PartCatalogueRequestId == id) >= 6);  // Submit + ۵ گام فعال
    }

    [Fact]
    public async Task PlanningA_Route_HasNoInventoryControlStep()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e, planA: true);
        var step = await e.Db.PartCatalogueRequestSteps.AsNoTracking().FirstAsync(s => s.PartCatalogueRequestId == id && s.StepKey == "InventoryControl");
        Assert.Equal("Skipped", step.Status);
    }

    [Fact]
    public async Task Return_RequiresComment_ThenOwnerCanEditAndResubmit()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);

        var noComment = await e.Svc.ExecuteAsync(id, "Return", null, null, null, e.Actor(DirectMgr));
        Assert.Contains(noComment.Errors, x => x.Key == "Pc_Err_CommentRequired");

        Assert.True((await e.Svc.ExecuteAsync(id, "Return", "fix data", null, null, e.Actor(DirectMgr))).Success);
        Assert.Equal("Returned", await StatusAsync(e, id));

        Assert.True((await e.Svc.SaveAsync(Form(e, id), e.Actor(Requester))).Success);
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        Assert.Equal("InApproval", await StatusAsync(e, id));
    }

    [Fact]
    public async Task Cancel_OnlyTheRequesterMay_NotTheStepApproverNorAdmin()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);

        // همه‌ی Actorهای تست نقش «PC» دارند که PartCatalogue.Admin را هم شامل می‌شود؛ DirectMgr گیرنده‌ی گام جاری هم هست.
        // با این حال هیچ‌کدام (با توضیح یا بدون آن) نمی‌توانند لغو کنند - لغو فقط برای درخواست‌دهنده است.
        foreach (var other in new[] { DirectMgr, Superior, Pool, Outsider })
            foreach (var comment in new string?[] { null, "reason" })
            {
                var denied = await e.Svc.ExecuteAsync(id, "Cancel", comment, null, null, e.Actor(other));
                Assert.False(denied.Success);
                Assert.Contains(denied.Errors, x => x.Key == "Pc_Err_NotAuthorized");
            }
        Assert.Equal("InApproval", await StatusAsync(e, id));
        Assert.False(await e.Db.PartCatalogueRequestApprovals.AnyAsync(a => a.PartCatalogueRequestId == id && (a.Action == "Cancel" || a.Action == "AdminOverride")));

        // دکمه‌ی لغو فقط برای درخواست‌دهنده؛ تأیید/بازگشت برای گیرنده‌ی گام دست‌نخورده می‌ماند
        var approverView = (await e.Svc.GetDetailsAsync(id, e.Actor(DirectMgr)))!;
        Assert.False(approverView.Actions.CanCancel);
        Assert.True(approverView.Actions.CanApprove);
        Assert.True(approverView.Actions.CanReturn);
        Assert.True((await e.Svc.GetDetailsAsync(id, e.Actor(Requester)))!.Actions.CanCancel);

        // درخواست‌دهنده بدون توضیح هم می‌تواند لغو کند و ثبت تاریخچه‌ی عادی Cancel است (نه AdminOverride)
        Assert.True((await e.Svc.ExecuteAsync(id, "Cancel", null, null, null, e.Actor(Requester))).Success);
        Assert.Equal("Cancelled", await StatusAsync(e, id));
        Assert.True(await e.Db.PartCatalogueRequestApprovals.AnyAsync(a => a.PartCatalogueRequestId == id && a.Action == "Cancel"));
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
    }

    [Fact]
    public async Task Cancel_Draft_IsAllowedForTheRequesterOnly()
    {
        var e = await NewEnvAsync();
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));

        Assert.False((await e.Svc.ExecuteAsync(id, "Cancel", "reason", null, null, e.Actor(Outsider))).Success);
        Assert.Equal("Draft", await StatusAsync(e, id));
        Assert.True((await e.Svc.GetDetailsAsync(id, e.Actor(Requester)))!.Actions.CanCancel);
        Assert.False((await e.Svc.GetDetailsAsync(id, e.Actor(Outsider)))?.Actions.CanCancel ?? false);

        Assert.True((await e.Svc.ExecuteAsync(id, "Cancel", null, null, null, e.Actor(Requester))).Success);
        Assert.Equal("Cancelled", await StatusAsync(e, id));
    }

    [Fact]
    public async Task Details_ShowsTheTechnicalClassDefinition_OnlyWhileTheRequestIsDraft()
    {
        const string definition = "A circular or moderately noncircular shaped sealing material";
        var e = await NewEnvAsync();
        (await e.Db.PartCatalogueTechnicalClasses.FindAsync(e.Tc))!.LongDescription = definition;
        await e.Db.SaveChangesAsync();

        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        Assert.True((await e.Svc.SaveAsync(Form(e, id), e.Actor(Requester))).Success);
        Assert.True((await e.Svc.GenerateNumberAsync(id, null, e.Actor(Requester))).Success);

        var host = new PEMS.Web.Areas.Warehouse.Controllers.PartCatalogueAdminController(null!, null!, null!, null!, null!, null!);
        async Task<string> RenderAsync()
        {
            var dto = (await e.Svc.GetDetailsAsync(id, e.Actor(Requester)))!;
            var result = new Microsoft.AspNetCore.Mvc.ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(host.ViewData) { Model = dto } };
            return await RazorViewRenderer.RenderAsync(host, result, "~/Areas/Warehouse/Views/PartCatalogueRequest/Details.cshtml");
        }

        Assert.Equal("Draft", await StatusAsync(e, id));
        Assert.Contains(definition, await RenderAsync());

        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        Assert.Equal("InApproval", await StatusAsync(e, id));
        var submitted = await RenderAsync();
        Assert.DoesNotContain(definition, submitted);
        Assert.Contains("V-1", submitted);   // the class itself is still shown; only its long definition is hidden
    }

    [Fact]
    public async Task MissingDirectManager_IsReceivedAsOrganizationResolutionRequired_ThenRepairedRetriedAndApproved()
    {
        var e = await NewEnvAsync();
        // ساختار ناقص: سمت درخواست‌دهنده ریشه می‌شود (مدیر مستقیم ندارد). هیچ Admin/CEO/Department جایگزین نمی‌شود.
        var reqPos = await e.Db.EmployeeAssignments.Where(a => a.EmployeeId == Requester).Select(a => a.PositionId).SingleAsync();
        var original = (await e.Db.Positions.FindAsync(reqPos))!.ParentPositionId;
        (await e.Db.Positions.FindAsync(reqPos))!.ParentPositionId = null;
        await e.Db.SaveChangesAsync();

        var id = await DraftWithNumberAsync(e);
        var r = await e.Svc.SubmitAsync(id, null, e.Actor(Requester));
        Assert.True(r.Success);                                   // درخواست رد/نابود نشد
        Assert.Equal("OrgResolution", await StatusAsync(e, id));
        var req = await e.Db.PartCatalogueRequests.AsNoTracking().SingleAsync(x => x.PartCatalogueRequestId == id);
        Assert.Equal("ROOT_POSITION_NO_PARENT", req.RoutingBlockReason);
        Assert.Null(req.WorkflowInstanceId);                       // موتور گردش‌کار شروع نشده؛ تأیید خودکار هم نیست
        Assert.Contains(await e.Db.PartCatalogueRequestApprovals.AsNoTracking().ToListAsync(), a => a.Action == "OrgResolutionRequired");

        // تا قبل از اصلاح: Retry هنوز مسدود، هیچ‌کس نمی‌تواند تأیید کند (حتی Admin/Pool)
        Assert.False((await e.Svc.RetryRoutingAsync(id, null, e.Actor(Requester))).Success);
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool))).Success);
        Assert.Equal("OrgResolution", await StatusAsync(e, id));
        // فقط درخواست‌دهنده/Admin ماژول می‌توانند Retry کنند
        Assert.False((await e.Svc.RetryRoutingAsync(id, null, e.Actor(Outsider))).Success);

        // مدیر سازمان ساختار را اصلاح می‌کند
        (await e.Db.Positions.FindAsync(reqPos))!.ParentPositionId = original;
        await e.Db.SaveChangesAsync();

        Assert.True((await e.Svc.RetryRoutingAsync(id, null, e.Actor(Requester))).Success);
        Assert.Equal("InApproval", await StatusAsync(e, id));
        var after = await e.Db.PartCatalogueRequests.AsNoTracking().SingleAsync(x => x.PartCatalogueRequestId == id);
        Assert.NotNull(after.WorkflowInstanceId);
        Assert.Null(after.RoutingBlockReason);
        Assert.Contains(await e.Db.PartCatalogueRequestApprovals.AsNoTracking().ToListAsync(), a => a.Action == "RetryRouting");

        // احراز اختیار عادی همچنان برقرار است: فقط مدیر مستقیم (حل‌شده) می‌تواند تأیید کند
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Outsider))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
    }

    [Fact]
    public async Task OrganizationResolutionRequired_RequestCanBeCancelledByRequester()
    {
        var e = await NewEnvAsync();
        e.Db.EmployeeAssignments.RemoveRange(e.Db.EmployeeAssignments.Where(a => a.EmployeeId == Requester));
        await e.Db.SaveChangesAsync();
        var id = await DraftWithNumberAsync(e);
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        Assert.Equal("OrgResolution", await StatusAsync(e, id));
        Assert.True((await e.Svc.ExecuteAsync(id, "Cancel", "not needed", null, null, e.Actor(Requester))).Success);
        Assert.Equal("Cancelled", await StatusAsync(e, id));
    }

    [Fact]
    public async Task WithoutCreatePermission_DraftCannotBeCreated()
    {
        var e = await NewEnvAsync();
        e.Actions.Allowed.Remove("Create");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => e.Svc.CreateDraftAsync("Add", e.Actor(Requester)));
    }

    [Fact]
    public async Task Seeder_IsIdempotent_AndDoesNotOverwriteAdminChanges()
    {
        var e = await NewEnvAsync();
        var steps = await e.Db.PartCatalogueWorkflowSteps.CountAsync();
        var sections = await e.Db.PartCatalogueSections.CountAsync();
        Assert.Equal(8, steps);
        Assert.Equal(23, sections);

        var step = await e.Db.PartCatalogueWorkflowSteps.FirstAsync(s => s.StepKey == "SourcingUnit");
        step.StepOrder = 55; step.IsActive = false;
        await e.Db.SaveChangesAsync();

        await PartCatalogueSeeder.SeedAsync(e.Db, importExcel: false);
        await PartCatalogueSeeder.SeedAsync(e.Db, importExcel: false);

        Assert.Equal(steps, await e.Db.PartCatalogueWorkflowSteps.CountAsync());
        Assert.Equal(sections, await e.Db.PartCatalogueSections.CountAsync());
        var again = await e.Db.PartCatalogueWorkflowSteps.AsNoTracking().FirstAsync(s => s.StepKey == "SourcingUnit");
        Assert.Equal(55, again.StepOrder);
        Assert.False(again.IsActive);
    }

    [Fact]
    public async Task GlobalSearch_ScopesResultsByPermissionAndOwnership()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);
        var number = (await e.Db.PartCatalogueRequests.AsNoTracking().FirstAsync(r => r.PartCatalogueRequestId == id)).RequestNumber!;

        async Task<int> Count(int emp, params string[] roles)
        {
            var user = new FakeCurrentUserContext { UserId = emp + 100, EmployeeId = emp, Roles = roles };
            var provider = new PartCatalogueRequestSearchProvider(e.Db, user, e.Actions);
            return (await provider.SearchAsync(number, 1, 20, null)).TotalCount;
        }

        Assert.Equal(1, await Count(Pool, "PC"));        // Review/Approve/Admin ⇒ همه
        Assert.Equal(0, await Count(Outsider, "NONE"));  // بدون مجوز
        Assert.Equal(1, await Count(Requester, "PCV"));  // View + مالک
        Assert.Equal(0, await Count(Outsider, "PCV"));   // View ولی نه مالک و نه گیرنده
        Assert.Equal(1, await Count(DirectMgr, "PCV"));  // View + گیرنده‌ی گام
    }

    // ------------------------------------------------------------------ Enterprise Actor Resolution / Workflow integration

    private async Task<int> DraftWithNumberAsync(Env e)
    {
        var id = await e.Svc.CreateDraftAsync("Add", e.Actor(Requester));
        Assert.True((await e.Svc.SaveAsync(Form(e, id), e.Actor(Requester))).Success);
        Assert.True((await e.Svc.GenerateNumberAsync(id, null, e.Actor(Requester))).Success);
        return id;
    }

    [Fact]
    public async Task RequesterOnlyPool_IsDiagnosed_SafeMessageForUsers_DetailedStateForAdmins()
    {
        var e = await NewEnvAsync();
        e.Db.PartCatalogueApproverAssignments.RemoveRange(e.Db.PartCatalogueApproverAssignments.Where(a => a.StepKey == "CommodityGroupReview"));
        e.Db.PartCatalogueApproverAssignments.Add(new PartCatalogueApproverAssignment { StepKey = "CommodityGroupReview", EmployeeId = Requester });
        await e.Db.SaveChangesAsync();
        var id = await DraftWithNumberAsync(e);

        var user = await e.Svc.SubmitAsync(id, null, new PcActor(Requester, 101, new[] { "PCC" }));      // فقط Create
        var userMsg = Assert.Single(user.Errors);
        Assert.Equal("Pc_Route_NoEligibleApprover", userMsg.Key);
        Assert.Equal("Pc_Step_CommodityGroupReview", userMsg.Arg);

        var admin = await e.Svc.SubmitAsync(id, null, e.Actor(Requester));                                   // Admin
        Assert.Equal("Pc_Route_State_REQUESTER_ONLY", Assert.Single(admin.Errors).Key);
        Assert.Equal("Draft", await StatusAsync(e, id));
        Assert.Empty(await e.Db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task CommodityGroupReview_ResolvesByCommodityMapping_WrongCommodityReviewerIsDenied()
    {
        var e = await NewEnvAsync();
        var posA = new Position { Title = "Reviewer for G1", IsActive = true };
        var posB = new Position { Title = "Reviewer for G2", IsActive = true };
        e.Db.Positions.AddRange(posA, posB);
        await e.Db.SaveChangesAsync();
        e.Db.EmployeeAssignments.AddRange(
            new EmployeeAssignment { PositionId = posA.PositionId, EmployeeId = Pool, IsActive = true, StartDate = DateTime.Now.AddDays(-1) },
            new EmployeeAssignment { PositionId = posB.PositionId, EmployeeId = Outsider, IsActive = true, StartDate = DateTime.Now.AddDays(-1) });
        e.Db.CommodityGroupResponsibles.AddRange(
            new CommodityGroupResponsible { ResponsibilityKey = "CommodityGroupReview", CommodityGroupId = e.G1, PositionId = posA.PositionId },
            new CommodityGroupResponsible { ResponsibilityKey = "CommodityGroupReview", CommodityGroupId = e.G2, PositionId = posB.PositionId });
        var resp = await e.Db.WorkflowActorResponsibilities.SingleAsync(r => r.ResponsibilityKey == "CommodityGroupReview");
        resp.Kind = ActorResponsibilityKinds.CommodityPosition;
        await e.Db.SaveChangesAsync();

        var id = await DraftWithNumberAsync(e);                // Group1 = G1
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Superior))).Success);

        // مرحله‌ی CommodityGroupReview: فقط مسئول G1
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Outsider))).Success);   // مسئول G2 (Commodity اشتباه)
        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(11))).Success);          // نقش درست ولی بی‌ارتباط
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool))).Success);
    }

    [Fact]
    public async Task EligibleActors_AreSnapshotted_AtStageEntry_LaterReassignmentDoesNotChangeWhoMayApprove()
    {
        var e = await NewEnvAsync();
        var id = await DraftWithNumberAsync(e);
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);

        // پس از ثبت، مدیر مستقیم عوض می‌شود - مرحله‌ی جاری همچنان فقط برای مدیر زمان ورود است
        var directPos = await e.Db.EmployeeAssignments.Where(a => a.EmployeeId == DirectMgr).Select(a => a.PositionId).SingleAsync();
        var direct = await e.Db.EmployeeAssignments.SingleAsync(a => a.EmployeeId == DirectMgr);
        direct.IsActive = false; direct.EndDate = DateTime.Now;
        e.Db.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = directPos, EmployeeId = Outsider, IsActive = true, IsPrimary = true, StartDate = DateTime.Now.AddDays(-1) });
        await e.Db.SaveChangesAsync();

        Assert.False((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Outsider))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);

        var snapshot = await e.Db.WorkflowInstanceStageCandidates.AsNoTracking().Where(c => c.InstanceStage!.Status == "Approve").ToListAsync();
        Assert.Equal(DirectMgr, Assert.Single(snapshot).EmployeeId);   // تاریخچه‌ی مجاز بودن تغییر نمی‌کند
        Assert.Contains("DirectManager", (await e.Db.WorkflowInstanceStages.AsNoTracking().Include(x => x.Candidates).FirstAsync()).Candidates.Single().Reason);
    }

    [Fact]
    public async Task SecondApprovalOnAnAlreadyDecidedStage_IsRejected_NoDoubleAdvance()
    {
        var e = await NewEnvAsync();
        e.Db.PartCatalogueApproverAssignments.Add(new PartCatalogueApproverAssignment { StepKey = "CommodityGroupReview", EmployeeId = 11 });
        await e.Db.SaveChangesAsync();
        var id = await DraftWithNumberAsync(e);
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(DirectMgr))).Success);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Superior))).Success);

        // هر دو در صف CommodityGroupReview هستند؛ اولی تأیید می‌کند، دومی نباید مرحله‌ی بعدی را دوباره جلو ببرد
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(Pool))).Success);
        var second = await e.Svc.ExecuteAsync(id, "Approve", null, null, null, e.Actor(11));
        Assert.False(second.Success);
        Assert.Equal("PartCataloguer", (await e.Db.PartCatalogueRequests.AsNoTracking().SingleAsync()).CurrentStepKey);
        Assert.Equal(1, await e.Db.WorkflowInstanceStages.CountAsync(s => s.Status == "Approve" && s.Stage!.Name == "CommodityGroupReview"));
    }

    [Fact]
    public async Task Return_EndsWorkflowInstance_AndResubmitStartsANewOne()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);
        var first = (await e.Db.PartCatalogueRequests.AsNoTracking().SingleAsync()).WorkflowInstanceId;
        Assert.True((await e.Svc.ExecuteAsync(id, "Return", "fix", null, null, e.Actor(DirectMgr))).Success);
        Assert.Equal("Returned", (await e.Db.WorkflowInstances.AsNoTracking().SingleAsync()).Status);

        Assert.True((await e.Svc.SaveAsync(Form(e, id), e.Actor(Requester))).Success);
        Assert.True((await e.Svc.SubmitAsync(id, null, e.Actor(Requester))).Success);
        var second = (await e.Db.PartCatalogueRequests.AsNoTracking().SingleAsync()).WorkflowInstanceId;
        Assert.NotEqual(first, second);
        Assert.Equal(2, await e.Db.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task ApprovalHistory_RecordsActor_AndWorkflowHistoryIsWritten()
    {
        var e = await NewEnvAsync();
        var id = await SubmittedAsync(e);
        Assert.True((await e.Svc.ExecuteAsync(id, "Approve", "ok", null, null, e.Actor(DirectMgr))).Success);

        var approval = await e.Db.PartCatalogueRequestApprovals.AsNoTracking().OrderBy(a => a.RequestApprovalId).LastAsync();
        Assert.Equal(DirectMgr, approval.ActorEmployeeId);
        Assert.Equal("DirectManager", approval.StepKey);
        Assert.Contains(await e.Db.WorkflowHistories.AsNoTracking().ToListAsync(), h => h.ActionType == "Approve" && h.UserId == DirectMgr && h.Comment == "ok");
    }

    [Fact]
    public async Task WorkflowAndResponsibilitiesAreSeeded_Structurally_WithoutFakePeople()
    {
        var e = await NewEnvAsync();
        Assert.Equal(1, await e.Db.WorkflowDefinitions.CountAsync(d => d.Module == "PartCatalogue"));
        Assert.Equal(8, await e.Db.WorkflowActorResponsibilities.CountAsync());
        // فقط همان سه سمتِ زنجیره‌ی تست (OrgSeed) وجود دارد؛ Seeder هیچ سمت/تخصیص/کارمندی نمی‌سازد
        Assert.Equal(3, await e.Db.Positions.CountAsync());
        Assert.Equal(3, await e.Db.EmployeeAssignments.CountAsync());

        await PartCatalogueWorkflowSeeder.SeedAsync(e.Db);        // Idempotent
        Assert.Equal(1, await e.Db.WorkflowDefinitions.CountAsync(d => d.Module == "PartCatalogue"));
        Assert.Equal(8, await e.Db.WorkflowActorResponsibilities.CountAsync());
        Assert.All(await e.Db.WorkflowStages.Where(st => st.ResponsibilityKey != null).ToListAsync(), st => Assert.Equal(st.Name, st.ResponsibilityKey));
    }
}
