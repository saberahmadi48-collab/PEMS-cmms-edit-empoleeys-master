using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.TaskManager;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.TaskManager;

/// <summary>
/// Task Manager روی TaskItem (InMemory): دسترسی سطح-شیء (دانستن شناسه دسترسی نمی‌دهد)، گذارهای مجاز، واجد شرایط بودن مسئول
/// بدون جایگزین، کلید فضا، Sprint، پیوند محدود، عملیات گروهی، سازگاری وظایف قدیمی/جلسه/کارهای من.
/// </summary>
public class TaskManagerServiceTests
{
    // واحد ۱ = تعمیرات (مالک فضای MNT)، واحد ۲ = بهره‌برداری
    private const int Alice = 10, Bob = 11, Carol = 12, Dan = 13, Erin = 14;

    private sealed class Env
    {
        public required PEMSDbContext Ctx { get; init; }
        public required FakePermissionService Permissions { get; init; }
        public required TaskManagerService Tasks { get; init; }
        public required WorkSpaceService Spaces { get; init; }
        public int SpaceId { get; set; }
    }

    private static TaskActor A => new(1, Alice, new[] { "Engineer" }, "Alice A");
    private static TaskActor B => new(2, Bob, new[] { "Engineer" }, "Bob B");
    private static TaskActor C => new(3, Carol, new[] { "Engineer" }, "Carol C");     // Lead صریح MNT، واحد ۲
    private static TaskActor E => new(5, Erin, new[] { "Engineer" }, "Erin E");       // واحد ۲، عضو هیچ فضایی نیست
    private static TaskActor Admin => new(9, null, new[] { TaskActor.SystemAdminRole }, "Admin");

    private static async Task<Env> CreateAsync(string name, bool withSpace = true)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TM_{name}");
        ctx.Departments.AddRange(new Department { DepartmentId = 1, Code = "MNT", Name = "Maintenance" }, new Department { DepartmentId = 2, Code = "OPS", Name = "Operations" });
        ctx.Employees.AddRange(
            new Employee { EmployeeId = Alice, FirstName = "Alice", LastName = "A", DepartmentId = 1, DisciplineId = 1 },
            new Employee { EmployeeId = Bob, FirstName = "Bob", LastName = "B", DepartmentId = 1, DisciplineId = 1 },
            new Employee { EmployeeId = Carol, FirstName = "Carol", LastName = "C", DepartmentId = 2, DisciplineId = 1 },
            new Employee { EmployeeId = Dan, FirstName = "Dan", LastName = "D", DepartmentId = 1, DisciplineId = 1, IsActive = false },
            new Employee { EmployeeId = Erin, FirstName = "Erin", LastName = "E", DepartmentId = 2, DisciplineId = 1 });
        ctx.Users.AddRange(
            new ApplicationUser { Id = 1, UserName = "alice", EmployeeId = Alice },
            new ApplicationUser { Id = 2, UserName = "bob", EmployeeId = Bob },
            new ApplicationUser { Id = 3, UserName = "carol", EmployeeId = Carol },
            new ApplicationUser { Id = 5, UserName = "erin", EmployeeId = Erin },
            new ApplicationUser { Id = 9, UserName = "admin" });
        await ctx.SaveChangesAsync();

        var permissions = new FakePermissionService();
        var env = new Env
        {
            Ctx = ctx,
            Permissions = permissions,
            Tasks = new TaskManagerService(ctx, permissions, new FakeNumberingService()),
            Spaces = new WorkSpaceService(ctx, permissions)
        };
        if (withSpace)
        {
            var r = await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "mnt", Name = "Maintenance", OwnerDepartmentId = 1 }, Admin);
            Assert.True(r.Success, r.ErrorKey);
            env.SpaceId = r.Id!.Value;
            Assert.True((await env.Spaces.AddMemberAsync(env.SpaceId, Carol, WorkSpaceMemberRoles.Lead, Admin)).Success);
        }
        return env;
    }

    private static async Task<int> CreateItemAsync(Env env, TaskActor actor, string title = "Pump overhaul", int? assignee = null)
    {
        var r = await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = title, AssigneeEmployeeId = assignee }, actor);
        Assert.True(r.Success, r.ErrorKey);
        return r.Id!.Value;
    }

    private async Task<int> StatusIdAsync(Env env, string name) =>
        (await env.Ctx.WorkItemStatuses.SingleAsync(s => s.WorkSpaceId == env.SpaceId && s.Name == name)).WorkItemStatusId;

    // ============================================================================ spaces & keys

    [Fact]
    public async Task SystemAdmin_CreatesSpace_WithDefaultScheme_AndItemsGetSequentialKeys()
    {
        var env = await CreateAsync(nameof(SystemAdmin_CreatesSpace_WithDefaultScheme_AndItemsGetSequentialKeys));
        Assert.Equal("MNT", (await env.Ctx.WorkSpaces.SingleAsync()).Key);
        Assert.Equal(4, await env.Ctx.WorkItemStatuses.CountAsync());
        Assert.Equal(12, await env.Ctx.WorkItemStatusTransitions.CountAsync());
        Assert.Equal(5, await env.Ctx.WorkItemTypes.CountAsync());

        var first = await CreateItemAsync(env, A);
        var second = await CreateItemAsync(env, B);
        var t1 = await env.Ctx.TaskItems.SingleAsync(t => t.TaskItemId == first);
        var t2 = await env.Ctx.TaskItems.SingleAsync(t => t.TaskItemId == second);
        Assert.Equal("MNT-1", t1.ItemKey);
        Assert.Equal("MNT-2", t2.ItemKey);
        Assert.Equal(TaskItemStatuses.Open, t1.Status);                 // دسته‌ی ToDo ⇒ رشته‌ی قدیمی Open (کارهای من/شمارنده)
        Assert.Equal(Alice, t1.ReporterEmployeeId);
        Assert.Equal("Task", (await env.Ctx.WorkItemTypes.SingleAsync(x => x.WorkItemTypeId == t1.WorkItemTypeId)).Name);
        Assert.True(t2.Rank > t1.Rank);
        Assert.True(await env.Ctx.WorkItemWatchers.AnyAsync(w => w.TaskItemId == first && w.EmployeeId == Alice));
    }

    [Fact]
    public async Task OnlySystemAdmin_ConfiguresSpaces_AndKeysAreValidatedAndUnique()
    {
        var env = await CreateAsync(nameof(OnlySystemAdmin_ConfiguresSpaces_AndKeysAreValidatedAndUnique));
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "OPS", Name = "Ops" }, C)).ErrorKey);
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Spaces.AddMemberAsync(env.SpaceId, Erin, "Member", C)).ErrorKey);
        Assert.Empty(await env.Spaces.GetAllForAdminAsync(C));
        Assert.Equal("Tm_Err_KeyExists", (await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "MNT", Name = "Again" }, Admin)).ErrorKey);
        Assert.Equal("Tm_Err_InvalidKey", (await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "A-B", Name = "Dash" }, Admin)).ErrorKey);
        Assert.Equal("Tm_Err_InvalidEmployee", (await env.Spaces.AddMemberAsync(env.SpaceId, Dan, "Member", Admin)).ErrorKey);   // کارمند غیرفعال
        Assert.Equal("Tm_Err_InvalidDepartment", (await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "XY", Name = "X", OwnerDepartmentId = 99 }, Admin)).ErrorKey);

        // کلید پس از ساخت تغییرناپذیر است
        await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Id = env.SpaceId, Key = "ZZZ", Name = "Renamed", OwnerDepartmentId = 1 }, Admin);
        Assert.Equal("MNT", (await env.Ctx.WorkSpaces.SingleAsync()).Key);
    }

    // ============================================================================ object-level access

    [Fact]
    public async Task KnowingAnId_GivesNoAccess_OutsidersAndAdminsGetNotFound()
    {
        var env = await CreateAsync(nameof(KnowingAnId_GivesNoAccess_OutsidersAndAdminsGetNotFound));
        var id = await CreateItemAsync(env, A);
        var statusId = await StatusIdAsync(env, "In Progress");

        Assert.Null(await env.Tasks.GetDetailAsync(id, E));
        Assert.Null(await env.Tasks.GetHistoryAsync(id, E));
        Assert.Equal(0, (await env.Tasks.QueryAsync(new WorkItemQuery(), E)).TotalCount);
        Assert.Equal("Tm_Err_NotFound", (await env.Tasks.TransitionAsync(id, statusId, null, null, null, E)).ErrorKey);
        Assert.Equal("Tm_Err_NotFound", (await env.Tasks.AddCommentAsync(id, "hi", E)).ErrorKey);
        Assert.Equal("Tm_Err_NotFound", (await env.Tasks.SetWatchingAsync(id, true, E)).ErrorKey);   // دنبال کردن راه دور زدن نیست
        Assert.Null(await env.Tasks.GetBacklogAsync("MNT", E));

        // SystemAdmin فقط پیکربندی می‌کند؛ دسترسی ضمنی به وظایف ندارد
        Assert.Null(await env.Tasks.GetDetailAsync(id, Admin));

        // عضو ضمنی (واحد مالک) و Lead صریح می‌بینند
        Assert.NotNull(await env.Tasks.GetDetailAsync(id, B));
        Assert.NotNull(await env.Tasks.GetDetailAsync(id, C));
    }

    [Fact]
    public async Task PersonalTask_IsVisibleOnlyToInvolvedPeople()
    {
        var env = await CreateAsync(nameof(PersonalTask_IsVisibleOnlyToInvolvedPeople));
        var r = await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "Call vendor", AssigneeEmployeeId = Carol }, A);
        Assert.True(r.Success, r.ErrorKey);
        var id = r.Id!.Value;

        Assert.NotNull(await env.Tasks.GetDetailAsync(id, A));          // ارجاع‌دهنده
        Assert.NotNull(await env.Tasks.GetDetailAsync(id, C));          // مسئول
        Assert.Null(await env.Tasks.GetDetailAsync(id, B));             // هم‌واحد ولی درگیر نیست
        Assert.True((await env.Tasks.TransitionAsync(id, null, TaskItemStatuses.InProgress, null, null, C)).Success);
        Assert.Equal("Tm_Err_NotFound", (await env.Tasks.TransitionAsync(id, null, TaskItemStatuses.Completed, null, null, B)).ErrorKey);
        Assert.Equal("Tm_Err_PersonalTaskFields", (await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "x", StoryPoints = 3 }, A)).ErrorKey);
    }

    [Fact]
    public async Task MembersCanViewAndComment_ButOnlyInvolvedOrLeadEdit_AndOnlyOwnerOrLeadAssign()
    {
        var env = await CreateAsync(nameof(MembersCanViewAndComment_ButOnlyInvolvedOrLeadEdit_AndOnlyOwnerOrLeadAssign));
        var id = await CreateItemAsync(env, A);
        var detail = (await env.Tasks.GetDetailAsync(id, B))!;
        Assert.False(detail.CanEdit);
        Assert.True(detail.CanComment);

        var update = new WorkItemUpdateInput { Id = id, Title = "Hijack", Priority = "High" };
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.UpdateAsync(update, B)).ErrorKey);
        Assert.True((await env.Tasks.UpdateAsync(new WorkItemUpdateInput { Id = id, Title = "Lead edit", Priority = "High" }, C)).Success);
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.AssignAsync(id, Bob, null, B)).ErrorKey);
        Assert.True((await env.Tasks.AddCommentAsync(id, "<script>alert(1)</script> checked", B)).Success);

        // مسئول می‌تواند وضعیت را تغییر دهد ولی ارجاع مجدد فقط با مالک/Lead است
        Assert.True((await env.Tasks.AssignAsync(id, Bob, null, A)).Success);
        Assert.True((await env.Tasks.TransitionAsync(id, await StatusIdAsync(env, "In Progress"), null, null, null, B)).Success);
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.AssignAsync(id, Alice, null, B)).ErrorKey);

        // متن نظر همان‌گونه که هست ذخیره می‌شود (نمایش با HTML-encode Razor)؛ فقط نویسنده حذف می‌کند
        var comment = await env.Ctx.WorkItemComments.SingleAsync();
        Assert.StartsWith("<script>", comment.Body);
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.DeleteCommentAsync(comment.WorkItemCommentId, A)).ErrorKey);
        Assert.True((await env.Tasks.DeleteCommentAsync(comment.WorkItemCommentId, B)).Success);
        Assert.Equal("", (await env.Tasks.GetDetailAsync(id, A))!.Comments.Single().Body);
    }

    [Fact]
    public async Task RoleGates_StillApply_OnTopOfObjectRules()
    {
        var env = await CreateAsync(nameof(RoleGates_StillApply_OnTopOfObjectRules));
        var id = await CreateItemAsync(env, A);
        env.Permissions.Denied.Add(("Task", "Edit"));
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.TransitionAsync(id, await StatusIdAsync(env, "Done"), null, null, null, A)).ErrorKey);
        env.Permissions.Denied.Add(("Task", "View"));
        Assert.Null(await env.Tasks.GetDetailAsync(id, A));
        Assert.Empty(await env.Tasks.GetAccessibleSpacesAsync(A));
        env.Permissions.Denied.Add(("Task", "Create"));
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = "x" }, A)).ErrorKey);
    }

    // ============================================================================ transitions

    [Fact]
    public async Task SpaceTransitions_FollowTheConfiguredScheme_AndSyncLegacyStatus()
    {
        var env = await CreateAsync(nameof(SpaceTransitions_FollowTheConfiguredScheme_AndSyncLegacyStatus));
        var id = await CreateItemAsync(env, A);
        int todo = await StatusIdAsync(env, "To Do"), doing = await StatusIdAsync(env, "In Progress"), done = await StatusIdAsync(env, "Done");

        Assert.True((await env.Spaces.SetTransitionAsync(env.SpaceId, todo, done, false, Admin)).Success);
        Assert.Equal("Tm_Err_InvalidTransition", (await env.Tasks.TransitionAsync(id, done, null, null, null, A)).ErrorKey);
        Assert.Equal("Tm_Err_InvalidTransition", (await env.Tasks.TransitionAsync(id, null, "Completed", null, null, A)).ErrorKey);   // رشته‌ی قدیمی برای وظیفه‌ی فضا پذیرفته نیست

        Assert.True((await env.Tasks.TransitionAsync(id, doing, null, null, null, A)).Success);
        Assert.Equal(TaskItemStatuses.InProgress, (await env.Ctx.TaskItems.SingleAsync()).Status);

        Assert.True((await env.Tasks.TransitionAsync(id, done, null, "fixed", null, A)).Success);
        var t = await env.Ctx.TaskItems.SingleAsync();
        Assert.Equal(TaskItemStatuses.Completed, t.Status);
        Assert.NotNull(t.CompletedDate);
        Assert.Equal("fixed", t.CompletionNote);

        // وضعیت در حال استفاده غیرفعال نمی‌شود و دسته‌اش تغییر نمی‌کند
        Assert.Equal("Tm_Err_StatusInUse", (await env.Spaces.UpdateStatusAsync(done, "Done", null, "InProgress", 4, true, Admin)).ErrorKey);
    }

    [Fact]
    public async Task LegacyTransitions_RejectFreeTextAndDisallowedMoves()
    {
        var env = await CreateAsync(nameof(LegacyTransitions_RejectFreeTextAndDisallowedMoves), withSpace: false);
        var id = (await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "Legacy", AssigneeEmployeeId = Bob }, A)).Id!.Value;

        Assert.Equal("Tm_Err_InvalidTransition", (await env.Tasks.TransitionAsync(id, null, "Hacked", null, null, B)).ErrorKey);
        Assert.True((await env.Tasks.TransitionAsync(id, null, "Completed", "ok", null, B)).Success);
        Assert.Equal("Tm_Err_InvalidTransition", (await env.Tasks.TransitionAsync(id, null, "InProgress", null, null, B)).ErrorKey);
        Assert.True((await env.Tasks.TransitionAsync(id, null, "Open", null, null, B)).Success);
        Assert.Null((await env.Ctx.TaskItems.SingleAsync()).CompletedDate);

        // مسیر سیستمی TaskService هم گذار نامعتبر را نادیده می‌گیرد
        var service = new TaskService(env.Ctx, null!);
        await service.UpdateStatusAsync(id, "Bogus");
        Assert.Equal("Open", (await env.Ctx.TaskItems.SingleAsync()).Status);
    }

    // ============================================================================ assignees

    [Fact]
    public async Task Assignees_AreOnlyActiveEligibleEmployees_WithNoFallback()
    {
        var env = await CreateAsync(nameof(Assignees_AreOnlyActiveEligibleEmployees_WithNoFallback));
        var eligible = await env.Tasks.GetEligibleAssigneesAsync(env.SpaceId, A);
        Assert.Equal("RESOLVED", eligible.State);
        Assert.Equal(new[] { Alice, Bob, Carol }, eligible.Candidates.Select(c => c.EmployeeId).OrderBy(x => x).ToArray());

        Assert.Equal("Tm_Err_AssigneeNotEligible", (await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = "x", AssigneeEmployeeId = Dan }, A)).ErrorKey);
        Assert.Equal("Tm_Err_AssigneeNotEligible", (await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = "x", AssigneeEmployeeId = Erin }, A)).ErrorKey);
        Assert.Equal(0, await env.Ctx.TaskItems.CountAsync());

        // فضای بدون واحد و عضو: NO_CONFIGURATION، هیچ کاندیدای پیش‌فرضی نیست
        var bare = await env.Spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "BARE", Name = "Bare" }, Admin);
        var diag = await env.Tasks.GetEligibleAssigneesAsync(bare.Id, A);
        Assert.Equal("NO_CONFIGURATION", diag.State);
        Assert.Empty(diag.Candidates);
    }

    // ============================================================================ sprints, rank, bulk

    [Fact]
    public async Task Sprints_OneActivePerSpace_LeadOnly_AndCompletionMovesUnfinishedWork()
    {
        var env = await CreateAsync(nameof(Sprints_OneActivePerSpace_LeadOnly_AndCompletionMovesUnfinishedWork));
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Spaces.CreateSprintAsync(env.SpaceId, "S1", null, null, null, A)).ErrorKey);
        var s1 = (await env.Spaces.CreateSprintAsync(env.SpaceId, "S1", "goal", DateTime.Today, DateTime.Today.AddDays(14), C)).Id!.Value;
        var s2 = (await env.Spaces.CreateSprintAsync(env.SpaceId, "S2", null, null, null, C)).Id!.Value;

        var open = await CreateItemAsync(env, A, "open");
        var finished = await CreateItemAsync(env, A, "finished");
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.SetSprintAsync(open, s1, A)).ErrorKey);
        Assert.True((await env.Tasks.SetSprintAsync(open, s1, C)).Success);
        Assert.True((await env.Tasks.SetSprintAsync(finished, s1, C)).Success);
        Assert.True((await env.Tasks.TransitionAsync(finished, await StatusIdAsync(env, "Done"), null, null, null, A)).Success);

        Assert.True((await env.Spaces.StartSprintAsync(s1, C)).Success);
        Assert.Equal("Tm_Err_ActiveSprintExists", (await env.Spaces.StartSprintAsync(s2, C)).ErrorKey);

        var board = (await env.Tasks.GetBoardAsync("MNT", A))!;
        Assert.Equal(s1, board.ActiveSprint!.Id);
        Assert.Equal(2, board.Columns.Sum(c => c.Items.Count));

        Assert.True((await env.Spaces.CompleteSprintAsync(s1, s2, C)).Success);
        Assert.Equal(s2, (await env.Ctx.TaskItems.SingleAsync(t => t.TaskItemId == open)).SprintId);
        Assert.Equal(s1, (await env.Ctx.TaskItems.SingleAsync(t => t.TaskItemId == finished)).SprintId);
        Assert.Equal(SprintStates.Closed, (await env.Ctx.Sprints.SingleAsync(s => s.SprintId == s1)).State);
    }

    [Fact]
    public async Task Reorder_IsLeadOnly_AndRejectsStaleOrForeignIds()
    {
        var env = await CreateAsync(nameof(Reorder_IsLeadOnly_AndRejectsStaleOrForeignIds));
        var a = await CreateItemAsync(env, A, "a");
        var b = await CreateItemAsync(env, A, "b");
        var c = await CreateItemAsync(env, A, "c");
        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.ReorderAsync(env.SpaceId, null, new[] { c, b, a }, A)).ErrorKey);
        Assert.True((await env.Tasks.ReorderAsync(env.SpaceId, null, new[] { c, a }, C)).Success);   // زیرمجموعه: فقط جایگاه همین دو
        var order = (await env.Tasks.GetBacklogAsync("MNT", C))!.BacklogItems.Select(i => i.Id).ToArray();
        Assert.Equal(new[] { c, b, a }, order);
        Assert.Equal("Tm_Err_StaleOrder", (await env.Tasks.ReorderAsync(env.SpaceId, null, new[] { a, 9999 }, C)).ErrorKey);

        Assert.True((await env.Tasks.MoveRankAsync(a, up: true, C)).Success);
        Assert.Equal(new[] { c, a, b }, (await env.Tasks.GetBacklogAsync("MNT", C))!.BacklogItems.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Bulk_AuthorizesEachItem_AndReportsFailuresWithoutLeakingHiddenItems()
    {
        var env = await CreateAsync(nameof(Bulk_AuthorizesEachItem_AndReportsFailuresWithoutLeakingHiddenItems));
        var mine = await CreateItemAsync(env, A, "mine");
        var others = await CreateItemAsync(env, B, "bob's");
        var hidden = (await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "private", AssigneeEmployeeId = Erin }, E)).Id!.Value;

        var r = await env.Tasks.BulkAsync(new[] { mine, others, hidden }, "status", (await StatusIdAsync(env, "In Progress")).ToString(), A);
        Assert.Equal(1, r.Succeeded);
        Assert.Equal(2, r.Failed.Count);
        Assert.Contains(r.Failed, f => f.EndsWith("Tm_Err_NotAuthorized"));
        Assert.Contains(r.Failed, f => f.EndsWith("Tm_Err_NotFound"));
        Assert.Equal(TaskItemStatuses.Open, (await env.Ctx.TaskItems.SingleAsync(t => t.TaskItemId == others)).Status);
    }

    // ============================================================================ links, hierarchy, history

    [Fact]
    public async Task Links_ToItemsTheViewerCannotSee_ShowARestrictedPlaceholder()
    {
        var env = await CreateAsync(nameof(Links_ToItemsTheViewerCannotSee_ShowARestrictedPlaceholder));
        var item = await CreateItemAsync(env, A);
        var personal = (await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "Secret prep", AssigneeEmployeeId = Alice }, C)).Id!.Value;
        Assert.True((await env.Tasks.AddLinkAsync(item, WorkItemLinkTypes.Blocks, "#" + personal, A)).Success);
        Assert.Equal("Tm_Err_LinkExists", (await env.Tasks.AddLinkAsync(item, WorkItemLinkTypes.Blocks, personal.ToString(), A)).ErrorKey);
        Assert.Equal("Tm_Err_SelfLink", (await env.Tasks.AddLinkAsync(item, WorkItemLinkTypes.RelatesTo, "mnt-1", A)).ErrorKey);

        var asAlice = (await env.Tasks.GetDetailAsync(item, A))!.Links.Single();
        Assert.True(asAlice.OtherVisible);
        Assert.Equal("Secret prep", asAlice.OtherTitle);

        var asBob = (await env.Tasks.GetDetailAsync(item, B))!.Links.Single();
        Assert.False(asBob.OtherVisible);
        Assert.Null(asBob.OtherTitle);
        Assert.Equal(0, asBob.OtherId);

        // مقصدی که کاربر نمی‌بیند «یافت نشد» است، نه «غیرمجاز»
        var erinTask = (await env.Tasks.CreateAsync(new WorkItemCreateInput { Title = "erin", AssigneeEmployeeId = Erin }, E)).Id!.Value;
        Assert.Equal("Tm_Err_LinkTargetNotFound", (await env.Tasks.AddLinkAsync(item, WorkItemLinkTypes.RelatesTo, "#" + erinTask, A)).ErrorKey);
    }

    [Fact]
    public async Task Subtasks_RequireAParentInTheSameSpace_AndCyclesAreRejected()
    {
        var env = await CreateAsync(nameof(Subtasks_RequireAParentInTheSameSpace_AndCyclesAreRejected));
        var parent = await CreateItemAsync(env, A, "parent");
        var subType = (await env.Ctx.WorkItemTypes.SingleAsync(t => t.IsSubtask)).WorkItemTypeId;
        Assert.Equal("Tm_Err_SubtaskNeedsParent", (await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = "s", TypeId = subType }, A)).ErrorKey);
        var child = (await env.Tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = env.SpaceId, Title = "s", TypeId = subType, ParentId = parent }, A)).Id!.Value;
        Assert.Single((await env.Tasks.GetDetailAsync(parent, A))!.Subtasks);

        Assert.Equal("Tm_Err_InvalidParent", (await env.Tasks.UpdateAsync(new WorkItemUpdateInput { Id = parent, Title = "parent", ParentId = child }, A)).ErrorKey);
        Assert.Equal("Tm_Err_InvalidParent", (await env.Tasks.UpdateAsync(new WorkItemUpdateInput { Id = parent, Title = "parent", ParentId = parent }, A)).ErrorKey);
    }

    [Fact]
    public async Task Archive_IsSoft_HidesFromListsAndMyWork_AndHistoryIsRecorded()
    {
        var env = await CreateAsync(nameof(Archive_IsSoft_HidesFromListsAndMyWork_AndHistoryIsRecorded));
        var id = await CreateItemAsync(env, A, assignee: Bob);
        Assert.True((await env.Tasks.UpdateAsync(new WorkItemUpdateInput { Id = id, Title = "Renamed", Priority = "Urgent" }, A)).Success);
        var legacy = new TaskService(env.Ctx, null!);
        Assert.Equal(1, await legacy.GetOpenActionItemCountAsync(2));

        Assert.Equal("Tm_Err_NotAuthorized", (await env.Tasks.SetArchivedAsync(id, true, B)).ErrorKey);   // مسئول بایگانی نمی‌کند
        Assert.True((await env.Tasks.SetArchivedAsync(id, true, A)).Success);
        Assert.Equal(1, await env.Ctx.TaskItems.CountAsync());                                            // حذف سخت نیست
        Assert.Equal(0, await legacy.GetOpenActionItemCountAsync(2));
        Assert.Empty((await legacy.GetMyActionItemsAsync(2)).Items);
        Assert.Equal(0, (await env.Tasks.QueryAsync(new WorkItemQuery(), A)).TotalCount);
        Assert.Equal(1, (await env.Tasks.QueryAsync(new WorkItemQuery { IncludeArchived = true }, A)).TotalCount);
        Assert.False((await env.Tasks.GetDetailAsync(id, A))!.CanEdit);                                 // بایگانی‌شده فقط‌خواندنی

        var history = (await env.Tasks.GetHistoryAsync(id, A))!;
        Assert.Contains(history, h => h.Action == "Update" && (h.Changes ?? "").Contains("Title"));
    }

    // ============================================================================ legacy compatibility

    [Fact]
    public async Task MeetingActionItems_AndLegacyLists_KeepWorking()
    {
        var env = await CreateAsync(nameof(MeetingActionItems_AndLegacyLists_KeepWorking), withSpace: false);
        var legacy = new TaskService(env.Ctx, null!);
        var id = await legacy.CreateAsync("Follow up minutes", null, Bob, DateTime.Today.AddDays(2), "High", "Meeting", 7, 1, "Alice A");

        var t = await env.Ctx.TaskItems.SingleAsync();
        Assert.True(t.IsActive);
        Assert.Null(t.WorkSpaceId);
        Assert.Single(await legacy.GetForRelatedAsync("Meeting", 7));
        Assert.Single((await legacy.GetMyActionItemsAsync(2)).Items);
        Assert.Single((await legacy.GetAssignedByMeAsync(1)).Items);

        var detail = (await env.Tasks.GetDetailAsync(id, B))!;
        Assert.True(detail.IsLegacy);
        Assert.Equal("Meeting", detail.RelatedType);
        Assert.Equal(new[] { "InProgress", "Completed", "Cancelled" }, detail.AllowedTransitions.Select(x => x.Value).ToArray());
        Assert.Null(await env.Tasks.GetDetailAsync(id, C));
    }
}
