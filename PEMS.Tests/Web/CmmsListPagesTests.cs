using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.ViewModels;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 wave 2 (CMMS): Work Orders, Work Requests, Equipment, Functional Locations, Instruments, Letter Tags,
/// Maintenance Plans, Maintenance Strategies, Job Plans, Spare Parts, Permits, Inspections, Findings and Assets are built from
/// the PEMS.UI list-page component. Each page keeps its own filters, columns, modals, actions and permissions; the old
/// hand-written frame (content cards, count cards, status button rows, inline column-resize script) is gone.
/// Wave 2b adds Criticality Assessments, Equipment Criticality, Mechanical Equipment, the Failure Mode Catalogue, Shift
/// Handovers, RCM / FMEA coverage and the Equipment / Functional Location assignment batches.
/// </summary>
public class CmmsListPagesTests
{
    private static readonly string[] Pages =
    {
        "WorkOrder", "Equipment", "FunctionalLocation", "Instrument", "Tag",
        "MaintenanceStrategy", "Asset",
        // wave 2b
        "CriticalityAssessment", "EquipmentCriticality", "MechanicalEquipment", "FailureModeCatalog", "RcmFmea",
        "EquipmentFunctionalLocationAssignment",
    };

    private static Task<string> Render<TController>(string view, object model, Action<ViewDataDictionary> data) where TController : Controller
    {
        // rendering only needs the controller's ViewData; its constructor (services) is not used
        var controller = (Controller)RuntimeHelpers.GetUninitializedObject(typeof(TController));
        data(controller.ViewData);
        var result = new ViewResult { ViewData = new ViewDataDictionary(controller.ViewData) { Model = model } };
        return RazorViewRenderer.RenderAsync(controller, result, view);
    }

    private static PagedResult<T> Page<T>(params T[] items) => new() { Items = items.ToList(), Page = 1, PageSize = 25, TotalCount = items.Length };

    // the controllers pass anonymous objects read through dynamic; ExpandoObject behaves the same across assemblies
    private static dynamic Obj(params (string Key, object? Value)[] values)
    {
        IDictionary<string, object?> o = new ExpandoObject();
        foreach (var (k, v) in values) o[k] = v;
        return o;
    }

    /// <summary>The shared frame. <paramref name="gridKey"/> null = a short list without stored column widths or paging.</summary>
    private static void AssertBuiltFromTheComponent(string html, string? gridKey, int rows, bool keepsDataBadges = false)
    {
        Assert.Contains("class=\"lp-page\"", html);
        Assert.Contains("class=\"lp-toolbar\"", html);
        Assert.Equal(rows, Regex.Matches(html, "<tr class=\"lp-row[ \"]").Count);
        if (gridKey != null)
        {
            Assert.Matches($"<table class=\"[^\"]*lp-table[^\"]*\" data-grid-key=\"{gridKey}\"", html);
            Assert.Contains($"data-lp-reset-columns=\"{gridKey}\"", html);
            Assert.Contains("class=\"lp-footer\"", html);
        }
        // the old hand-written frame is gone
        Assert.DoesNotContain("content-card", html);
        Assert.DoesNotContain("PemsDataGrid.initColumnResize", html);
        Assert.DoesNotContain("btn-group btn-group-sm flex-wrap", html);       // the old status button row (paging keeps its own group)
        // status badges became tone pills; a badge stays only where its colour is data (tag colour, RCM criticality helpers)
        if (!keepsDataBadges) Assert.DoesNotContain("class=\"badge ", html);
    }

    private static string Chips(string html) => Regex.Match(html, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline).Value;
    private static string Kpis(string html, string id) => Regex.Match(html, $"id=\"{id}\" hidden(?:=\"hidden\")?>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;

    // ---------- Work orders ----------

    private static Task<string> WorkOrders(string? status = null, int? equipmentId = null, bool mine = false, params WorkOrderListDto[] items)
        => Render<WorkOrderController>("/Views/WorkOrder/Index.cshtml", Page(items), v =>
        {
            v["SelectedStatus"] = status;
            v["SelectedEquipmentId"] = equipmentId;
            v["SelectedEquipmentLabel"] = equipmentId.HasValue ? "P-101A" : null;
            v["AssignedToMe"] = mine;
            v["OpenCount"] = 3; v["InProgressCount"] = 2; v["CompletedCount"] = 9; v["CriticalOpenCount"] = 1;
        });

    [Fact]
    public async Task WorkOrders_CountsAreKpiLinks_StatusesAreLocalisedPills_AndTheEquipmentSearchFeedsTheFilter()
    {
        var html = await WorkOrders(items: new WorkOrderListDto { WorkOrderId = 4, WorkOrderNumber = "WO-0004", EquipmentTagNumber = "P-101A", WorkOrderType = "CM", Priority = "Critical", Status = "InProgress", CreatedDate = new DateTime(2026, 10, 5) });

        AssertBuiltFromTheComponent(html, "WorkOrder", 1);
        var kpis = Kpis(html, "woKpis");
        Assert.Equal(4, Regex.Matches(kpis, "class=\"vs-chip").Count);
        Assert.Equal(3, Regex.Matches(kpis, "<a ").Count);                     // open / in progress / completed stay filter links
        Assert.Equal(2, Regex.Matches(kpis, "vs-chip--danger").Count);           // open and critical-open above zero
        Assert.Contains(">In progress<", html.Replace("> ", ">"));               // Wo_St_InProgress, not the stored value
        Assert.Contains("vs-pill vs-pill--danger", html);                        // critical priority
        Assert.Contains("data-search-type=\"Equipment\"", html);
        Assert.Contains("id=\"woEquipmentFilterHidden\"", html);
        Assert.Contains("lp-compose", html);
    }

    [Fact]
    public async Task WorkOrders_ActiveFilters_ShowTheBadgeAndOneChipEach()
    {
        var html = await WorkOrders("Open", 7, mine: true);

        Assert.Matches("<span class=\"lp-badge\">3<", html);
        var chips = Chips(html);
        Assert.Equal(3, Regex.Matches(chips, "class=\"pems-filter-chip\"").Count);
        Assert.Contains("P-101A", chips);
        Assert.Matches("id=\"assignedToMeCheck\"[^>]*checked=\"checked\"", html);
        Assert.Contains("class=\"lp-empty\"", html);
    }

    // ---------- Work requests ----------

    // ---------- Letter tags ----------

    [Fact]
    public async Task Tags_CreateMovedIntoAModal_AndEachTagKeepsItsColourAndDeleteForm()
    {
        var html = await Render<TagController>("/Views/Tag/Index.cshtml",
            new List<TagDto> { new() { TagId = 1, Name = "Urgent", Color = "danger", LetterCount = 4 } }, _ => { });

        AssertBuiltFromTheComponent(html, null, 1, keepsDataBadges: true);
        Assert.Contains("data-bs-target=\"#createTagModal\"", html);
        Assert.Contains("id=\"createTagModal\"", html);
        Assert.Contains("class=\"badge bg-danger\"", html);
        Assert.Contains("4 letters", html);
        Assert.Contains("name=\"tagId\" value=\"1\"", html);
        Assert.Contains("data-pems-confirm=", html);
    }

    // ---------- Inspections ----------

    // ---------- Findings ----------

    // ---------- Maintenance plans ----------

    // ---------- Job plans ----------

    // ---------- Permits ----------

    // ---------- Functional locations ----------

    [Fact]
    public async Task FunctionalLocations_EditButtonsCarryTheRow_AndBothModalsStay()
    {
        var html = await Render<FunctionalLocationController>("/Views/FunctionalLocation/Index.cshtml",
            Page(new FunctionalLocationListDto { FunctionalLocationId = 6, Code = "U100-CW", Name = "Cooling water", PathBreadcrumb = "JPC / U100 / U100-CW", Plant = "JPC", Unit = "U100" }),
            v => v["PickerList"] = new List<FunctionalLocationPickerItemDto>());

        AssertBuiltFromTheComponent(html, "FunctionalLocation", 1);
        Assert.Contains("data-code=\"U100-CW\"", html);
        Assert.Contains("id=\"createFlModal\"", html);
        Assert.Contains("id=\"editFlModal\"", html);
        Assert.Contains("lp-act lp-act--always edit-fl-btn", html);               // the edit script finds the buttons by this class
    }

    // ---------- Instruments ----------

    [Fact]
    public async Task Instruments_CountsAreKpis_TheExportSaysItIsEveryRow_AndCalibrationIsAPill()
    {
        var html = await Render<InstrumentController>("/Views/Instrument/Index.cshtml",
            Page(new InstrumentListDto { InstrumentId = 1, EquipmentId = 3, TagNumber = "PT-1001", InstrumentType = "PT", CalibrationStatus = "DueSoon" }), v =>
            {
                v["Equipments"] = new List<dynamic>();
                v["TotalCount"] = 40; v["OverdueCount"] = 2; v["DueSoonCount"] = 5;
            });

        AssertBuiltFromTheComponent(html, "Instrument", 1);
        Assert.Contains("vs-chip--danger", Kpis(html, "inKpis"));
        Assert.Contains("title=\"Export all instruments to Excel\"", html);
        Assert.Contains("vs-pill--warning", html);
        Assert.Contains("id=\"createInstrumentModal\"", html);
    }

    // ---------- Maintenance strategies ----------

    private static Task<string> Strategies(bool canCreate, bool canApprove)
        => Render<MaintenanceStrategyController>("/Views/MaintenanceStrategy/Index.cshtml", new StrategyIndexViewModel
        {
            Page = Page(new MaintenanceStrategyListDto { MaintenanceStrategyId = 1, VersionNumber = 2, Status = "Active", EquipmentClassName = "Centrifugal pump", EquipmentTagNumber = "", StrategyType = "TimeBased", ConsequenceCategory = "Operational", Frequency = "Monthly" }),
            Filter = new StrategyListFilter { EquipmentClassId = 4 },
            Classes = new() { new LookupItem { Id = 4, Text = "Centrifugal pump" } },
            CanCreate = canCreate, CanApprove = canApprove, CanEdit = true,
        }, _ => { });

    [Fact]
    public async Task MaintenanceStrategies_KeepTheirPermissions_AndMoveTheExtraLinksIntoTheMoreMenu()
    {
        var full = await Strategies(true, true);
        AssertBuiltFromTheComponent(full, "MaintenanceStrategy", 1, keepsDataBadges: true);
        var menu = Regex.Match(full, "<ul[^>]*lp-menu[^>]*>(.*?)</ul>", RegexOptions.Singleline).Value;
        Assert.Contains("bi-pie-chart", menu);                                  // coverage
        Assert.Contains("bi-journal-text", menu);                               // failure-mode catalogue
        Assert.Contains("bi-collection", menu);                                 // bulk assign (approvers only)
        Assert.Contains("lp-compose", full);
        Assert.Contains("Centrifugal pump", Chips(full));

        var readOnly = await Strategies(false, false);
        Assert.DoesNotContain("bi-collection", readOnly);
        Assert.DoesNotContain("lp-compose", readOnly);
    }

    // ---------- Spare parts ----------

    // ---------- Assets ----------

    [Fact]
    public async Task Assets_StatusesAreLocalisedPills_AndTheCreateModalStays()
    {
        var html = await Render<AssetController>("/Views/Asset/Index.cshtml",
            Page(new AssetListDto { AssetId = 1, AssetNumber = "AST-P101", EquipmentTagNumber = "P-101A", EquipmentName = "Feed pump", Status = "Standby" }), v =>
            {
                v["Equipments"] = new List<dynamic>();
                v["FunctionalLocations"] = new List<FunctionalLocationPickerItemDto>();
                v["TotalCount"] = 1; v["ActiveCount"] = 0;
                v["SelectedStatus"] = "Standby";
            });

        AssertBuiltFromTheComponent(html, "Asset", 1);
        Assert.Contains("vs-pill vs-pill--warning", html);
        Assert.Contains("Standby", Chips(html));
        Assert.Matches("<button[^>]*data-bs-target=\"#createAssetModal\"", html);
    }

    // ================= wave 2b: the remaining CMMS lists =================

    // ---------- Criticality assessments ----------

    [Fact]
    public async Task CriticalityAssessments_KeepStatusAndClassFilters_AndNewOnlyForEditors()
    {
        static Task<string> Queue(bool canEdit, string? status, int? classId) => Render<CriticalityAssessmentController>(
            "/Views/CriticalityAssessment/Index.cshtml",
            Page(new CriticalityAssessmentListDto { CriticalityAssessmentId = 4, EquipmentTagNumber = "P-101A", EquipmentClassName = "Pump", FrameworkVersion = "v1", Status = "UNDER_REVIEW", CreatedDate = new DateTime(2026, 10, 1) }),
            v =>
            {
                v["StatusFilter"] = status; v["EquipmentClassId"] = classId; v["CanEdit"] = canEdit;
                v["EquipmentClasses"] = new List<object> { Obj(("EquipmentClassId", 2), ("Name", "Pump"), ("Code", "PU")) };
            });

        var editor = await Queue(true, "UNDER_REVIEW", 2);
        AssertBuiltFromTheComponent(editor, "CriticalityAssessmentQueue", 1);
        Assert.Matches("<span class=\"lp-badge\">2<", editor);                       // status + class
        Assert.Equal(2, Regex.Matches(Chips(editor), "class=\"pems-filter-chip\"").Count);
        Assert.Contains("vs-pill--warning", editor);                                  // under review
        Assert.Contains("bi-speedometer2", editor);                                   // the dashboard tool
        Assert.Contains("lp-compose", editor);

        var reader = await Queue(false, null, null);
        Assert.DoesNotContain("lp-compose", reader);
        Assert.Equal("", Chips(reader));
    }

    // ---------- Equipment criticality ----------

    [Fact]
    public async Task EquipmentCriticality_CountsAreKpiChips_TheClassFilterWorks_AndNewOpensTheAssessModal()
    {
        var html = await Render<EquipmentCriticalityController>("/Views/EquipmentCriticality/Index.cshtml", Page(
            new EquipmentCriticalityListDto { EquipmentId = 3, EquipmentTagNumber = "P-101A", CriticalityClass = "A", RiskLevel = "High", SafetyImpact = "Fire" },
            new EquipmentCriticalityListDto { EquipmentId = 4, EquipmentTagNumber = "P-102", CriticalityClass = "C", RiskLevel = "Low" }),
            v => { v["ClassACount"] = 1; v["ClassBCount"] = 0; v["ClassCCount"] = 1; v["UnassessedCount"] = 7; v["SelectedClass"] = "A"; });

        AssertBuiltFromTheComponent(html, "EquipmentCriticality", 2);
        Assert.Single(Regex.Matches(Chips(html), "class=\"pems-filter-chip\""));
        Assert.Contains("vs-pill--danger", html);                                     // class A
        Assert.Matches("<button[^>]*data-bs-target=\"#assessModal\"", html);
        Assert.Contains("id=\"assessModal\"", html);                                  // the modal is kept
        Assert.Equal(4, Regex.Matches(Kpis(html, "ecKpis"), "class=\"vs-chip").Count);
    }

    // ---------- Mechanical equipment ----------

    [Fact]
    public async Task MechanicalEquipment_KeepsSearchSubTypeFilterAndRowActions()
    {
        var html = await Render<MechanicalEquipmentController>("/Views/MechanicalEquipment/Index.cshtml", Page(
            new MechanicalEquipmentListDto { MechanicalEquipmentId = 5, TagNumber = "C-201", Manufacturer = "Atlas", Model = "GA90", EquipmentType = "Compressor", SubType = "Rotating" }),
            v =>
            {
                v["Equipments"] = new List<object>(); v["SearchTerm"] = "atlas"; v["SelectedSubType"] = "Rotating";
                v["TotalCount"] = 1; v["RotatingCount"] = 1; v["StaticCount"] = 0; v["UnclassifiedCount"] = 0;
            });

        AssertBuiltFromTheComponent(html, "MechanicalEquipment", 1);
        Assert.Contains("id=\"meSearchForm\"", html);
        Assert.Equal(2, Regex.Matches(Chips(html), "class=\"pems-filter-chip\"").Count);   // search + sub-type
        Assert.Contains("bi-card-text", html);                                         // passport
        Assert.Contains("bi-pencil", html);                                            // edit
        Assert.Matches("<button[^>]*data-bs-target=\"#createMechanicalModal\"", html);
        Assert.Equal(4, Regex.Matches(Kpis(html, "meKpis"), "class=\"vs-chip").Count);
    }

    // ---------- Failure mode catalogue ----------

    [Fact]
    public async Task FailureModeCatalog_EditAndDeactivateOnlyForEditors()
    {
        static Task<string> Catalog(bool canEdit) => Render<FailureModeCatalogController>("/Views/FailureModeCatalog/Index.cshtml",
            new List<FailureModeOptionDto>
            {
                new() { FailureModeId = 1, Code = "VIB", Name = "Vibration", EquipmentClassName = "Pump", Causes = { new FailureCauseOptionDto { Code = "MIS", Name = "Misalignment" } } },
            },
            v => { v["Classes"] = new List<LookupItem> { new() { Id = 2, Text = "Pump", Code = "PU" } }; v["ClassId"] = 2; v["Query"] = null; v["CanEdit"] = canEdit; });

        var editor = await Catalog(true);
        AssertBuiltFromTheComponent(editor, null, 1);
        Assert.Contains("Misalignment", editor);
        Assert.Single(Regex.Matches(Chips(editor), "class=\"pems-filter-chip\""));
        Assert.Contains("name=\"active\" value=\"false\"", editor);
        Assert.Contains("lp-compose", editor);

        var reader = await Catalog(false);
        Assert.DoesNotContain("name=\"active\"", reader);
        Assert.DoesNotContain("lp-compose", reader);
    }

    // ---------- Shift handovers ----------

    // ---------- Production orders and the procurement cartable (SCRUM-337) ----------

    [Fact]
    public async Task QcLists_AreListPages_WithToneBadges()
    {
        var ncr = await Render<QCController>("/Views/QC/NcrIndex.cshtml", new List<NonConformanceReport>
        {
            new() { NonConformanceReportId = 1, NCRNumber = "NCR-001", Severity = "Critical", Status = "Closed", RaisedDate = new DateTime(2026, 10, 1) },
        }, _ => { });
        AssertBuiltFromTheComponent(ncr, null, 1, keepsDataBadges: true);
        Assert.Contains("NCR-001", ncr);
        Assert.Contains("badge bg-success\">Closed<", ncr);

        var itp = await Render<QCController>("/Views/QC/ItpIndex.cshtml", new List<InspectionTestPlan>
        {
            new() { InspectionTestPlanId = 2, ITPNumber = "ITP-002", Title = "Weld check", Status = "Active", Revision = 1 },
        }, _ => { });
        AssertBuiltFromTheComponent(itp, null, 1, keepsDataBadges: true);
        Assert.Contains("ITP-002", itp);
        Assert.Contains("badge bg-success\">Active<", itp);
    }

    [Fact]
    public async Task ReferredLetters_AreAListPage()
    {
        var html = await Render<LetterController>("/Views/Letter/MyReferrals.cshtml", new List<LetterDto>
        {
            new() { LetterId = 5, LetterType = "Incoming", LetterNumber = "L-005", Subject = "Pump quote", LetterDate = new DateTime(2026, 10, 9), Status = "Draft" },
        }, _ => { });
        AssertBuiltFromTheComponent(html, null, 1, keepsDataBadges: true);
        Assert.Contains("L-005", html);
    }

    [Fact]
    public async Task LetterTemplates_AreAListPage_AndKeepTheirToggleForm()
    {
        var html = await Render<LetterTemplateController>("/Views/LetterTemplate/Index.cshtml", new List<LetterTemplateDto>
        {
            new() { LetterTemplateId = 1, Name = "Ack", Subject = "S1", Priority = "Normal", ConfidentialityLevel = "Internal", IsActive = true },
        }, _ => { });
        AssertBuiltFromTheComponent(html, null, 1, keepsDataBadges: true);
        Assert.Contains("name=\"active\" value=\"false\"", html);
    }

    [Fact]
    public async Task OcrProcessing_IsAListPage_AndKeepsItsCountTiles()
    {
        var html = await Render<OCRProcessingController>("/Views/OCRProcessing/Index.cshtml", Page(
            new OCRProcessingAttachmentListDto { AttachmentId = 3, FileName = "scan.pdf", FileExtension = ".pdf", FileSize = 2048, AIStatus = "Failed", AIError = "unreadable", UploadDate = new DateTime(2026, 11, 1, 9, 0, 0) }),
            v => { v["Counts"] = new Dictionary<string, int> { ["Failed"] = 1 }; v["SelectedStatus"] = "Failed"; });

        AssertBuiltFromTheComponent(html, "OCRProcessing", 1, keepsDataBadges: true);
        Assert.Contains("unreadable", html);
        Assert.Equal(4, Regex.Matches(html, "class=\"kpi-card").Count);
        Assert.Single(Regex.Matches(Chips(html), "class=\"pems-filter-chip\""));
    }

    [Fact]
    public async Task ClassificationReviewQueue_IsAListPage_WithItsStatusFilterAndActions()
    {
        var html = await Render<EquipmentClassificationController>("/Views/EquipmentClassification/ReviewQueue.cshtml", new ReviewQueuePageDto
        {
            Items = { new ReviewQueueItemDto { SuggestionId = 11, TagNumber = "P-101", EquipmentName = "Pump", SuggestedEquipmentClassName = "Centrifugal pump", Confidence = "MEDIUM", Source = "AI", Reason = "name match" } },
            TotalCount = 1, Page = 1, PageSize = 50
        }, v => { v["StatusFilter"] = "PENDING"; });

        AssertBuiltFromTheComponent(html, null, 1, keepsDataBadges: true);
        Assert.Contains("P-101", html);
        Assert.Contains("name=\"status\"", html);
        Assert.Contains("name=\"id\" value=\"11\"", html);   // accept and reject forms kept
        Assert.DoesNotContain("pagination-sm", html);
    }

    [Fact]
    public async Task Tasks_AreAListPage_WithTheirTabFilterAndTheNewTaskForm()
    {
        var html = await Render<TaskController>("/Views/Task/Index.cshtml", Page(
            new TaskItemDto { TaskItemId = 9, Title = "Call vendor", Priority = "Normal", Status = "Open", IsOverdue = true, DueDate = new DateTime(2026, 10, 20) }),
            v => { v["Tab"] = "action"; v["CanEdit"] = true; v["CanCreate"] = true; v["Employees"] = new List<EligibleAssigneeDto> { new() { EmployeeId = 4, Name = "A. Karimi" } }; });

        AssertBuiltFromTheComponent(html, "Task", 1, keepsDataBadges: true);
        Assert.Contains("Call vendor", html);
        Assert.Contains("name=\"tab\"", html);
        Assert.Contains("name=\"assignedToEmployeeId\"", html);   // the new-task form moved into the toolbar popover
        Assert.Contains("name=\"taskItemId\" value=\"9\"", html);   // status change form kept
        Assert.DoesNotContain("pagination-sm", html);
    }

    [Fact]
    public async Task Meetings_AreAListPage_AndScheduleOpensTheirOwnModal()
    {
        var html = await Render<MeetingController>("/Views/Meeting/Index.cshtml", Page(
            new MeetingDto { MeetingId = 5, Title = "Weekly review", MeetingDate = new DateTime(2026, 11, 2, 9, 0, 0), Status = "Scheduled", AttendeeCount = 3, ActionItemCount = 1 }),
            v => v["Projects"] = new List<dynamic>());

        AssertBuiltFromTheComponent(html, "Meeting", 1, keepsDataBadges: true);   // counts and status are library badges
        Assert.Contains("Weekly review", html);
        Assert.Contains("data-bs-target=\"#createMeetingModal\"", html);
        Assert.Contains("id=\"createMeetingModal\"", html);
    }

    // ---------- RCM / FMEA coverage ----------

    [Fact]
    public async Task RcmFmeaCoverage_ShowsItsCountsFirst_AndListsOnlyClassesWithEquipment()
    {
        var html = await Render<RcmFmeaController>("/Views/RcmFmea/Index.cshtml", new RcmFmeaCoverageMatrixDto
        {
            ClassesWithEquipment = 1, ClassesFmeaReady = 1,
            Classes =
            {
                new() { EquipmentClassId = 2, Code = "PU", Name = "Pump", EquipmentCount = 12, FMEAReadiness = "READY", RCMReadiness = "PARTIALLY_READY", ApprovedFMEACount = 1 },
                new() { EquipmentClassId = 3, Code = "VS", Name = "Vessel", EquipmentCount = 0 },
            },
        }, _ => { });

        AssertBuiltFromTheComponent(html, null, 1);
        Assert.Matches("<div class=\"lp-kpis\" id=\"rfKpis\">", html);               // open: no hidden attribute
        Assert.Contains("data-lp-open=\"1\"", html);
        Assert.Contains("vs-pill--success", html);
        Assert.Contains("vs-pill--warning", html);
        Assert.DoesNotContain("Vessel", html);
    }

    // ---------- Equipment <-> functional location assignment ----------

    [Fact]
    public async Task AssignmentBatches_UploadOpensFromTheToolbar_WithoutAModal_AndCountsArePills()
    {
        var html = await Render<EquipmentFunctionalLocationAssignmentController>("/Views/EquipmentFunctionalLocationAssignment/Index.cshtml",
            new List<EquipmentFunctionalLocationAssignmentBatchSummaryDto>
            {
                new() { EquipmentFunctionalLocationAssignmentBatchId = 9, FileName = "fl.xlsx", ImportDate = new DateTime(2026, 10, 29), Status = "Staging", TotalRows = 10, ExactCount = 7, UnresolvedCount = 2, ConflictCount = 1 },
            }, _ => { });

        AssertBuiltFromTheComponent(html, null, 1);
        Assert.Contains("id=\"flaUploadBtn\"", html);
        Assert.DoesNotContain("class=\"modal", html);                                   // SCRUM-341 ratchet: no new hand-written modal
        var upload = html[html.IndexOf("id=\"flaUpload\"", StringComparison.Ordinal)..];
        Assert.Contains("type=\"file\"", upload);
        Assert.Contains("enctype=\"multipart/form-data\"", upload);
        Assert.Equal(2, Regex.Matches(html, "vs-pill vs-pill--danger").Count);         // unresolved + conflicts
    }

    [Fact]
    public void EveryWave2Page_UsesTheComponentAndItsAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var page in Pages)
        {
            var view = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", page, "Index.cshtml"));
            Assert.True(view.Contains("<pems-list-page texts="), page);
            Assert.True(view.Contains("~/_content/PEMS.UI/pems-list.css"), page);
            Assert.True(view.Contains("~/_content/PEMS.UI/pems-list.js"), page);
            Assert.False(view.Contains("initColumnResize"), page);
            Assert.False(view.Contains("_PageHeader"), page);           // the toolbar heading is the page title
        }
    }
}
