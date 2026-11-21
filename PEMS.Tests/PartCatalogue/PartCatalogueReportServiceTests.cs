using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Warehouse.Controllers;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Helpers;
using PEMS.Web.Resources;

namespace PEMS.Tests.PartCatalogue;

/// <summary>
/// SCRUM-478: Part Catalogue reports. Who did what (incl. registered part numbers), equipment coverage, stages, the period
/// (both custom dates inclusive), the request list's visibility rule, the workbook sheets and the View permission.
/// </summary>
public class PartCatalogueReportServiceTests
{
    private const int Requester = 1, Approver = 2, Cataloguer = 3, Other = 4;
    private static readonly DateTime Today = new(2026, 10, 31);

    private sealed class FakeActions : IRoleModuleActionService
    {
        public Task<bool> HasActionAsync(IEnumerable<string> userRoles, string module, string action) =>
            Task.FromResult(module == "PartCatalogue"
                && ((userRoles.Contains("PC") && action is "View" or "Review" or "Approve" or "Admin")
                    || (userRoles.Contains("PCV") && action == "View")));
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name + ":" + string.Join(",", arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private sealed class User : ICurrentUserContext
    {
        public int? UserId { get; init; } = 100;
        public int? EmployeeId { get; init; } = Requester;
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        // the rest of the interface is not used by the report controller
        public bool IsAuthenticated => true;
        public string? UserName => "u";
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required PartCatalogueReportService Svc { get; init; }
        public int Pump, Valve;
    }

    private static PcActor All(int emp = Approver) => new(emp, emp + 100, new[] { "PC" });
    private static PcActor ViewOnly(int emp) => new(emp, emp + 100, new[] { "PCV" });

    private static async Task<Env> SeedAsync()
    {
        var db = PemsDbContextFactory.CreateInMemory("pcrpt_" + Guid.NewGuid().ToString("N"));
        db.Employees.AddRange(
            new Employee { EmployeeId = Requester, FirstName = "Reza", LastName = "Requester" },
            new Employee { EmployeeId = Approver, FirstName = "Ali", LastName = "Approver" },
            new Employee { EmployeeId = Cataloguer, FirstName = "Sara", LastName = "Cataloguer" },
            new Employee { EmployeeId = Other, FirstName = "Omid", LastName = "Other" });
        var pump = new Equipment { TagNumber = "P-101", Name = "Feed pump" };
        var valve = new Equipment { TagNumber = "V-201", Name = "Control valve" };
        db.Equipments.AddRange(pump, valve);
        db.PartCatalogueWorkflowSteps.Add(new PartCatalogueWorkflowStep { StepKey = "SPR", StepOrder = 1, NameEn = "Spare parts review", NameFa = "بررسی قطعات" });
        await db.SaveChangesAsync();

        // 1: completed in October, part number registered by the cataloguer, on the pump
        var r1 = Req(1, Requester, PartCatalogueStatuses.Completed, new DateTime(2026, 10, 2), "PN-1", submitted: new DateTime(2026, 10, 3), completed: new DateTime(2026, 10, 13));
        r1.IsCritical = true;
        r1.Equipment.Add(new PartCatalogueRequestEquipment { EquipmentId = pump.EquipmentId, InstalledQuantity = 2 });
        r1.Attributes.Add(new PartCatalogueRequestAttribute { Name = "Size", Value = "DN50", SortOrder = 1 });
        r1.Approvals.Add(Act(Requester, PartCatalogueActions.Submit, new DateTime(2026, 10, 3)));
        r1.Approvals.Add(Act(Approver, PartCatalogueActions.Approve, new DateTime(2026, 10, 5)));
        r1.Approvals.Add(Act(Cataloguer, PartCatalogueActions.RegisterPartNumber, new DateTime(2026, 10, 13)));

        // 2: in approval at step SPR, on the pump and on a free-text equipment
        var r2 = Req(2, Requester, PartCatalogueStatuses.InApproval, new DateTime(2026, 10, 20), null, submitted: new DateTime(2026, 10, 21));
        r2.CurrentStepKey = "SPR";
        r2.Equipment.Add(new PartCatalogueRequestEquipment { EquipmentId = pump.EquipmentId, InstalledQuantity = 1 });
        r2.Equipment.Add(new PartCatalogueRequestEquipment { EquipmentText = "new compressor", InstalledQuantity = 1 });
        r2.Approvals.Add(Act(Requester, PartCatalogueActions.Submit, new DateTime(2026, 10, 21)));

        // 3: another person's request, returned, on the valve
        var r3 = Req(3, Other, PartCatalogueStatuses.Returned, new DateTime(2026, 10, 31, 18, 0, 0), null, submitted: new DateTime(2026, 10, 31, 19, 0, 0));
        r3.RequestType = PartCatalogueRequestTypes.Change;
        r3.Equipment.Add(new PartCatalogueRequestEquipment { EquipmentId = valve.EquipmentId, InstalledQuantity = 4 });
        r3.Approvals.Add(Act(Other, PartCatalogueActions.Submit, new DateTime(2026, 10, 31, 19, 0, 0)));
        r3.Approvals.Add(Act(Approver, PartCatalogueActions.Return, new DateTime(2026, 10, 31, 20, 0, 0)));

        // 4: September, completed (outside an October period)
        var r4 = Req(4, Requester, PartCatalogueStatuses.Completed, new DateTime(2026, 9, 10), "PN-4", submitted: new DateTime(2026, 9, 10), completed: new DateTime(2026, 9, 30));
        r4.Equipment.Add(new PartCatalogueRequestEquipment { EquipmentId = valve.EquipmentId, InstalledQuantity = 1 });
        r4.Approvals.Add(Act(Cataloguer, PartCatalogueActions.RegisterPartNumber, new DateTime(2026, 9, 30)));

        // 5: November 1st (just after the period)
        var r5 = Req(5, Requester, PartCatalogueStatuses.Draft, new DateTime(2026, 11, 1), null);

        var created = new[] { r1, r2, r3, r4, r5 }.ToDictionary(x => x, x => x.CreatedDate);
        db.PartCatalogueRequests.AddRange(r1, r2, r3, r4, r5);
        await db.SaveChangesAsync();
        // PEMSDbContext stamps CreatedDate with "now" on insert; put the seeded dates back
        foreach (var (req, date) in created) req.CreatedDate = date;
        await db.SaveChangesAsync();
        return new Env { Db = db, Svc = new PartCatalogueReportService(db, new FakeActions()), Pump = pump.EquipmentId, Valve = valve.EquipmentId };
    }

    private static PartCatalogueRequest Req(int n, int requester, string status, DateTime created, string? partNumber,
        DateTime? submitted = null, DateTime? completed = null) => new()
    {
        RequestNumber = "PCR-" + n, RequesterEmployeeId = requester, Status = status, CreatedDate = created, SubmittedDate = submitted,
        CompletedDate = completed, AssignedPartNumber = partNumber, ManufacturerPartNumber = "MPN-" + n
    };

    private static PartCatalogueRequestApproval Act(int who, string action, DateTime when) =>
        new() { ActorEmployeeId = who, Action = action, ActionDate = when };

    private static PartCatalogueReportFilter October(Action<PartCatalogueReportFilter>? change = null)
    {
        var f = new PartCatalogueReportFilter { Period = PartCatalogueReportPeriods.Custom, From = new DateTime(2026, 10, 1), To = new DateTime(2026, 10, 31) };
        change?.Invoke(f);
        return f;
    }

    [Fact]
    public async Task Activity_CountsWhatEachPersonDid_IncludingRegisteredPartNumbers()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(), All(), Today);

        var cat = Assert.Single(r.Activity.People, p => p.EmployeeId == Cataloguer);
        Assert.Equal(1, cat.PartNumbersRegistered);           // September registration is outside the period
        Assert.Equal("Sara Cataloguer", cat.EmployeeName);
        var req = r.Activity.People.Single(p => p.EmployeeId == Requester);
        Assert.Equal(2, req.Created);                         // r1 and r2 (r4 is September, r5 November)
        Assert.Equal(2, req.Submitted);
        var app = r.Activity.People.Single(p => p.EmployeeId == Approver);
        Assert.Equal((1, 1), (app.Approved, app.Returned));
        Assert.Equal(cat.EmployeeId, r.Activity.People[0].EmployeeId);   // most registrations first

        var reg = Assert.Single(r.Activity.Rows, x => x.Action == PartCatalogueActions.RegisterPartNumber);
        Assert.Equal(("PCR-1", "PN-1"), (reg.RequestNumber, reg.AssignedPartNumber));
    }

    [Fact]
    public async Task CustomPeriod_IncludesBothDates_AndExcludesTheDaysAround()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(), All(), Today);

        Assert.Contains(r.Activity.Rows, x => x.RequestId > 0 && x.RequestNumber == "PCR-3" && x.Action == "Create");   // 31 Oct 18:00
        Assert.DoesNotContain(r.Activity.Rows, x => x.RequestNumber is "PCR-4" or "PCR-5");
        Assert.Equal(3, r.Pipeline.Total);
        Assert.Equal(new DateTime(2026, 10, 1), r.PeriodStart);
        Assert.Equal(new DateTime(2026, 11, 1), r.PeriodEnd);

        var all = await env.Svc.GetReportAsync(new PartCatalogueReportFilter { Period = PartCatalogueReportPeriods.All }, All(), Today);
        Assert.Equal(5, all.Pipeline.Total);
    }

    [Theory]
    [InlineData(PartCatalogueReportPeriods.ThisMonth, "2026-10-01", "2026-11-01")]
    [InlineData(PartCatalogueReportPeriods.LastMonth, "2026-09-01", "2026-10-01")]
    [InlineData(PartCatalogueReportPeriods.ThisQuarter, "2026-10-01", "2027-01-01")]
    [InlineData(PartCatalogueReportPeriods.ThisYear, "2026-01-01", "2027-01-01")]
    [InlineData("nonsense", "2026-10-01", "2026-11-01")]
    public void Period_presets_resolve_to_half_open_ranges(string period, string start, string end)
    {
        var (s, e) = PartCatalogueReportPeriods.Resolve(period, null, null, Today);
        Assert.Equal((DateTime.Parse(start), DateTime.Parse(end)), (s!.Value, e!.Value));
    }

    [Fact]
    public void Custom_period_with_swapped_dates_is_put_in_order()
    {
        var (s, e) = PartCatalogueReportPeriods.Resolve(PartCatalogueReportPeriods.Custom, new DateTime(2026, 10, 31), new DateTime(2026, 10, 1), Today);
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)), (s!.Value, e!.Value));
    }

    [Fact]
    public async Task EquipmentCoverage_CountsOnlyCompletedPartNumbersAsRegistered()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(), All(), Today);

        var pump = Assert.Single(r.Equipment.Equipment, e => e.EquipmentId == env.Pump);
        Assert.Equal((2, 1, 1, 1), (pump.Requests, pump.RegisteredPartNumbers, pump.InProgress, pump.Critical));
        var valve = Assert.Single(r.Equipment.Equipment, e => e.EquipmentId == env.Valve);
        Assert.Equal((1, 0, 1), (valve.Requests, valve.RegisteredPartNumbers, valve.InProgress));   // September's PN-4 is outside
        var freeText = Assert.Single(r.Equipment.Equipment, e => !e.InRegister);
        Assert.Equal("new compressor", freeText.EquipmentTag);

        var part = Assert.Single(r.Equipment.Parts, p => p.AssignedPartNumber == "PN-1");
        Assert.Equal(("P-101", 2m, "Size: DN50", true), (part.EquipmentTag, part.InstalledQuantity, part.Attributes, part.IsRegistered));
        Assert.Equal(pump.EquipmentId, r.Equipment.Equipment[0].EquipmentId);   // most registered part numbers first
    }

    [Fact]
    public async Task Stages_AddUpToTheTotal_WithStepsAgeAndCycleTime()
    {
        var env = await SeedAsync();
        var p = (await env.Svc.GetReportAsync(October(), All(), Today)).Pipeline;

        Assert.Equal(p.Total, p.ByStatus.Sum(s => s.Count));
        Assert.Equal((2, 1), (p.Add, p.Change));
        Assert.Equal(2, p.Open);
        var step = Assert.Single(p.ByStep);
        Assert.Equal(("SPR", "Spare parts review", "بررسی قطعات", 1), (step.Key, step.Label, step.LabelFa, step.Count));
        Assert.Equal(1, p.CompletedInPeriod);
        Assert.Equal(1, p.PartNumbersRegisteredInPeriod);
        Assert.Equal(10.0, p.AverageDaysToComplete);   // submitted 3 Oct, completed 13 Oct
        Assert.Equal(p.Open, p.OpenAge.Sum(a => a.Count));
        var oct = Assert.Single(p.Months);
        Assert.Equal(("2026-10", 3, 1), (oct.Month, oct.Created, oct.Completed));
    }

    [Fact]
    public async Task ViewOnlyUser_SeesOnlyTheirOwnRequests()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(), ViewOnly(Other), Today);

        Assert.False(r.SeesAll);
        Assert.Equal(1, r.Pipeline.Total);
        Assert.All(r.Activity.Rows, x => Assert.Equal("PCR-3", x.RequestNumber));
        Assert.DoesNotContain(r.Equipment.Equipment, e => e.EquipmentId == env.Pump);
        Assert.True((await env.Svc.GetReportAsync(October(), All(), Today)).SeesAll);
    }

    [Fact]
    public async Task AccountWithoutEmployeeRecord_SeesAllWithAdminRights_AndNothingWithViewOnly()
    {
        var env = await SeedAsync();
        Assert.Equal(3, (await env.Svc.GetReportAsync(October(), new PcActor(0, 1, new[] { "PC" }), Today)).Pipeline.Total);
        Assert.Equal(0, (await env.Svc.GetReportAsync(October(), new PcActor(0, 1, new[] { "PCV" }), Today)).Pipeline.Total);
    }

    [Fact]
    public async Task PersonFilter_LimitsActivityToTheirActions_AndEquipmentToTheirRequests()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(f => f.EmployeeId = Approver), All(), Today);
        Assert.All(r.Activity.Rows, x => Assert.Equal(Approver, x.EmployeeId));
        Assert.Equal(2, r.Activity.Rows.Count);
        Assert.Empty(r.Equipment.Equipment);   // the approver raised no request

        var mine = await env.Svc.GetReportAsync(October(f => f.EmployeeId = Requester), All(), Today);
        Assert.Equal(2, mine.Pipeline.Total);
        Assert.DoesNotContain(mine.Equipment.Equipment, e => e.EquipmentId == env.Valve);
    }

    [Fact]
    public async Task Workbook_HasOneSheetPerTab_AndTheRowsOfTheReport()
    {
        var env = await SeedAsync();
        var r = await env.Svc.GetReportAsync(October(), All(), Today);
        var t = new PartCatalogueReportTexts(new KeyLocalizer(), rightToLeft: true);

        using (var wb = new XLWorkbook(new MemoryStream(PartCatalogueReportExport.Build(r, t, null))))
        {
            Assert.Equal(new[] { "PcRpt_Sheet_Summary", "PcRpt_Sheet_People", "PcRpt_Sheet_Activity", "PcRpt_Sheet_Equipment", "PcRpt_Sheet_Parts", "PcRpt_Sheet_Stages" },
                wb.Worksheets.Select(w => w.Name).ToArray());
            Assert.All(wb.Worksheets, w => Assert.True(w.RightToLeft));
            var people = wb.Worksheet("PcRpt_Sheet_People");
            Assert.Equal("PcRpt_Col_Registered", people.Cell(1, 7).GetString());
            Assert.Equal(r.Activity.People.Count + 1, people.LastRowUsed()!.RowNumber());
            var parts = wb.Worksheet("PcRpt_Sheet_Parts");
            Assert.Equal(r.Equipment.Parts.Count + 1, parts.LastRowUsed()!.RowNumber());
        }

        using var one = new XLWorkbook(new MemoryStream(PartCatalogueReportExport.Build(r, t, "equipment")));
        Assert.Equal("PcRpt_Sheet_Equipment", Assert.Single(one.Worksheets).Name);
    }

    [Fact]
    public async Task Workbook_AllTimePeriodIsNotRepeated()
    {
        var env = await SeedAsync();
        var report = await env.Svc.GetReportAsync(new PartCatalogueReportFilter { Period = PartCatalogueReportPeriods.All }, All(), Today);
        var t = new PartCatalogueReportTexts(new KeyLocalizer(), rightToLeft: false);
        Assert.Equal("PcRpt_Period_All", t.PeriodDisplay(report));
        using var wb = new XLWorkbook(new MemoryStream(PartCatalogueReportExport.Build(report, t, null)));
        Assert.Equal("PcRpt_Period_All", wb.Worksheet("PcRpt_Sheet_Summary").Cell(2, 2).GetString());
    }

    [Fact]
    public async Task Operations_RefuseAUserWithoutPartCatalogueView()
    {
        // SCRUM-661: the report is an operation now; a user without any Part Catalogue action is refused, the page and the workbook alike
        var env = await SeedAsync();
        var operations = new PEMS.Web.Services.PartCatalogueOperations(null!, null!, env.Svc, new FakeActions(), new KeyLocalizer(), env.Db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.ReportSummaryAsync(new List<string> { "Other" }, "nobody"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.ReportExportAsync(new List<string> { "Other" }, "nobody", "en"));
    }
}
