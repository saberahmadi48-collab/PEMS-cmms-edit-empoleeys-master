using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Services;
using PEMS.Tests.Workflow;
using PEMS.Web.Controllers;
using PEMS.Web.Helpers;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.MyWork;

/// <summary>
/// «کارهای من»: کارتابل مشترک گردش‌کار (GetPendingTasksAsync) + سرویس MyWorkService + لینک رکورد + کنترلر.
/// تمرکز: پیش‌فیلتر SQL نتیجه را عوض نکند (به‌خصوص تطبیق زیررشته‌ای نقش)، سررسید SLA درست محاسبه شود، شمارنده‌ها کل کارتابل را نشان دهند،
/// هویت فقط از Principal سمت سرور بیاید و اعلان‌ها هیچ‌وقت مرحله‌ای را انجام ندهند.
/// </summary>
public class MyWorkInboxTests : WorkflowPlatformTestBase
{
    public MyWorkInboxTests() { Catalog.Roles.Add("Approver"); Catalog.Roles.Add("Auditor"); }

    private async Task<(int Instance, WorkflowStage Stage)> StartSingleStageAsync(string module, string roles, int? timeoutHours, int businessId, int initiator = 1)
    {
        var def = await Defs.CreateDefinitionAsync("Inbox " + module, "Std", module, null, Actor);
        var v = await Defs.CreateDraftVersionAsync(def.WorkflowDefinitionId, Actor);
        var s = await Defs.AddStageAsync(v.WorkflowVersionId,
            new WorkflowStageInput { Name = "Review " + module, StageOrder = 1, ResponsibleRole = roles, TimeoutHours = timeoutHours }, Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(null, s.WorkflowStageId, "Start"), Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(s.WorkflowStageId, null, "Approve"), Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(s.WorkflowStageId, null, "Reject"), Actor);
        var r = await Defs.ValidateVersionAsync(v.WorkflowVersionId, Actor);
        Assert.True(r.IsValid, string.Join("; ", r.Errors.Select(e => e.Code)));
        await Defs.PublishVersionAsync(v.WorkflowVersionId, Actor);
        var id = await Runtime.StartInstanceAsync(module, "Doc", businessId, "Std", null, initiator);
        return (id, s);
    }

    private async Task SetAssignedDateAsync(int instanceId, DateTime when)
    {
        foreach (var st in Ctx.WorkflowInstanceStages.Where(x => x.WorkflowInstanceId == instanceId)) st.AssignedDate = when;
        await Ctx.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- pending tasks (prefilter must not change semantics)

    [Fact]
    public async Task RoleStage_IsVisibleOnlyToExactRoleMatch_NotToSubstringOrOtherRoles()
    {
        await StartSingleStageAsync("ModA", "Approver,Auditor", null, 11);

        Assert.Single(await Runtime.GetPendingTasksAsync(5, new[] { "Approver" }));
        Assert.Single(await Runtime.GetPendingTasksAsync(5, new[] { "Auditor" }));
        Assert.Single(await Runtime.GetPendingTasksAsync(5, new[] { "Other", "Auditor" }));

        // «Appro» و «Aud» زیررشته‌ی نقش‌اند: پیش‌فیلتر SQL آن‌ها را رد نمی‌کند، تطبیق دقیق باید رد کند
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "Appro" }));
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "Aud" }));
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "Approver,Auditor" }));   // یک نقش واحدِ شامل ویرگول، با هیچ‌کدام از دو نقش یکی نیست
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "Manager" }));
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, Array.Empty<string>()));
        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "", "  " }));
    }

    [Fact]
    public async Task OnlyRunningInstances_AndPendingStages_AreListed()
    {
        var (id, _) = await StartSingleStageAsync("ModB", "Approver", null, 21);
        Assert.Single(await Runtime.GetPendingTasksAsync(5, new[] { "Approver" }));

        await Runtime.ExecuteActionAsync(id, "Approve", 9, new[] { "Approver" }, null, null, null);

        Assert.Empty(await Runtime.GetPendingTasksAsync(5, new[] { "Approver" }));
    }

    [Fact]
    public async Task DueDate_IsAssignedPlusTimeout_AndOverdueFlagFollowsClock()
    {
        var (idLate, _) = await StartSingleStageAsync("SlaLate", "Approver", 4, 31);
        var (idOk, _) = await StartSingleStageAsync("SlaOk", "Approver", 48, 32);
        var (idNone, _) = await StartSingleStageAsync("SlaNone", "Approver", null, 33);

        var late = DateTime.Now.AddHours(-10);
        await SetAssignedDateAsync(idLate, late);

        var tasks = (await Runtime.GetPendingTasksAsync(5, new[] { "Approver" })).ToDictionary(t => t.Module);

        Assert.Equal(late.AddHours(4), tasks["SlaLate"].DueDate);
        Assert.True(tasks["SlaLate"].IsOverdue);
        Assert.False(tasks["SlaOk"].IsOverdue);
        Assert.NotNull(tasks["SlaOk"].DueDate);
        Assert.Null(tasks["SlaNone"].DueDate);
        Assert.False(tasks["SlaNone"].IsOverdue);
    }

    // ---------------------------------------------------------------- MyWorkService

    [Fact]
    public async Task Service_SortsOverdueFirst_FiltersByModule_AndSummaryCountsWholeInbox()
    {
        var (idLate, _) = await StartSingleStageAsync("SlaLate", "Approver", 4, 41);
        await StartSingleStageAsync("SlaOk", "Approver", 48, 42);
        await StartSingleStageAsync("SlaNone", "Approver", null, 43);
        await SetAssignedDateAsync(idLate, DateTime.Now.AddHours(-10));
        var svc = new MyWorkService(Runtime);

        var all = await svc.GetApprovalsAsync(5, new[] { "Approver" }, null, false, false, 1, 50);
        Assert.Equal("SlaLate", all.Page.Items[0].Module);                      // عقب‌افتاده اول
        Assert.Equal(3, all.Summary.Total);
        Assert.Equal(1, all.Summary.Overdue);

        var one = await svc.GetApprovalsAsync(5, new[] { "Approver" }, "slaok", false, false, 1, 50);   // فیلتر بی‌تفاوت به حروف
        Assert.Single(one.Page.Items);
        Assert.Equal(3, one.Summary.Total);                                       // شمارنده‌ها به فیلتر وابسته نیستند
        Assert.Equal(3, one.Summary.ByModule.Count);

        var overdue = await svc.GetApprovalsAsync(5, new[] { "Approver" }, null, true, false, 1, 50);
        Assert.Single(overdue.Page.Items);
        Assert.True(overdue.Page.Items[0].IsOverdue);
    }

    [Fact]
    public async Task Service_IsPaged_AndPageSizeIsNormalized()
    {
        for (var i = 0; i < 23; i++) await StartSingleStageAsync("Bulk" + i, "Approver", null, 100 + i);
        var svc = new MyWorkService(Runtime);

        var p1 = await svc.GetApprovalsAsync(5, new[] { "Approver" }, null, false, false, 1, 20);
        var p2 = await svc.GetApprovalsAsync(5, new[] { "Approver" }, null, false, false, 2, 20);
        var weird = await svc.GetApprovalsAsync(5, new[] { "Approver" }, null, false, false, -4, 7);   // اندازه‌ی غیرمجاز → پیش‌فرض، صفحه‌ی نامعتبر → ۱

        Assert.Equal(20, p1.Page.Items.Count);
        Assert.Equal(3, p2.Page.Items.Count);
        Assert.Equal(23, p1.Page.TotalCount);
        Assert.Equal(1, weird.Page.Page);
        Assert.Equal(PEMS.Application.Common.DataGridPageSize.Default, weird.Page.PageSize);
        Assert.Empty(p1.Page.Items.Select(x => x.WorkflowInstanceStageId).Intersect(p2.Page.Items.Select(x => x.WorkflowInstanceStageId)));
    }

    [Fact]
    public async Task Service_OtherUsersWithoutTheRole_SeeNothing()
    {
        await StartSingleStageAsync("ModC", "Approver", null, 51);
        var svc = new MyWorkService(Runtime);

        var outsider = await svc.GetApprovalsAsync(77, new[] { "Viewer" }, null, false, false, 1, 50);

        Assert.Empty(outsider.Page.Items);
        Assert.Equal(0, outsider.Summary.Total);
    }


    // ---------------------------------------------------------------- personal assignment vs role queue

    private sealed class SeatHook : IWorkflowActorResolutionHook
    {
        public Task<WorkflowActorResolutionOutcome> ResolveAsync(WorkflowActorResolutionContext c, CancellationToken ct = default) =>
            Task.FromResult(new WorkflowActorResolutionOutcome(WorkflowActorResolutionKind.Resolved, "RESOLVED", new[] { new WorkflowActorCandidate(10, "seat") }));
    }

    [Fact]
    public async Task PersonalAssignment_IsFlagged_SortedBeforeRoleQueue_AndFilterable()
    {
        // یک مرحله‌ی صف نقش (قدیمی‌تر) + یک مرحله‌ی مبتنی بر مسئولیت که کارمند ۱۰ را انتخاب می‌کند (جدیدتر)
        var rt = new WorkflowRuntimeService(Ctx, Bindings, Conditions, Hook, Sink, Registry, new SeatHook(), Array.Empty<IWorkflowBusinessContextProvider>());
        var (idQueue, _) = await StartSingleStageAsync("QueueMod", "Approver", null, 61);
        await SetAssignedDateAsync(idQueue, DateTime.Now.AddDays(-30));

        var def = await Defs.CreateDefinitionAsync("Seat", "Std", "SeatMod", null, Actor);
        var v = await Defs.CreateDraftVersionAsync(def.WorkflowDefinitionId, Actor);
        var st = await Defs.AddStageAsync(v.WorkflowVersionId, new WorkflowStageInput { Name = "SeatStage", StageOrder = 1, ResponsibilityKey = "R_Seat" }, Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(null, st.WorkflowStageId, "Start"), Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(st.WorkflowStageId, null, "Approve"), Actor);
        await Defs.AddTransitionAsync(v.WorkflowVersionId, Tr(st.WorkflowStageId, null, "Reject"), Actor);
        Assert.True((await Defs.ValidateVersionAsync(v.WorkflowVersionId, Actor)).IsValid);
        await Defs.PublishVersionAsync(v.WorkflowVersionId, Actor);
        await rt.StartInstanceAsync("SeatMod", "Doc", 62, "Std", null, 1);

        var svc = new MyWorkService(rt);
        var roles = new[] { "Approver" };

        var all = await svc.GetApprovalsAsync(10, roles, null, false, false, 1, 50);
        Assert.Equal(2, all.Summary.Total);
        Assert.Equal(1, all.Summary.Personal);
        Assert.Equal("SeatMod", all.Page.Items[0].Module);          // شخصی اول، با اینکه جدیدتر است
        Assert.True(all.Page.Items[0].IsPersonal);
        Assert.False(all.Page.Items[1].IsPersonal);

        var mine = await svc.GetApprovalsAsync(10, roles, null, false, true, 1, 50);
        Assert.Single(mine.Page.Items);
        Assert.True(mine.PersonalOnly);
        Assert.Equal(2, mine.Summary.Total);                        // شمارنده‌ها همچنان کل کارتابل

        // کارمند دیگر (غیر از کاندیدای انتخاب‌شده) با همان نقش: فقط صف نقش را می‌بیند، کار شخصیِ دیگری را نه
        var other = await svc.GetApprovalsAsync(11, roles, null, false, false, 1, 50);
        Assert.Single(other.Page.Items);
        Assert.Equal("QueueMod", other.Page.Items[0].Module);
        Assert.Equal(0, other.Summary.Personal);
    }


    // ---------------------------------------------------------------- SLA awareness (IsOverdue is only meaningful when a time limit is configured)

    [Fact]
    public async Task Summary_WithSla_IsZero_WhenNoStageHasATimeLimit_SoOverdueCannotBeImplied()
    {
        await StartSingleStageAsync("NoSlaA", "Approver", null, 71);
        await StartSingleStageAsync("NoSlaB", "Approver", null, 72);

        var s = await new MyWorkService(Runtime).GetSummaryAsync(5, new[] { "Approver" });

        Assert.Equal(2, s.Total);
        Assert.Equal(0, s.WithSla);
        Assert.Equal(0, s.Overdue);
    }

    [Fact]
    public async Task Summary_WithSla_CountsOnlyStagesThatHaveATimeLimit()
    {
        await StartSingleStageAsync("SlaX", "Approver", 24, 73);
        await StartSingleStageAsync("NoSlaY", "Approver", null, 74);

        var s = await new MyWorkService(Runtime).GetSummaryAsync(5, new[] { "Approver" });

        Assert.Equal(2, s.Total);
        Assert.Equal(1, s.WithSla);
    }

    // ---------------------------------------------------------------- Persian stage labels

    // فهرست نام مرحله‌های واقعی تعریف‌های گردش‌کار (استخراج‌شده از داده‌ی کلون و Seederها؛ ۱۱ ماژول). اگر مرحله‌ی جدیدی اضافه شد باید همین‌جا و در resx اضافه شود.
    private static readonly string[] KnownStageNames =
    {
        "Approval", "Check", "IDC", "Issue", "QADispositionApproval", "Closure", "RiskAssessment", "SeniorApproval", "QAApproval",
        "CommodityGroupReview", "DirectManager", "InventoryControl", "MaintenanceSupervisor", "PartCataloguer", "SourcingUnit",
        "SparePartsSpecialist", "SuperiorManager", "HSEReview", "MaintenanceApproval", "OperationsApproval", "PendingConfirmation",
        "PendingResponse", "DisciplineReview", "LeadReview", "ReturnToVendor", "Completion", "Created", "Final Approval", "FinalApproval",
        "In Execution", "Maintenance Planning", "On Hold", "Operations Issue / Release", "Originating-Discipline Verification",
        "Preparation / Assignment", "Released / Execution Authorized", "Approved", "Planned", "Submitted", "Under Review"
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> ResxKeys(string file) =>
        System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "PEMS.Web", "Resources", file))
            .Descendants("data").Select(e => (string)e.Attribute("name")!).ToHashSet();

    [Theory]
    [InlineData("DirectManager", "MyWork_Stage_DirectManager")]
    [InlineData("Operations Issue / Release", "MyWork_Stage_OperationsIssueRelease")]
    [InlineData("Originating-Discipline Verification", "MyWork_Stage_OriginatingDisciplineVerification")]
    [InlineData("Released / Execution Authorized", "MyWork_Stage_ReleasedExecutionAuthorized")]
    [InlineData("", "MyWork_Stage_")]
    [InlineData(null, "MyWork_Stage_")]
    public void StageLabel_ResourceKey_KeepsOnlyLettersAndDigits(string? name, string expected)
        => Assert.Equal(expected, WorkflowStageLabels.ResourceKey(name));

    [Fact]
    public void EveryKnownStageName_HasAPersianLabel_InTheFaResx()
    {
        var fa = ResxKeys("SharedResource.fa.resx");
        var missing = KnownStageNames.Select(WorkflowStageLabels.ResourceKey).Where(k => !fa.Contains(k)).Distinct().ToList();

        Assert.True(missing.Count == 0, "Missing Persian stage labels: " + string.Join(", ", missing));
    }

    [Fact]
    public void MyWorkResourceKeys_ExistInBothLanguages()
    {
        var en = ResxKeys("SharedResource.resx"); var fa = ResxKeys("SharedResource.fa.resx");
        var uiKeys = new[] { "MyWork_Approvals", "MyWork_Notifications", "MyWork_ActionItems", "MyWork_Hint", "MyWork_All", "MyWork_Overdue", "MyWork_Personal",
            "MyWork_Queue", "MyWork_Module", "MyWork_Stage", "MyWork_Record", "MyWork_Assigned", "MyWork_Due", "MyWork_Open", "MyWork_Empty", "MyWork_NoSla", "MyWork_Card_Other" };

        Assert.All(uiKeys, k => { Assert.Contains(k, en); Assert.Contains(k, fa); });
    }

    // ---------------------------------------------------------------- sidebar

    [Fact]
    public void Sidebar_HasMyWorkLink_RightAfterDashboard_AndNotInsideAnyVisibilityGate()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml"));
        var dash = text.IndexOf("asp-controller=\"Home\" asp-action=\"Index\"", StringComparison.Ordinal);
        var mine = text.IndexOf("asp-controller=\"MyWork\" asp-action=\"Index\"", StringComparison.Ordinal);

        Assert.True(dash >= 0 && mine > dash, "My Work link must follow the Dashboard link");
        var between = text.Substring(dash, mine - dash);
        Assert.DoesNotContain("@if", between);                       // همان‌طور که Dashboard بدون شرط است؛ صفحه فقط کارهای خودِ کاربر را نشان می‌دهد
        Assert.True(mine - dash < 600, "My Work link must sit directly under Dashboard");
    }

    // ---------------------------------------------------------------- record links

    [Theory]
    [InlineData("Permit", "Permit", 7, "/Permit/Details/7")]
    [InlineData("MOC", "MOC", 8, "/MOC/Details/8")]
    [InlineData("TechnicalQuery", "TechnicalQuery", 9, "/TechnicalQuery/Details/9")]
    [InlineData("VendorDocument", "VDRItem", 10, "/VDR/Details/10")]
    [InlineData("EngineeringDocument", "Document", 11, "/EngineeringDocuments/Details/11")]
    [InlineData("PartCatalogue", "PartCatalogueRequest", 12, "/Warehouse/PartCatalogueRequest/Details/12")]
    [InlineData("NCR", "NonConformanceReport", 13, "/QC/NcrDetails/13")]
    [InlineData("Contract", "Contract", 14, "/Contract/Details/14")]
    [InlineData("IRN", "IrnReceiptDisposition", 15, "/Warehouse/IrnReceipt/Details/15")]
    [InlineData("WorkOrder", "WorkOrderLifecycle", 16, "/WorkOrder/Details/16")]
    [InlineData("WorkOrder", "WorkOrder", 17, "/WorkOrder/Details/17")]
    [InlineData("WorkOrder", "WorkOrderMultiDisciplineReview", 18, "/WorkOrder/Details/18")]
    [InlineData("WorkRequest", "WorkRequest", 19, "/WorkRequest/Details/19")]
    public void Links_MapKnownBusinessObjectTypes(string module, string type, int id, string expected)
        => Assert.Equal(expected, WorkflowTaskLinks.TryGetPath(module, type, id));

    [Theory]
    [InlineData("X", "Unknown", 5)]
    [InlineData("X", "", 5)]
    [InlineData("X", null, 5)]
    [InlineData("X", "Permit", 0)]
    [InlineData("X", "Permit", -3)]
    [InlineData("X", "../../Admin/Users", 5)]
    [InlineData("X", "Permit/../Admin", 5)]
    public void Links_UnknownOrUnsafeInput_YieldsNoLink(string module, string? type, int id)
        => Assert.Null(WorkflowTaskLinks.TryGetPath(module, type, id));

    // ---------------------------------------------------------------- controller: identity comes from the server principal

    private sealed class Ctxt : ICurrentUserContext
    {
        public int? UserId { get; init; }
        public int? EmployeeId { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    }

    private sealed class SpyMyWork : IMyWorkService
    {
        public (int Emp, string[] Roles, string? Module, bool Overdue, bool Personal, int Page, int Size)? Last;
        public Task<MyApprovalsSummaryDto> GetSummaryAsync(int employeeId, IReadOnlyList<string> roles) => Task.FromResult(new MyApprovalsSummaryDto());
        public Task<MyApprovalsPageDto> GetApprovalsAsync(int employeeId, IReadOnlyList<string> roles, string? module, bool overdueOnly, bool personalOnly, int page, int pageSize)
        {
            Last = (employeeId, roles.ToArray(), module, overdueOnly, personalOnly, page, pageSize);
            return Task.FromResult(new MyApprovalsPageDto());
        }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public int? UnreadAskedFor;
        public Task<int> GetUnreadCountAsync(int userId) { UnreadAskedFor = userId; return Task.FromResult(4); }
        public Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null) => throw new NotSupportedException();
        public Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false) => throw new NotSupportedException();
        public Task<PagedResult<NotificationDto>> GetInboxAsync(int userId, string? filter, int page, int pageSize) => throw new NotSupportedException();
        public Task<bool> MarkAsReadAsync(int userId, int notificationId) => throw new NotSupportedException();
        public Task MarkAllAsReadAsync(int userId) => throw new NotSupportedException();
        public Task<string?> OpenAsync(int userId, int notificationId) => throw new NotSupportedException();
        public Task<bool> DismissAsync(int userId, int notificationId) => throw new NotSupportedException();
        public Task DismissAllAsync(int userId) => throw new NotSupportedException();
        public Task MarkRelatedReadAsync(int userId, string relatedType, int relatedId) => throw new NotSupportedException();
        public Task ResolveRelatedAsync(int? userId, string relatedType, int relatedId) => throw new NotSupportedException();
    }

    private sealed class FakeTasks : ITaskService
    {
        public int? AskedFor;
        public Task<int> GetOpenActionItemCountAsync(int applicationUserId) { AskedFor = applicationUserId; return Task.FromResult(2); }
        public Task<int> CreateAsync(string title, string? description, int? assignedToEmployeeId, DateTime? dueDate, string priority, string? relatedType, int? relatedId, int? assignedByApplicationUserId, string? assignedByName) => throw new NotSupportedException();
        public Task<PagedResult<TaskItemDto>> GetMyActionItemsAsync(int applicationUserId, bool openOnly = true, int page = 1, int pageSize = 50) => throw new NotSupportedException();
        public Task<PagedResult<TaskItemDto>> GetAssignedByMeAsync(int applicationUserId, bool openOnly = true, int page = 1, int pageSize = 50) => throw new NotSupportedException();
        public Task<List<TaskItemDto>> GetForRelatedAsync(string relatedType, int relatedId) => throw new NotSupportedException();
        public Task UpdateStatusAsync(int taskItemId, string status, string? completionNote = null) => throw new NotSupportedException();
    }

}
