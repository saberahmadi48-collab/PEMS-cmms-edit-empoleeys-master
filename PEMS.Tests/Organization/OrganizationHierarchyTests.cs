using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.ActorResolution;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Organization;

/// <summary>Organization Hierarchy Phase 1: اعتبارسنجی ساختار، سرویس سلسله‌مراتب، Resolver مدیر مستقیم، Import، امنیت.</summary>
public class OrganizationHierarchyTests
{
    private sealed class NoActions : IRoleModuleActionService
    {
        public Task<bool> HasActionAsync(IEnumerable<string> userRoles, string module, string action) => Task.FromResult(true);
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private static PEMSDbContext NewDb() => PemsDbContextFactory.CreateInMemory("org_" + Guid.NewGuid().ToString("N"));

    private static async Task Employees(PEMSDbContext db, params int[] ids)
    {
        foreach (var id in ids)
            db.Employees.Add(new Employee { EmployeeId = id, FirstName = "E" + id, LastName = "T", PersonnelCode = "P" + id, DepartmentId = 1, DisciplineId = 1, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task<ActorResolutionResult> ResolveManager(PEMSDbContext db, int requester, int level = 1)
    {
        db.WorkflowActorResponsibilities.Add(new WorkflowActorResponsibility
        { ResponsibilityKey = "MGR", DisplayName = "MGR", Kind = ActorResponsibilityKinds.RequesterManager, ManagerLevel = level });
        await db.SaveChangesAsync();
        return await new EnterpriseActorResolver(db, new NoActions())
            .ResolveAsync(new ActorResolutionRequest("MGR", requester, new Dictionary<string, string?>()));
    }

    // ------------------------------------------------------------------ Resolver scenarios

    [Fact]
    public async Task A_ValidPosition_ResolvesParentHolder_WithExplainablePath()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3);
        await OrgSeed.ChainAsync(db, 1, 2, 3);
        var r = await ResolveManager(db, 1);
        Assert.Equal(ActorResolutionStates.Resolved, r.State);
        Assert.Equal(2, Assert.Single(r.SelectedEmployeeIds));
        Assert.Equal(new[] { "Employee", "Primary Position", "Parent Position", "Holder" }, r.ResolutionPath.Select(p => p.Split(':')[0].Trim()).ToArray());
        Assert.True(r.RequesterPositionId.HasValue && r.ResolvedPositionId.HasValue && r.RequesterPositionId != r.ResolvedPositionId);
    }

    [Fact]
    public async Task B_NoPosition_IsOrganizationResolutionRequired_NotGuessed()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        var r = await ResolveManager(db, 1);
        Assert.Equal(ActorResolutionStates.OrganizationResolutionRequired, r.State);
        Assert.Equal(OrganizationIssueCodes.EmployeeNoPrimaryPosition, r.OrganizationIssue);
        Assert.Empty(r.SelectedEmployeeIds);
    }

    [Fact]
    public async Task C_InactiveParentPosition_Blocks()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        (await db.Positions.FindAsync(ps[1]))!.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.ParentPositionInactive, (await ResolveManager(db, 1)).OrganizationIssue);
    }

    [Fact]
    public async Task D_VacantParent_Blocks_AndDoesNotFallBackToAnyoneElse()
    {
        var db = NewDb(); await Employees(db, 1, 2, 9);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        var a = await db.EmployeeAssignments.SingleAsync(x => x.EmployeeId == 2); a.IsActive = false; a.EndDate = DateTime.Now.AddDays(-1);
        await db.SaveChangesAsync();
        var r = await ResolveManager(db, 1);
        Assert.Equal(ActorResolutionStates.OrganizationResolutionRequired, r.State);
        Assert.Equal(OrganizationIssueCodes.ParentPositionVacant, r.OrganizationIssue);
        Assert.Empty(r.Candidates);
    }

    [Fact]
    public async Task E_RootRequester_HasNoDirectManager()
    {
        var db = NewDb(); await Employees(db, 1);
        await OrgSeed.ChainAsync(db, 1);
        Assert.Equal(OrganizationIssueCodes.RootPosition, (await ResolveManager(db, 1)).OrganizationIssue);
    }

    [Fact]
    public async Task F_Cycle_IsDetected_NotLoopedForever()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3);
        var ps = await OrgSeed.ChainAsync(db, 1, 2, 3);
        (await db.Positions.FindAsync(ps[2]))!.ParentPositionId = ps[0]; await db.SaveChangesAsync();   // 1→2→3→1
        var r = await ResolveManager(db, 1, level: 3);
        Assert.Equal(OrganizationIssueCodes.HierarchyCycle, r.OrganizationIssue);
    }

    [Fact]
    public async Task G_MultiplePrimaryPositions_Blocks()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        await OrgSeed.ChainAsync(db, 1, 2);
        var extra = new Position { Title = "Extra", IsActive = true }; db.Positions.Add(extra); await db.SaveChangesAsync();
        db.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = extra.PositionId, EmployeeId = 1, IsActive = true, IsPrimary = true, StartDate = DateTime.Now.AddDays(-2) });
        await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.MultiplePrimaryPositions, (await ResolveManager(db, 1)).OrganizationIssue);
    }

    [Fact]
    public async Task H_SecondaryAssignment_DoesNotDetermineManager()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3);
        await OrgSeed.ChainAsync(db, 1, 2);
        var other = new Position { Title = "Other", IsActive = true }; db.Positions.Add(other); await db.SaveChangesAsync();
        db.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = other.PositionId, EmployeeId = 1, IsActive = true, IsPrimary = false, AssignmentType = "Secondary", StartDate = DateTime.Now.AddDays(-2) });
        await db.SaveChangesAsync();
        Assert.Equal(2, Assert.Single((await ResolveManager(db, 1)).SelectedEmployeeIds));   // مدیر از سمت اصلی
    }

    [Fact]
    public async Task I_ExpiredAndFutureAssignments_AreNotEffective_InactiveHolderBlocks()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        await OrgSeed.ChainAsync(db, 1, 2);
        var holder = await db.EmployeeAssignments.SingleAsync(a => a.EmployeeId == 2);
        holder.EndDate = DateTime.Now.AddDays(-1); await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.AssignmentExpired, (await ResolveManager(db, 1)).OrganizationIssue);

        holder.EndDate = null; holder.StartDate = DateTime.Now.AddDays(5); await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.AssignmentExpired, (await new EnterpriseActorResolver(db, new NoActions())
            .ResolveAsync(new ActorResolutionRequest("MGR", 1, new Dictionary<string, string?>()))).OrganizationIssue);

        holder.StartDate = DateTime.Now.AddYears(-1); (await db.Employees.FindAsync(2))!.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.HolderEmployeeInactive, (await new EnterpriseActorResolver(db, new NoActions())
            .ResolveAsync(new ActorResolutionRequest("MGR", 1, new Dictionary<string, string?>()))).OrganizationIssue);
    }

    [Fact]
    public async Task SuperiorManager_IsGrandparent_AndTeamSupervisorIsNeverUsed()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3, 7);
        await OrgSeed.ChainAsync(db, 1, 2, 3);
        // employee 7 may supervise a team, but Team.SupervisorEmployeeId is never consulted for Direct/Superior manager
        Assert.Equal(3, Assert.Single((await ResolveManager(db, 1, level: 2)).SelectedEmployeeIds));
    }

    [Fact]
    public async Task Hook_Snapshot_ContainsPathPositionsAndIssue()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        await OrgSeed.ChainAsync(db, 1, 2);
        var r = await ResolveManager(db, 1);
        var json = PEMS.Persistence.Workflow.ActorResolutionWorkflowHook.ToJson(r);
        Assert.Contains("ResolutionPath", json); Assert.Contains("RequesterPositionId", json); Assert.Contains("ResolvedPositionId", json); Assert.Contains("ResolvedAt", json);
    }

    // ------------------------------------------------------------------ Validation service

    [Fact]
    public async Task Validator_ParentChange_RejectsSelfCycleAndInactive_AllowsRootAndValid()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3);
        var ps = await OrgSeed.ChainAsync(db, 1, 2, 3);
        var v = new OrganizationValidationService(db);
        Assert.Equal(OrganizationIssueCodes.SelfParent, await v.ValidateParentChangeAsync(ps[0], ps[0]));
        Assert.Equal(OrganizationIssueCodes.HierarchyCycle, await v.ValidateParentChangeAsync(ps[2], ps[0]));
        Assert.Null(await v.ValidateParentChangeAsync(ps[2], null));
        Assert.Null(await v.ValidateParentChangeAsync(ps[0], ps[2]));
        (await db.Positions.FindAsync(ps[1]))!.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(OrganizationIssueCodes.ParentPositionInactive, await v.ValidateParentChangeAsync(ps[0], ps[1]));
    }

    [Fact]
    public async Task Validator_ReportsVacancyInactiveHolderMultiplePrimaryAndMissingPrimary()
    {
        var db = NewDb(); await Employees(db, 1, 2, 3);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        // کارمند ۳ حساب کاربری فعال دارد ولی سمتی ندارد
        db.Users.Add(new PEMS.Persistence.Identity.ApplicationUser { UserName = "u3", NormalizedUserName = "U3", EmployeeId = 3, FullName = "u3", IsActive = true });
        (await db.Employees.FindAsync(2))!.IsActive = false;
        await db.SaveChangesAsync();
        var codes = (await new OrganizationValidationService(db).ValidateAllAsync()).Select(i => i.Code).ToHashSet();
        Assert.Contains(OrganizationIssueCodes.HolderEmployeeInactive, codes);
        Assert.Contains(OrganizationIssueCodes.EmployeeNoPrimaryPosition, codes);
        Assert.Contains(OrganizationIssueCodes.RootPosition, codes);
    }

    // ------------------------------------------------------------------ Hierarchy service

    [Fact]
    public async Task HierarchyService_EnforcesParentRules_UniquePrimary_OccupiedPosition_AndDates()
    {
        var db = NewDb(); await Employees(db, 1, 2);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        var svc = new OrganizationHierarchyService(db, new OrganizationValidationService(db));

        Assert.False((await svc.SetPositionHierarchyAsync(ps[1], null, ps[0])).Success);                    // دور
        Assert.False((await svc.SetPositionHierarchyAsync(ps[0], null, ps[0])).Success);                    // والدِ خود
        Assert.False((await svc.SetPositionHierarchyAsync(ps[0], "T-2", ps[1])).Success);                    // کد تکراری
        Assert.True((await svc.SetPositionHierarchyAsync(ps[0], "NEW-1", ps[1])).Success);

        var free = new Position { Title = "Free", IsActive = true }; db.Positions.Add(free); await db.SaveChangesAsync();
        Assert.False((await svc.AssignAsync(new OrganizationAssignmentRequest(ps[1], 1, false, DateTime.Now, null, null))).Success);   // اشغال‌شده
        Assert.False((await svc.AssignAsync(new OrganizationAssignmentRequest(free.PositionId, 1, true, DateTime.Now, null, null))).Success);   // اصلیِ تکراری
        Assert.False((await svc.AssignAsync(new OrganizationAssignmentRequest(free.PositionId, 1, false, DateTime.Now, DateTime.Now.AddDays(-3), null))).Success);   // تاریخ نامعتبر
        Assert.True((await svc.AssignAsync(new OrganizationAssignmentRequest(free.PositionId, 1, false, DateTime.Now, null, null))).Success);   // فرعی مجاز

        var secondary = await db.EmployeeAssignments.SingleAsync(a => a.PositionId == free.PositionId);
        Assert.False((await svc.UpdateAssignmentAsync(secondary.EmployeeAssignmentId, true, DateTime.Now, null)).Success);   // دومین اصلی
        Assert.True((await svc.EndAssignmentAsync(secondary.EmployeeAssignmentId, "x")).Success);
        Assert.False((await svc.EndAssignmentAsync(secondary.EmployeeAssignmentId, "x")).Success);
    }

    [Fact]
    public async Task LegacyAssignEmployee_FirstAssignmentIsPrimary_NextIsSecondary()
    {
        var db = NewDb(); await Employees(db, 1);
        var svc = new OrganizationStructureService(db);
        var p1 = await svc.CreatePositionAsync(null, null, "A", null, 0);
        var p2 = await svc.CreatePositionAsync(null, null, "B", null, 0);
        await svc.AssignEmployeeAsync(p1, 1, null);
        await svc.AssignEmployeeAsync(p2, 1, null);
        var all = await db.EmployeeAssignments.OrderBy(a => a.EmployeeAssignmentId).ToListAsync();
        Assert.True(all[0].IsPrimary); Assert.False(all[1].IsPrimary); Assert.Equal("Secondary", all[1].AssignmentType);
    }

    // ------------------------------------------------------------------ Import

    private static MemoryStream Xlsx(params string[][] rows)
    {
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Org");
        var header = new[] { "Department", "Position", "PositionCode", "PositionTitle", "ParentPositionCode", "EmployeePersonnelCode", "IsPrimary", "AssignmentType", "EffectiveFrom", "EffectiveTo" };
        for (var c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }

    private static async Task<PEMSDbContext> ImportDb()
    {
        var db = NewDb();
        db.Departments.Add(new Department { DepartmentId = 1, Name = "Ops", IsActive = true });
        await db.SaveChangesAsync();
        await Employees(db, 1, 2);
        return db;
    }

    [Fact]
    public async Task Import_ValidFile_PreviewDoesNotSave_ConfirmCreatesHierarchyAndAssignments()
    {
        var db = await ImportDb();
        string[] Top() => new[] { "Ops", "", "CEO", "Chief", "", "P2", "true", "Primary", "", "" };
        string[] Sub() => new[] { "Ops", "", "ENG", "Engineer", "CEO", "P1", "", "", "", "" };
        var svc = new OrganizationImportService(db);

        var preview = await svc.ImportAsync(Xlsx(Top(), Sub()), commit: false);
        Assert.Empty(preview.Errors); Assert.Equal(2, preview.PositionsCreated); Assert.Equal(2, preview.AssignmentsCreated); Assert.False(preview.Committed);
        Assert.Empty(db.Positions);

        var done = await svc.ImportAsync(Xlsx(Top(), Sub()), commit: true);
        Assert.True(done.Committed);
        var eng = await db.Positions.SingleAsync(p => p.PositionCode == "ENG");
        Assert.Equal((await db.Positions.SingleAsync(p => p.PositionCode == "CEO")).PositionId, eng.ParentPositionId);
        Assert.Equal(2, await db.EmployeeAssignments.CountAsync(a => a.IsActive && a.IsPrimary));

        // و Resolver از همین داده مدیر را می‌یابد
        Assert.Equal(2, Assert.Single((await ResolveManager(db, 1)).SelectedEmployeeIds));

        // تکرار Import ایدمپوتنت است
        var again = await svc.ImportAsync(Xlsx(Top(), Sub()), commit: true);
        Assert.Equal(0, again.PositionsCreated + again.PositionsUpdated + again.AssignmentsCreated + again.AssignmentsUpdated);
    }

    [Theory]
    [InlineData("unknown parent", "Ops", "ENG", "Engineer", "NOPE", "P1")]
    [InlineData("unknown employee", "Ops", "ENG", "Engineer", "", "P999")]
    [InlineData("unknown department", "Nowhere", "ENG", "Engineer", "", "P1")]
    [InlineData("self parent", "Ops", "ENG", "Engineer", "ENG", "P1")]
    public async Task Import_InvalidRows_AreRejected_NothingSaved(string _, string dept, string code, string title, string parent, string emp)
    {
        var db = await ImportDb();
        var r = await new OrganizationImportService(db).ImportAsync(Xlsx(new[] { dept, "", code, title, parent, emp, "", "", "", "" }), commit: true);
        Assert.NotEmpty(r.Errors); Assert.False(r.Committed);
        Assert.Empty(db.Positions); Assert.Empty(db.EmployeeAssignments);
    }

    [Fact]
    public async Task Import_Cycle_DuplicatePrimary_InactiveHolder_BadDates_AndMissingColumn_AreRejected()
    {
        var db = await ImportDb();
        (await db.Employees.FindAsync(2))!.IsActive = false; await db.SaveChangesAsync();
        var svc = new OrganizationImportService(db);

        var cycle = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "A", "A", "B", "", "", "", "", "" }, new[] { "Ops", "", "B", "B", "A", "", "", "", "", "" }), true);
        Assert.Contains(cycle.Errors, e => e.Contains("cycle"));

        var dupPrimary = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "A", "A", "", "P1", "true", "", "", "" }, new[] { "Ops", "", "B", "B", "", "P1", "true", "", "", "" }), true);
        Assert.Contains(dupPrimary.Errors, e => e.Contains("duplicate primary"));

        var inactive = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "A", "A", "", "P2", "", "", "", "" }), true);
        Assert.Contains(inactive.Errors, e => e.Contains("inactive"));

        var dates = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "A", "A", "", "P1", "", "", "2026-05-01", "2026-04-01" }), true);
        Assert.Contains(dates.Errors, e => e.Contains("earlier"));

        var dupCode = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "A", "One", "", "", "", "", "", "" }, new[] { "Ops", "", "A", "Two", "", "", "", "", "", "" }), true);
        Assert.Contains(dupCode.Errors, e => e.Contains("defined again"));

        Assert.Empty(db.Positions); Assert.Empty(db.EmployeeAssignments);

        using var ms = new MemoryStream(); var wb = new XLWorkbook(); wb.AddWorksheet("x").Cell(1, 1).Value = "Foo"; wb.SaveAs(ms); ms.Position = 0;
        Assert.Contains((await svc.ImportAsync(ms, false)).Errors, e => e.Contains("Missing required column"));
    }

    [Fact]
    public async Task Import_DoesNotReplaceExistingHolder_NorDuplicateAnExistingPrimary()
    {
        var db = await ImportDb();
        await OrgSeed.ChainAsync(db, 1, 2);                       // P1/T-1 held by 1 (primary), T-2 held by 2
        db.Positions.Add(new Position { Title = "Free", PositionCode = "FREE", IsActive = true, DepartmentId = 1 }); await db.SaveChangesAsync();
        var svc = new OrganizationImportService(db);

        var replace = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "T-2", "P2", "", "P1", "false", "", "", "" }), true);   // T-2 is held by employee 2
        Assert.Contains(replace.Errors, e => e.Contains("already occupied"));

        var dup = await svc.ImportAsync(Xlsx(new[] { "Ops", "", "FREE", "Free", "", "P1", "true", "", "", "" }), true);       // employee 1 already has a primary
        Assert.Contains(dup.Errors, e => e.Contains("already has an active primary"));
    }

    // ------------------------------------------------------------------ Security / architecture

    [Fact]
    public void PartCatalogueControllers_NeverAcceptApproverManagerOrPositionIdsFromTheBrowser()
    {
        var asm = typeof(PEMS.Web.Areas.Warehouse.Controllers.PartCatalogueRequestController).Assembly;
        var forbidden = new[] { "approverid", "managerid", "manageremployeeid", "approveremployeeid", "positionid", "parentpositionid", "resolvedemployeeid" };
        foreach (var t in new[] { "PEMS.Web.Areas.Warehouse.Controllers.PartCatalogueRequestController" })
            foreach (var m in asm.GetType(t)!.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                foreach (var p in m.GetParameters())
                    Assert.DoesNotContain(p.Name!.ToLowerInvariant(), forbidden);
    }

    [Fact]
    public void OrganizationControllers_AreSystemAdminOnly()
    {
        // SCRUM-639: the organization pages are operations (SystemAdmin only)
        var operations = PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly);
        foreach (var action in new[] { "CreatePosition", "UpdatePosition", "SetPositionActive", "SetPositionHierarchy", "Assign", "UpdateAssignment", "EndAssignment", "PositionRows", "Issues", "ChartRows" })
            Assert.Contains(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, operations.Get("Organization", action)!.Roles);
        // SCRUM-640: the import is operations too, SystemAdmin only
        foreach (var action in new[] { "Template", "Validate", "Pending", "Confirm" })
            Assert.Contains(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, operations.Get("OrganizationImport", action)!.Roles);
    }


    [Fact]
    public void Resolver_NoLongerReadsModuleLocalOrgLinksOrCeo()
    {
        string src(string rel)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, rel))) dir = dir.Parent;
            return File.ReadAllText(Path.Combine(dir!.FullName, rel));
        }
        var resolver = src("PEMS.Persistence/Servises/EnterpriseActorResolver.cs");
        Assert.DoesNotContain("PartCatalogueOrgLinks", resolver);
        Assert.DoesNotContain("CeoKey", resolver);
        var svc = src("PEMS.Persistence/Servises/PartCatalogueService.cs");
        Assert.DoesNotContain("PartCatalogueOrgLinks", svc);
        var admin = src("PEMS.Persistence/Servises/PartCatalogueAdminService.cs");
        Assert.DoesNotContain("PartCatalogueOrgLinks", admin);
    }
}
