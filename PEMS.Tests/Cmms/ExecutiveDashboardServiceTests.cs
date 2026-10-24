using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>داشبورد مدیریتی: گروه‌بندی وضعیت، تعریف رد/بازگشته، دیسیپلین، فیلتر پروژه، سری رویژن، ردیف «سایر»، توجه مدیریت و ایزوله‌بودن خطای هر بخش.</summary>
public class ExecutiveDashboardServiceTests
{
    private static ExecutiveDashboardService NewService(PEMSDbContext ctx, IVDRReportService? vdr = null)
        => new(ctx, vdr ?? new VDRReportService(ctx), new ProcurementReportService(ctx), new CmmsReportService(ctx), new CorrespondenceReportService(ctx),
            NullLogger<ExecutiveDashboardService>.Instance);

    private static PEMSDbContext NewContext(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("Exec_" + name + Guid.NewGuid().ToString("N"));
        ctx.Projects.AddRange(
            new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "Main plant" },
            new Project { ProjectId = 2, ProjectCode = "P2", ProjectName = "Small job" });
        ctx.Disciplines.AddRange(
            new Discipline { DisciplineId = 1, Code = "ME", Name = "Mechanical", IsActive = true },
            new Discipline { DisciplineId = 2, Code = "EL", Name = "Electrical", IsActive = true });
        ctx.SaveChanges();
        return ctx;
    }

    private static int _seq;

    private static EngineeringDocument Doc(string status, int project = 1, int? discipline = 1, string? comment = null, bool active = true,
        DateTime? planned = null, DateTime? actual = null)
        => new()
        {
            DocumentNo = "D-" + (++_seq), Title = "Doc " + _seq, DocumentType = "DWG", ProjectId = project, DisciplineId = discipline,
            Status = status, CommentStatus = comment, IsActive = active, PlannedDate = planned, ActualDate = actual
        };

    private static async Task<ExecutiveDashboardDto> Run(PEMSDbContext ctx, ExecutiveDashboardRequest? request = null, IVDRReportService? vdr = null)
        => await NewService(ctx, vdr).GetAsync(request ?? new ExecutiveDashboardRequest());

    // ------------------------------------------------------------------ status buckets

    [Fact]
    public async Task Engineering_GroupsStatusesIntoManagementBuckets_AndSeparatesRejectedFromReturnedWithComments()
    {
        var ctx = NewContext("buckets");
        ctx.EngineeringDocuments.AddRange(
            Doc("Approved"), Doc("Issued"), Doc("Client Approved"),
            Doc("Submitted"), Doc("Checked"), Doc("IDC Review"),
            Doc("Rejected", comment: null), Doc("Rejected", comment: "RE"), Doc("Rejected", comment: "CO"),
            Doc("Draft"), Doc("Draft"), Doc("Draft"),
            Doc("Archived"),
            Doc("Approved", active: false));   // inactive documents never count
        await ctx.SaveChangesAsync();

        var e = (await Run(ctx)).Engineering!;

        Assert.Equal(13, e.Total);
        Assert.Equal((3, 3, 3, 3, 1), (e.Approved, e.InReview, e.Returned, e.Draft, e.Other));
        Assert.Equal(2, e.RejectedTrue);            // no code or RE = a real rejection
        Assert.Equal(1, e.ReturnedWithComments);    // CO/AC = needs a revision, not a rejection
        Assert.Equal(23, e.ApprovedPercent);        // 3 of 13
        Assert.Equal(new[] { "Approved", "InReview", "Returned", "Draft", "Other" }, e.Buckets.Select(b => b.Key));
        Assert.Equal(13, e.Buckets.Sum(b => b.Value));
    }

    [Fact]
    public async Task Engineering_EmptyBuckets_AreOmitted_AndAnEmptyRegisterIsAZeroedDashboard()
    {
        var ctx = NewContext("empty");

        var result = await Run(ctx);

        Assert.Equal(0, result.Engineering!.Total);
        Assert.Empty(result.Engineering.Buckets);
        Assert.Empty(result.Engineering.Disciplines);
        Assert.Empty(result.Attention);
        Assert.Empty(result.FailedSections);
    }

    [Fact]
    public async Task Engineering_OverdueNeedsAPlannedDateInThePastAndNoActualDate()
    {
        var ctx = NewContext("overdue");
        var past = DateTime.Now.AddDays(-10);
        ctx.EngineeringDocuments.AddRange(
            Doc("Draft", planned: past),                              // overdue
            Doc("Draft", planned: past, actual: past.AddDays(3)),     // delivered
            Doc("Draft", planned: DateTime.Now.AddDays(10)),          // not yet due
            Doc("Draft"));                                            // no plan: cannot be measured
        await ctx.SaveChangesAsync();

        var e = (await Run(ctx)).Engineering!;

        Assert.Equal(1, e.Overdue);
        Assert.Equal(75, e.Completeness.WithPlannedDatePercent);   // plans exist on three of the four documents
    }

    // ------------------------------------------------------------------ disciplines

    [Fact]
    public async Task Disciplines_AreRankedBySize_WithApprovalShare_AndUnassignedIsAnEmptyName()
    {
        var ctx = NewContext("disc");
        ctx.EngineeringDocuments.AddRange(
            Doc("Approved", discipline: 1), Doc("Draft", discipline: 1), Doc("Rejected", discipline: 1), Doc("Submitted", discipline: 1),
            Doc("Approved", discipline: 2),
            Doc("Draft", discipline: null), Doc("Draft", discipline: null));
        await ctx.SaveChangesAsync();

        var rows = (await Run(ctx)).Engineering!.Disciplines;

        Assert.Equal(new[] { "Mechanical", "", "Electrical" }, rows.Select(r => r.Name));
        var mech = rows[0];
        Assert.Equal((4, 1, 1, 1, 1, 25), (mech.Total, mech.Approved, mech.InReview, mech.Returned, mech.Draft, mech.ApprovedPercent));
        Assert.Equal(100, rows.Single(r => r.Name == "Electrical").ApprovedPercent);
    }

    [Fact]
    public async Task Disciplines_TopTenOnly()
    {
        var ctx = NewContext("disc12");
        for (var i = 3; i <= 14; i++) ctx.Disciplines.Add(new Discipline { DisciplineId = i, Code = "D" + i, Name = "Disc " + i, IsActive = true });
        await ctx.SaveChangesAsync();
        for (var i = 3; i <= 14; i++) ctx.EngineeringDocuments.Add(Doc("Draft", discipline: i));
        await ctx.SaveChangesAsync();

        Assert.Equal(10, (await Run(ctx)).Engineering!.Disciplines.Count);
    }

    [Fact]
    public async Task Restricted_Users_OnlySeeTheirOwnDisciplineAndUnassignedDocuments()
    {
        var ctx = NewContext("restrict");
        ctx.EngineeringDocuments.AddRange(Doc("Draft", discipline: 1), Doc("Draft", discipline: 1), Doc("Draft", discipline: 2), Doc("Draft", discipline: null));
        await ctx.SaveChangesAsync();

        var restricted = (await Run(ctx, new ExecutiveDashboardRequest { RestrictToOwnDiscipline = true, OwnDisciplineId = 1 })).Engineering!;
        var noDiscipline = (await Run(ctx, new ExecutiveDashboardRequest { RestrictToOwnDiscipline = true, OwnDisciplineId = null })).Engineering!;
        var everyone = (await Run(ctx)).Engineering!;

        Assert.Equal(3, restricted.Total);     // 2 own + 1 unassigned, never discipline 2
        Assert.Equal(1, noDiscipline.Total);   // an employee without a discipline sees only unassigned documents (not everything, not nothing)
        Assert.Equal(4, everyone.Total);
        Assert.DoesNotContain(restricted.Disciplines, d => d.Name == "Electrical");
    }

    // ------------------------------------------------------------------ projects

    [Fact]
    public async Task ProjectFilter_ScopesEverything_ButTheProjectListStillOffersEveryProject()
    {
        var ctx = NewContext("filter");
        ctx.EngineeringDocuments.AddRange(Doc("Approved", project: 1), Doc("Draft", project: 1), Doc("Draft", project: 1), Doc("Approved", project: 2));
        await ctx.SaveChangesAsync();

        var all = await Run(ctx);
        var one = await Run(ctx, new ExecutiveDashboardRequest { ProjectId = 2 });

        Assert.Equal(4, all.Engineering!.Total);
        Assert.Equal(1, one.Engineering!.Total);
        Assert.Equal(100, one.Engineering.ApprovedPercent);
        Assert.Equal(new[] { 1, 2 }, one.Projects.Select(p => p.Id));   // ordered by size, filter ignored
        Assert.Empty(one.Engineering.Projects);                          // the ranking is pointless inside one project
        Assert.Equal(2, one.ProjectId);
    }

    [Fact]
    public async Task ProjectRanking_KeepsMaterialProjects_AndFoldsTheRestIntoOneOthersRow()
    {
        var ctx = NewContext("others");
        for (var i = 3; i <= 9; i++) ctx.Projects.Add(new Project { ProjectId = i, ProjectCode = "S" + i, ProjectName = "Tiny " + i });
        await ctx.SaveChangesAsync();
        for (var i = 0; i < 100; i++) ctx.EngineeringDocuments.Add(Doc("Draft", project: 1));
        for (var i = 3; i <= 9; i++) ctx.EngineeringDocuments.Add(Doc(i == 3 ? "Approved" : "Draft", project: i));   // 7 projects with 1 document: under 1% each
        await ctx.SaveChangesAsync();

        var rows = (await Run(ctx)).Engineering!.Projects;

        Assert.Equal(2, rows.Count);
        Assert.Equal("Main plant", rows[0].Name);
        var others = rows[1];
        Assert.Equal((0, 7, 7, 1), (others.ProjectId, others.OthersCount, others.Total, others.Approved));
        Assert.Equal(107, rows.Sum(r => r.Total));   // nothing is hidden
    }

    // ------------------------------------------------------------------ revisions

    private static DocumentRevision Rev(EngineeringDocument doc, DateTime? issued, bool active = true)
        => new() { Document = doc, RevisionNo = "0" + (++_seq), Status = "Approved", RevisionDate = issued ?? DateTime.Now, IssuedDate = issued, IsActive = active };

    [Fact]
    public async Task Issuance_IsAContinuousMonthlySeries_WithZeroMonthsAndARunningTotal()
    {
        var ctx = NewContext("issuance");
        var doc = Doc("Issued");
        ctx.EngineeringDocuments.Add(doc);
        ctx.DocumentRevisions.AddRange(
            Rev(doc, new DateTime(2024, 1, 5)), Rev(doc, new DateTime(2024, 1, 20)),
            Rev(doc, new DateTime(2024, 4, 2)),
            Rev(doc, null));   // not issued yet: not part of the issuance curve
        await ctx.SaveChangesAsync();

        var e = (await Run(ctx)).Engineering!;

        Assert.Equal(new[] { 2, 0, 0, 1 }, e.Issuance.Select(p => p.Issued));
        Assert.Equal(new[] { 2, 2, 2, 3 }, e.Issuance.Select(p => p.Cumulative));
        Assert.Equal(new DateTime(2024, 1, 1), e.Issuance[0].Date);
        Assert.Equal(new DateTime(2024, 4, 1), e.Issuance[^1].Date);
        Assert.Equal(4, e.TotalRevisions);
        Assert.Equal(3, e.IssuedRevisions);
    }

    [Fact]
    public async Task RevisionPressure_RanksDocumentsByRevisionCount_AndIgnoresInactiveRevisionsAndDocuments()
    {
        var ctx = NewContext("pressure");
        var busy = Doc("Draft"); var calm = Doc("Draft"); var other = Doc("Draft", project: 2); var gone = Doc("Draft", active: false);
        ctx.EngineeringDocuments.AddRange(busy, calm, other, gone);
        ctx.DocumentRevisions.AddRange(Rev(busy, null), Rev(busy, null), Rev(busy, null), Rev(calm, null), Rev(other, null), Rev(other, null), Rev(gone, null), Rev(gone, null),
            Rev(calm, null, active: false));
        await ctx.SaveChangesAsync();

        var all = (await Run(ctx)).Engineering!;
        var one = (await Run(ctx, new ExecutiveDashboardRequest { ProjectId = 1 })).Engineering!;

        Assert.Equal(new[] { busy.DocumentNo, other.DocumentNo, calm.DocumentNo }, all.RevisionPressure.Select(r => r.DocumentNo));
        Assert.Equal(new[] { 3, 2, 1 }, all.RevisionPressure.Select(r => r.Revisions));
        Assert.Equal(new[] { busy.DocumentNo, calm.DocumentNo }, one.RevisionPressure.Select(r => r.DocumentNo));
        Assert.Equal(Math.Round(6.0 / 3, 2), all.RevisionsPerDocument);   // 6 active revisions over 3 active documents
    }

    [Fact]
    public async Task ClientResponses_CountRealCodesOnly_MostFrequentFirst()
    {
        var ctx = NewContext("codes");
        ctx.EngineeringDocuments.AddRange(Doc("Draft", comment: "FINAL"), Doc("Draft", comment: "FINAL"), Doc("Draft", comment: "FINAL"), Doc("Draft", comment: "CO"),
            Doc("Draft", comment: ""), Doc("Draft", comment: null));
        await ctx.SaveChangesAsync();

        var e = (await Run(ctx)).Engineering!;

        Assert.Equal(new[] { ("FINAL", 3), ("CO", 1) }, e.ClientResponses.Select(c => (c.Label, c.Value)));
        Assert.Equal(67, e.Completeness.WithClientResponsePercent);   // 4 of 6
    }

    // ------------------------------------------------------------------ section access and failure isolation

    [Fact]
    public async Task SectionsTheUserMayNotView_AreNullAndNeverQueried()
    {
        var ctx = NewContext("access");
        ctx.EngineeringDocuments.Add(Doc("Draft"));
        await ctx.SaveChangesAsync();

        var none = await Run(ctx, new ExecutiveDashboardRequest
        {
            CanViewEngineering = false, CanViewVdr = false, CanViewProcurement = false, CanViewCmms = false, CanViewCorrespondence = false
        });

        Assert.Null(none.Engineering);
        Assert.Null(none.Vdr);
        Assert.Null(none.Procurement);
        Assert.Null(none.Transmittals);
        Assert.Null(none.Operations);
        Assert.Null(none.Correspondence);
        Assert.Empty(none.Projects);   // the project list is derived from documents the user may not see
        Assert.Empty(none.Attention);

        var onlyEngineering = await Run(ctx, new ExecutiveDashboardRequest { CanViewVdr = false, CanViewProcurement = false, CanViewCmms = false, CanViewCorrespondence = false });
        Assert.NotNull(onlyEngineering.Engineering);
        Assert.Null(onlyEngineering.Vdr);
    }

    private sealed class ThrowingVdr : IVDRReportService
    {
        public Task<VDRReportDto> GetReportAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
            => throw new InvalidOperationException("Invalid object name 'VDRItems'.");
    }

    [Fact]
    public async Task AFailingSection_IsReported_AndTheRestOfTheDashboardStillLoads()
    {
        var ctx = NewContext("failure");
        ctx.EngineeringDocuments.Add(Doc("Approved"));
        await ctx.SaveChangesAsync();

        var result = await Run(ctx, vdr: new ThrowingVdr());

        Assert.Equal(new[] { "Vdr" }, result.FailedSections);
        Assert.Null(result.Vdr);
        Assert.Equal(1, result.Engineering!.Total);
        Assert.NotNull(result.Procurement);
    }

    // ------------------------------------------------------------------ procurement, transmittals, correspondence

    [Fact]
    public async Task Procurement_CountsMaterialRequisitionsByStatus_AndPurchaseOrderValueByCurrency_ExcludingDraftsAndCancelled()
    {
        var ctx = NewContext("proc");
        ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "V" });
        ctx.MaterialRequisitions.AddRange(
            new MaterialRequisition { MRNumber = "MR-1", ProjectId = 1, Status = "Draft" }, new MaterialRequisition { MRNumber = "MR-2", ProjectId = 1, Status = "Draft" },
            new MaterialRequisition { MRNumber = "MR-3", ProjectId = 1, Status = "Closed" }, new MaterialRequisition { MRNumber = "MR-4", ProjectId = 2, Status = "Draft" });
        ctx.PurchaseOrders.AddRange(
            new PurchaseOrder { PoNumber = "PO-1", ProjectId = 1, VendorId = 1, Status = "Issued", Currency = "USD", Value = 100m },
            new PurchaseOrder { PoNumber = "PO-2", ProjectId = 1, VendorId = 1, Status = "Closed", Currency = "USD", Value = 50m },
            new PurchaseOrder { PoNumber = "PO-3", ProjectId = 1, VendorId = 1, Status = "Issued", Currency = "EUR", Value = 10m },
            new PurchaseOrder { PoNumber = "PO-4", ProjectId = 1, VendorId = 1, Status = "Draft", Currency = "USD", Value = 999m },
            new PurchaseOrder { PoNumber = "PO-5", ProjectId = 1, VendorId = 1, Status = "Cancelled", Currency = "USD", Value = 999m });
        await ctx.SaveChangesAsync();

        var p = (await Run(ctx, new ExecutiveDashboardRequest { ProjectId = 1 })).Procurement!;

        Assert.Equal(3, p.MaterialRequisitions);
        Assert.Equal(2, p.OpenMaterialRequisitions);
        Assert.Equal(new[] { ("Draft", 2), ("Closed", 1) }, p.MrByStatus.Select(m => (m.Label, m.Value)));
        Assert.Equal(3, p.PurchaseOrders);
        var usd = p.PurchaseOrderValue.Single(v => v.Currency == "USD");
        Assert.Equal((150m, 2), (usd.Value, usd.Count));
        Assert.Equal(10m, p.PurchaseOrderValue.Single(v => v.Currency == "EUR").Value);
    }

    [Fact]
    public async Task Transmittals_AreCountedByMonth_AndOnlyAProjectsOwnWhenFiltered()
    {
        var ctx = NewContext("trans");
        ctx.Transmittals.AddRange(
            new Transmittal { TransmittalNo = "T-1", ProjectId = 1, From = "A", To = "B", Status = "Sent", TransmittalDate = new DateTime(2026, 1, 3) },
            new Transmittal { TransmittalNo = "T-2", ProjectId = 1, From = "A", To = "B", Status = "Draft", TransmittalDate = new DateTime(2026, 3, 9) },
            new Transmittal { TransmittalNo = "T-3", ProjectId = 2, From = "A", To = "B", Status = "Sent", TransmittalDate = new DateTime(2026, 3, 9) });
        await ctx.SaveChangesAsync();

        var all = (await Run(ctx)).Transmittals!;
        var one = (await Run(ctx, new ExecutiveDashboardRequest { ProjectId = 1 })).Transmittals!;

        Assert.Equal((3, 2), (all.Total, all.Sent));
        Assert.Equal(new[] { 1, 0, 2 }, all.ByMonth.Select(m => m.Issued));
        Assert.Equal((2, 1), (one.Total, one.Sent));
    }

    [Fact]
    public async Task Correspondence_CountsOverdueResponsesOnlyForOpenUnansweredLetters()
    {
        var ctx = NewContext("letters");
        var yesterday = DateTime.Today.AddDays(-1);
        Letter L(string no, string status = "Sent", bool required = true, DateTime? due = null) => new()
        {
            LetterNumber = no, Subject = no, LetterType = "Outgoing", Status = status, ResponseRequired = required, ResponseDueDate = due ?? yesterday, IsActive = true
        };
        var overdue = L("OVERDUE");
        var answered = L("ANSWERED");
        ctx.Letters.AddRange(overdue, answered, L("CLOSED", status: "Closed"), L("NOTREQUIRED", required: false), L("FUTURE", due: DateTime.Today.AddDays(5)));
        await ctx.SaveChangesAsync();
        ctx.Letters.Add(new Letter { LetterNumber = "REPLY", Subject = "r", LetterType = "Incoming", Status = "Registered", ParentLetterId = answered.LetterId, IsActive = true });
        await ctx.SaveChangesAsync();

        var c = (await Run(ctx)).Correspondence!;

        Assert.Equal(1, c.OverdueResponses);   // only OVERDUE: the reply answers one, the rest are closed / not required / not due yet
    }

    // ------------------------------------------------------------------ attention + helpers

    [Fact]
    public void Attention_IsOrderedBySeverityThenSize_AndZeroesAreOmitted()
    {
        var dto = new ExecutiveDashboardDto
        {
            Engineering = new DashboardEngineeringDto { RejectedTrue = 5, Overdue = 0, ReturnedWithComments = 40, InReview = 9 },
            Vdr = new DashboardVdrDto { Overdue = 12, PendingResubmission = 0 },
            Operations = new DashboardOperationsDto { CriticalWorkOrders = 1 },
            Procurement = new DashboardProcurementDto { MrByStatus = { new DashboardCountDto { Label = "Draft", Value = 7 } } }
        };

        var items = ExecutiveDashboardService.BuildAttention(dto);

        Assert.Equal(new[] { "VdrOverdue", "EngRejected", "CriticalWorkOrders", "EngReturned", "EngInReview", "MrDraft" }, items.Select(i => i.Key));
        Assert.Equal(new[] { "High", "High", "High", "Medium", "Info", "Info" }, items.Select(i => i.Severity));
        Assert.DoesNotContain(items, i => i.Count == 0);
    }

    [Fact]
    public void FillMonths_ProducesNoGaps_AndMergesRowsOfTheSameMonth()
    {
        var rows = new[] { (new DateTime(2025, 11, 1), 2), (new DateTime(2026, 2, 1), 5), (new DateTime(2025, 11, 1), 1) };

        var points = ExecutiveDashboardService.FillMonths(rows);

        Assert.Equal(4, points.Count);
        Assert.Equal(new[] { 3, 0, 0, 5 }, points.Select(p => p.Issued));
        Assert.Equal(new[] { 3, 3, 3, 8 }, points.Select(p => p.Cumulative));
        Assert.Empty(ExecutiveDashboardService.FillMonths(Array.Empty<(DateTime, int)>()));
    }
}
