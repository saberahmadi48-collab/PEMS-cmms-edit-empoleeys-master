using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Phase 3B - پایه‌ی تست‌های حاکمیت Work Order: سازمان‌ها (Maintenance/Engineering/Operations)، دیسیپلین‌ها، نقش‌ها و مجوزها همه از Seeder های واقعی
/// (WorkOrderGovernanceSeeder + WorkOrderLifecycleWorkflowSeeder) ساخته می‌شوند؛ هیچ Mock ای برای مجوز/گردش‌کار نیست.
/// </summary>
public abstract class WorkOrderGovernanceTestBase : IDisposable
{
    protected readonly PEMSDbContext Ctx = PemsDbContextFactory.CreateInMemory("wo-gov-" + Guid.NewGuid());
    protected readonly IAccessControlService AccessControl;
    protected readonly WorkOrderLifecycleService Svc;
    protected int Mech, Elec, Instr, Civil, EquipmentId;
    protected int DeptMaint, DeptEng, DeptOps;

    // (employeeId, roles) بازیگران
    protected WorkOrderActor Creator = null!, Planner = null!, DisciplineHead = null!, Executor = null!, Ops = null!, Verifier = null!, Approver = null!, Manager = null!,
        EngVerifier = null!, EngApprover = null!, ElecExecutor = null!, EngManager = null!;

    protected WorkOrderGovernanceTestBase(bool seedWorkflow = true)
    {
        DeptMaint = AddDept("Maintenance"); DeptEng = AddDept("Engineering"); DeptOps = AddDept("Operations");
        Mech = AddDiscipline("Mechanical", "MECH"); Elec = AddDiscipline("Electrical", "ELEC"); Instr = AddDiscipline("Instrumentation", "INST"); Civil = AddDiscipline("Civil", "CIV");
        Ctx.Projects.Add(new Project { ProjectCode = "P", ProjectName = "P" });
        Ctx.SaveChanges();
        Ctx.Equipments.Add(new Equipment { TagNumber = "P-101", Name = "Pump", DisciplineId = Mech, ProjectId = Ctx.Projects.First().ProjectId, IsActive = true });
        Ctx.SaveChanges();
        EquipmentId = Ctx.Equipments.First().EquipmentId;

        foreach (var r in new[] { SystemRoles.DisciplineEngineer, SystemRoles.EngineeringSupervisor, SystemRoles.EngineeringManager, SystemRoles.MaintenanceEngineer })
            Ctx.Roles.Add(new ApplicationRole { Name = r, NormalizedName = r.ToUpperInvariant() });
        // نقش‌های قدیمی هنگام Seed اولیه‌ی PermissionService گرانت‌های گسترده‌ی WorkOrder داشتند؛ همان وضعیت پیش از Phase 3B را بازتولید می‌کنیم
        foreach (var (role, c, e, a) in new[] { (SystemRoles.DisciplineEngineer, false, false, true), (SystemRoles.EngineeringSupervisor, true, true, true), (SystemRoles.EngineeringManager, true, true, true), (SystemRoles.MaintenanceEngineer, true, true, true) })
            Ctx.RolePermissions.Add(new RolePermission { RoleName = role, ModuleName = "WorkOrder", CanView = true, CanCreate = c, CanEdit = e, CanApprove = a, CreatedDate = DateTime.Now });
        Ctx.SaveChanges();
        foreach (var action in new[] { "Create", "Edit", "Approve", "Delete", "View" })
            Ctx.Permissions.Add(new Permission { Name = "WorkOrder." + action, ResourceName = "WorkOrder", ActionName = action, IsActive = true, CreatedDate = DateTime.Now });
        Ctx.SaveChanges();
        foreach (var (role, actions) in new[] { (SystemRoles.DisciplineEngineer, new[] { "View", "Approve" }), (SystemRoles.EngineeringSupervisor, new[] { "View", "Create", "Edit", "Approve" }),
                     (SystemRoles.EngineeringManager, new[] { "View", "Create", "Edit", "Approve", "Delete" }), (SystemRoles.MaintenanceEngineer, new[] { "View", "Create", "Edit", "Approve" }) })
            foreach (var a in actions)
                Ctx.RolePermissionGrants.Add(new RolePermissionGrant
                {
                    RoleId = Ctx.Roles.First(x => x.Name == role).Id, PermissionId = Ctx.Permissions.First(p => p.Name == "WorkOrder." + a).PermissionId, IsActive = true
                });
        Ctx.SaveChanges();

        if (seedWorkflow)
        {
            var sp = new ServiceCollection().AddSingleton(Ctx).BuildServiceProvider();
            WorkOrderReviewWorkflowSeeder.SeedAsync(sp).GetAwaiter().GetResult();      // تعریف قدیمی بازبینی (Legacy)
            WorkOrderGovernanceSeeder.SeedAsync(sp).GetAwaiter().GetResult();
            WorkOrderLifecycleWorkflowSeeder.SeedAsync(sp).GetAwaiter().GetResult();
            // طبق «PEMS — Work Order Governance Remediation» F2: نسخه‌ی ۲ (جداسازی Issue/Release از
            // Start Work) - همه‌ی تست‌های این پایه از این پس رفتار واقعی v2 را می‌بینند.
            WorkOrderLifecycleWorkflowV2Seeder.SeedAsync(sp).GetAwaiter().GetResult();
        }

        // سازمان‌ها/بازیگران
        Creator = Actor(DeptMaint, Mech, "MNT.PlanningExpert");
        Planner = Actor(DeptMaint, Mech, "MNT.PlanningHead");
        DisciplineHead = Actor(DeptMaint, Mech, "MNT.DisciplineHead");
        Executor = Actor(DeptMaint, Mech, "MNT.Expert");
        Ops = Actor(DeptOps, Mech, "OPS.IssueAuthority");
        Verifier = Actor(DeptMaint, Mech, "MNT.SeniorExpert");
        Approver = Actor(DeptMaint, Mech, "MNT.DisciplineHead");
        Manager = Actor(DeptMaint, Elec, "MNT.Manager");
        EngVerifier = Actor(DeptEng, Mech, SystemRoles.DisciplineEngineer);
        EngApprover = Actor(DeptEng, Mech, SystemRoles.EngineeringSupervisor);
        EngManager = Actor(DeptEng, Mech, SystemRoles.EngineeringManager);
        ElecExecutor = Actor(DeptMaint, Elec, "MNT.Expert");

        AccessControl = new AccessControlService(Ctx);
        Svc = new WorkOrderLifecycleService(Ctx, new WorkOrderReleaseGateService(Ctx), AccessControl);
    }

    public void Dispose() => Ctx.Dispose();

    private int AddDept(string name) { var d = new Department { Name = name, Code = name[..3].ToUpper() }; Ctx.Departments.Add(d); Ctx.SaveChanges(); return d.DepartmentId; }
    private int AddDiscipline(string name, string code) { var d = new Discipline { Name = name, Code = code, IsActive = true }; Ctx.Disciplines.Add(d); Ctx.SaveChanges(); return d.DisciplineId; }

    protected WorkOrderActor Actor(int dept, int discipline, params string[] roles)
    {
        var e = new Employee { FirstName = "E" + Guid.NewGuid().ToString("N")[..4], LastName = "T", DepartmentId = dept, DisciplineId = discipline, IsActive = true };
        Ctx.Employees.Add(e);
        Ctx.SaveChanges();
        return new WorkOrderActor(e.EmployeeId, roles);
    }

    protected static WorkOrderActor WithRoles(WorkOrderActor a, params string[] extra) => new(a.EmployeeId, a.Roles.Concat(extra).ToArray());

    protected int _num;
    protected async Task<int> CreateAsync(string type = "Preventive", WorkOrderActor? creator = null)
    {
        var (ok, err, id) = await Svc.CreateGovernedAsync(new WorkOrderCreateRequest
        { WorkOrderNumber = "WO-T" + (++_num).ToString("D4"), EquipmentId = EquipmentId, WorkOrderType = type, Priority = "Medium", Description = "t" }, creator ?? Creator);
        Assert.True(ok, err);
        return id!.Value;
    }

    protected async Task Do(int id, string action, WorkOrderActor who, string? comment = null, WorkOrderCompletionData? data = null)
    {
        // A1/B14: Reject/Cancel/Hold/Return نیازمند دلیل‌اند؛ تست‌ها که موضوعشان دلیل نیست یک دلیل پیش‌فرض می‌دهند
        var (ok, err) = await Svc.ExecuteWorkflowActionAsync(id, action, comment ?? (PEMS.Application.Common.WorkflowActionRules.ActionRequiresReason(action) ? "test reason" : null), who, data);
        Assert.True(ok, $"{action} by {who.Roles.FirstOrDefault()}: {err}");
    }

    protected async Task<string> Fail(int id, string action, WorkOrderActor who, WorkOrderCompletionData? data = null)
    {
        var (ok, err) = await Svc.ExecuteWorkflowActionAsync(id, action, PEMS.Application.Common.WorkflowActionRules.ActionRequiresReason(action) ? "test reason" : null, who, data);
        Assert.False(ok, $"{action} by {who.Roles.FirstOrDefault()} unexpectedly succeeded");
        return err ?? "";
    }

    protected async Task<WorkOrder> Wo(int id) => await Ctx.WorkOrders.AsNoTracking().FirstAsync(w => w.WorkOrderId == id);

    protected async Task PlanAsync(int id, int? discipline = null)
    {
        await Do(id, "Approve", Creator);                                             // Submit to Planning
        var (ok, err) = await Svc.SetPlanningAsync(id, discipline ?? Mech, Planner); Assert.True(ok, err);
        await Do(id, "Approve", Planner);                                             // Plan Complete → Preparation
    }

    protected async Task PrepareAndIssueAsync(int id, WorkOrderActor? exec = null)
    {
        var (ok, err) = await Svc.AssignExecutorAsync(id, null, (exec ?? Executor).EmployeeId, DisciplineHead); Assert.True(ok, err);
        await Do(id, "Approve", Planner);                                             // Request Issue
        await Do(id, "Approve", Ops);                                                 // Issue / Release → Released/Execution Authorized (F2)
        await Do(id, "Approve", exec ?? Executor);                                    // Start Work → Execution (F2) - همان مجری تخصیص‌یافته
    }

    /// <summary>Plan → Assign/Issue/Start؛ اسم واضح برای تست‌هایی که فقط لازم دارند به InProgress برسند.</summary>
    protected async Task RunToInProgressAsync(int id, WorkOrderActor? exec = null)
    {
        await PlanAsync(id);
        await PrepareAndIssueAsync(id, exec);
    }

    protected static WorkOrderCompletionData Data(string? mode = "Leakage") => new() { FailureModeCode = mode, FailureCauseCode = "Wear", RootCause = "seal", DowntimeHours = 2 };

    protected async Task RunToVerificationAsync(int id, string type = "Corrective")
    {
        await PlanAsync(id);
        await PrepareAndIssueAsync(id);
        await Do(id, "Approve", Executor, null, Data());                             // Record Completion
        await Do(id, "Approve", Executor);                                            // Send to Verification
    }

    /// <summary>دیسیپلین/سازمان مبدأ را مستقیماً (Arrange) روی Engineering/Mechanical می‌گذارد؛ مسیر واقعی آن در تست تبدیل Work Request پوشش داده شده است.</summary>
    protected async Task MakeEngineeringOriginAsync(int id)
    {
        var w = await Ctx.WorkOrders.FirstAsync(x => x.WorkOrderId == id);
        w.OriginatingOrganization = OrganizationDomains.Engineering; w.OriginatingDisciplineId = Mech;
        await Ctx.SaveChangesAsync();
    }

    /// <summary>یک Team فعال زیر یک MaintenanceGroup تازه در دیسیپلین داده‌شده می‌سازد (برای تست اسکوپ دیسیپلین در AssignExecutorAsync/AddLaborEntryAsync).</summary>
    protected int AddTeam(int disciplineId, string name = "Team")
    {
        var g = new MaintenanceGroup { Code = "G" + Guid.NewGuid().ToString("N")[..6], Name = name + "-Group", DisciplineId = disciplineId, IsActive = true };
        Ctx.MaintenanceGroups.Add(g);
        Ctx.SaveChanges();
        var t = new Team { Code = "T" + Guid.NewGuid().ToString("N")[..6], Name = name, MaintenanceGroupId = g.MaintenanceGroupId, IsActive = true };
        Ctx.Teams.Add(t);
        Ctx.SaveChanges();
        return t.TeamId;
    }

    /// <summary>
    /// SCRUM-437: the child-section POSTs (SetPlanning, AssignExecutor, SetSchedule) are shipped action definitions (work-order-*.json) run by the generic operation.invoke handler.
    /// This runs the real definition file for <paramref name="who"/> against this test database and mirrors the old controller outcome (a redirect plus a Message/Error toast).
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.IActionResult> RunSectionActionAsync(Microsoft.AspNetCore.Mvc.Controller toast, WorkOrderActor who, string key, params (string Name, string? Value)[] values)
    {
        var userName = "section-" + who.EmployeeId;
        if (!await Ctx.Users.AnyAsync(u => u.UserName == userName))
        {
            Ctx.Users.Add(new ApplicationUser { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), EmployeeId = who.EmployeeId });
            await Ctx.SaveChangesAsync();
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Persistence", "Definitions", "System", "action", "core", key + ".json"))) dir = dir.Parent;
        var file = Path.Combine(dir!.FullName, "PEMS.Persistence", "Definitions", "System", "action", "core", key + ".json");
        var parameters = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["payload"]!["parameters"]!.AsObject();

        var services = new ServiceCollection()
            .AddSingleton(Ctx)
            .AddSingleton<IDefinitionAccess>(new PEMS.Persistence.Definitions.PermissionDefinitionAccess(new PermissionService(Ctx, AccessControl)))
            .AddSingleton<IWorkOrderLifecycleService>(Svc)
            .AddSingleton<ICrewService>(new CrewService(Ctx))
            .AddSingleton<IWorkOrderChildService>(new WorkOrderChildService(Ctx))
            .AddSingleton<IWorkOrderReviewService>(new WorkOrderReviewService(Ctx, new PEMS.WorkflowPlatform.Services.WorkflowRuntimeService(Ctx)))
            .AddSingleton<IWorkOrderSectionGuard>(new WorkOrderSectionGuard(Ctx, AccessControl))
            .AddSingleton<IWorkOrderTaskOperations>(new PEMS.Web.Services.WorkOrderTaskOperations(Ctx, new WorkOrderLaborService(Ctx, AccessControl), null!, null!))
            .BuildServiceProvider();
        var handler = new PEMS.Web.Definitions.OperationInvokeAction(services.GetRequiredService<IServiceScopeFactory>(),
            PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly));
        var outcome = await handler.ExecuteAsync(new PEMS.Web.Definitions.ActionInvocation(
            values.ToDictionary(v => v.Name, v => v.Value), "en", who.Roles.ToList(), userName, parameters, "10.0.0.9", null), CancellationToken.None);

        toast.TempData[outcome.Ok ? "Message" : "Error"] = outcome.Ok ? "ok" : outcome.FormError?.Resolve(new System.Globalization.CultureInfo("en")) ?? "refused";
        return new Microsoft.AspNetCore.Mvc.RedirectToActionResult("Details", "WorkOrder", new { id = values.FirstOrDefault(v => v.Name == "id").Value });
    }
}

/// <summary>
/// SCRUM-437: the spare-part, required-skill and required-material POSTs are shipped action definitions (work-order-*.json). These extension methods keep the old call shape of the tests
/// (<c>ctl.AddSpareUsage(...)</c>) and run the real definition for the user the controller was built for; the result is the redirect with the toast the controller used to give.
/// </summary>
public static class WorkOrderChildActionShims
{
    private sealed record Registration(WorkOrderGovernanceTestBase Owner, WorkOrderActor Who);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorkOrderController, Registration> Owners = new();

    public static WorkOrderController As(this WorkOrderController ctl, WorkOrderGovernanceTestBase owner, WorkOrderActor who)
    {
        Owners.Remove(ctl); Owners.Add(ctl, new Registration(owner, who));
        return ctl;
    }

    private static Task<Microsoft.AspNetCore.Mvc.IActionResult> Run(WorkOrderController ctl, string key, params (string Name, string? Value)[] values)
    {
        Assert.True(Owners.TryGetValue(ctl, out var owner), "register the controller with .As(this, actor) in the test");
        return owner.Owner.RunSectionActionAsync(ctl, owner.Who, key, values);
    }

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> AddSpareUsage(this WorkOrderController ctl, int workOrderId, int materialItemId, decimal quantity, decimal? unitCost, string? correctionReason = null)
        => Run(ctl, "work-order-add-spare-usage", ("workOrderId", workOrderId.ToString()), ("materialItemId", materialItemId.ToString()), ("quantity", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("unitCost", unitCost?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("correctionReason", correctionReason));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> ReturnSpareUsage(this WorkOrderController ctl, int maintenanceSpareUsageId, int workOrderId, decimal returnQuantity, string? reason)
        => Run(ctl, "work-order-return-spare-usage", ("maintenanceSpareUsageId", maintenanceSpareUsageId.ToString()), ("workOrderId", workOrderId.ToString()), ("returnQuantity", returnQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("reason", reason));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> AddRequiredSkill(this WorkOrderController ctl, int workOrderId, int skillId, string? minimumProficiencyLevel, decimal? plannedHours)
        => Run(ctl, "work-order-add-required-skill", ("workOrderId", workOrderId.ToString()), ("skillId", skillId.ToString()), ("minimumProficiencyLevel", minimumProficiencyLevel), ("plannedHours", plannedHours?.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> RemoveRequiredSkill(this WorkOrderController ctl, int requiredSkillId, int workOrderId)
        => Run(ctl, "work-order-remove-required-skill", ("requiredSkillId", requiredSkillId.ToString()), ("workOrderId", workOrderId.ToString()));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> AddRequiredMaterial(this WorkOrderController ctl, int workOrderId, int materialItemId, decimal requiredQuantity)
        => Run(ctl, "work-order-add-required-material", ("workOrderId", workOrderId.ToString()), ("materialItemId", materialItemId.ToString()), ("requiredQuantity", requiredQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> RemoveRequiredMaterial(this WorkOrderController ctl, int workOrderMaterialId, int workOrderId)
        => Run(ctl, "work-order-remove-required-material", ("workOrderMaterialId", workOrderMaterialId.ToString()), ("workOrderId", workOrderId.ToString()));

    // SCRUM-764: the task check-off and the labor entry are action definitions too (work-order-complete-task, work-order-reopen-task, work-order-add-labor)
    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> CompleteTask(this WorkOrderController ctl, int workOrderTaskId, int workOrderId)
        => Run(ctl, "work-order-complete-task", ("workOrderTaskId", workOrderTaskId.ToString()), ("workOrderId", workOrderId.ToString()));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> ReopenTask(this WorkOrderController ctl, int workOrderTaskId, int workOrderId)
        => Run(ctl, "work-order-reopen-task", ("workOrderTaskId", workOrderTaskId.ToString()), ("workOrderId", workOrderId.ToString()));

    public static Task<Microsoft.AspNetCore.Mvc.IActionResult> AddLaborEntry(this WorkOrderController ctl, int workOrderId, int employeeId, int? teamId, DateTime workDate, decimal regularHours, decimal overtimeHours, string? notes, string? correctionReason = null)
        => Run(ctl, "work-order-add-labor", ("workOrderId", workOrderId.ToString()), ("employeeId", employeeId.ToString()), ("teamId", teamId?.ToString()), ("workDate", workDate.ToString("O")),
            ("regularHours", regularHours.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("overtimeHours", overtimeHours.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("notes", notes), ("correctionReason", correctionReason));
}
