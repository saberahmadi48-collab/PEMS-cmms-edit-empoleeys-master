using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 wave 1 (document control): DocumentRegister, DocumentRevisions, Transmittal, TechnicalQuery, MDB, MOC and
/// DocumentWorkflow are built from the PEMS.UI list-page component. Each page keeps its own filters, columns and actions;
/// the old hand-written frame (content cards, KPI cards, inline column-resize script) is gone. DCC is a desk, not a list.
/// </summary>
public class DocumentControlListPagesTests
{
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

    private static List<dynamic> Projects() => new() { Obj(("ProjectId", 1), ("ProjectName", "JPC")), Obj(("ProjectId", 2), ("ProjectName", "BIDBOLAND")) };

    private static void AssertBuiltFromTheComponent(string html, string gridKey, int rows)
    {
        Assert.Contains("class=\"lp-page\"", html);
        Assert.Contains("class=\"lp-toolbar\"", html);
        Assert.Matches($"<table class=\"[^\"]*lp-table[^\"]*\" data-grid-key=\"{gridKey}\"", html);
        Assert.Equal(rows, Regex.Matches(html, "<tr class=\"lp-row\"").Count);
        Assert.Contains($"data-lp-reset-columns=\"{gridKey}\"", html);
        Assert.Contains("class=\"lp-footer\"", html);
        // the old hand-written frame is gone
        Assert.DoesNotContain("content-card", html);
        Assert.DoesNotContain("PemsDataGrid.initColumnResize", html);
        Assert.DoesNotContain("class=\"badge ", html);
    }

    // ---------- DocumentRegister ----------

    private static Task<string> Register(string? project = null, List<string>? disciplines = null, string? status = null, string? q = null,
        List<SavedFilterDto>? saved = null, params EngineeringDocumentDto[] docs)
        => Render<DocumentRegisterController>("/Views/DocumentRegister/Index.cshtml", Page(docs), v =>
        {
            v["Projects"] = new List<string> { "JPC", "BIDBOLAND" };
            v["Disciplines"] = new List<string> { "Mechanical", "Electrical" };
            v["Statuses"] = new List<string> { "Approved", "Submitted" };
            v["SelectedProject"] = project;
            v["SelectedDisciplines"] = disciplines ?? new List<string>();
            v["SelectedStatus"] = status;
            v["SearchTerm"] = q;
            v["SavedFilters"] = saved ?? new List<SavedFilterDto>();
        });

    private static EngineeringDocumentDto Doc(int id) => new()
    {
        DocumentId = id, DocumentNo = "JPC-ME-DS-00" + id, Title = "Pump datasheet " + id, DocumentType = "DS", ProjectName = "JPC",
        DisciplineName = "Mechanical", RevisionNo = "B", Status = "Approved", CreatedDate = new DateTime(2026, 10, 1), IsFrozen = id == 2
    };

    [Fact]
    public async Task Register_IsBuiltFromTheComponent_WithSearchFiltersSavedViewsExcelAndRowSelection()
    {
        var html = await Register(docs: new[] { Doc(1), Doc(2) });

        AssertBuiltFromTheComponent(html, "DocumentRegister", 2);
        Assert.Contains("aria-current=\"page\"", html);                        // the document sub-navigation, Register active
        Assert.Contains("data-lp-search-open", html);
        Assert.Contains("id=\"drFilterFormBtn\"", html);
        Assert.Contains("id=\"drViewsBtn\"", html);                            // quick and saved filters in one menu
        Assert.Contains("bi-file-earmark-excel", html);
        Assert.Contains("data-bs-target=\"#saveFilterModal\"", html);
        // row selection drives the shared bulk bar; the page posts the ticked ids to its own action
        Assert.Contains("data-lp-select-all", html);
        Assert.Equal(2, Regex.Matches(html, "data-lp-select(?!-)").Count);
        var bulk = Regex.Match(html, "<div class=\"lp-bulk\"[^>]*>").Value;
        Assert.Contains("data-lp-bulk=\"ids\"", bulk);
        Assert.Contains("hidden=\"hidden\"", bulk);
        Assert.Contains("data-count-text=", bulk);
        Assert.DoesNotContain("selectAllDocs", html);                          // the page's own selection script is gone
        Assert.Contains("bi-snow", html);                                      // frozen shown next to the status
    }

    [Fact]
    public async Task Register_ActiveFilters_ShowBadgeAndChips_AndTheDisciplineChecksKeepTheirValues()
    {
        var html = await Register(project: "JPC", disciplines: new() { "Electrical" }, status: "Approved", q: "pump");

        Assert.Matches("<span class=\"lp-badge\">3<", html);
        var chips = Regex.Match(html, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline).Value;
        Assert.Equal(3, Regex.Matches(chips, "class=\"pems-filter-chip\"").Count);
        Assert.Contains("value=\"Electrical\"", html);
        Assert.Matches("value=\"Electrical\"[^>]*checked=\"checked\"", html);
        Assert.Contains("id=\"drDisciplines\" value=\"Electrical\"", html);
        Assert.Contains("value=\"pump\"", html);                               // search term stays in the search field
        Assert.Contains("class=\"lp-empty\"", html);                           // filtered and empty: clear-all offered
        Assert.Contains("btn btn-sm btn-outline-secondary mt-2", html);
    }

    [Fact]
    public async Task Register_SavedFilters_AreListedWithADeleteForm()
    {
        var html = await Register(saved: new() { new SavedFilterDto { SavedFilterId = 7, Name = "My pumps", QueryString = "status=Approved", IsDefault = true } });

        var menu = Regex.Match(html, "<ul[^>]*lp-menu[^>]*>(.*?)</ul>", RegexOptions.Singleline).Value;
        Assert.Contains("My pumps", menu);
        Assert.Contains("?status=Approved", menu);
        Assert.Contains("name=\"id\" value=\"7\"", menu);
        Assert.Contains("bi-star-fill", menu);
    }

    // ---------- DocumentRevisions ----------

    private static Task<string> Revisions(int? documentId, params DocumentRevisionDto[] items)
        => Render<DocumentRevisionsController>("/Views/DocumentRevisions/Index.cshtml", Page(items), v =>
        {
            v["Documents"] = new List<dynamic> { Obj(("DocumentId", 5), ("DocumentNo", "JPC-001"), ("Title", "P&ID")) };
            v["DocumentId"] = documentId;
        });

    [Fact]
    public async Task Revisions_OffersNewRevisionOnlyForAChosenDocument()
    {
        var all = await Revisions(null, new DocumentRevisionDto { DocumentNo = "JPC-001", RevisionNo = "A", Status = "Approved", RevisionDate = new DateTime(2026, 10, 2) });
        AssertBuiltFromTheComponent(all, "DocumentRevisions", 1);
        Assert.DoesNotContain("lp-compose", all);
        Assert.DoesNotContain("class=\"lp-chips\"", all);

        var one = await Revisions(5);
        Assert.Contains("lp-compose", one);
        Assert.Contains("JPC-001", Regex.Match(one, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline).Value);
        Assert.Contains("class=\"lp-empty\"", one);
    }

    // ---------- Transmittal ----------

    [Fact]
    public async Task Transmittal_KeepsTheOverdueFlag_AndTheDocumentSubNavigation()
    {
        var html = await Render<TransmittalController>("/Views/Transmittal/Index.cshtml",
            Page(new TransmittalDto { TransmittalId = 3, TransmittalNo = "TR-0003", ProjectName = "JPC", Status = "Sent", IsResponseOverdue = true, TransmittalDate = new DateTime(2026, 10, 3) }),
            _ => { });

        AssertBuiltFromTheComponent(html, "Transmittal", 1);
        Assert.Contains("vs-subnav", html);
        Assert.Contains("vs-pill--danger", html);
        Assert.Contains("lp-compose", html);
        Assert.DoesNotContain("vs-panel", html);
    }

    // ---------- TechnicalQuery ----------

    // ---------- MDB ----------

    [Fact]
    public async Task Mdb_FiltersByProjectAndVendor_AndShowsDeliveryAsPills()
    {
        var html = await Render<MDBController>("/Views/MDB/Index.cshtml",
            Page(new ManufacturerDataBookDto { MDBId = 2, EquipmentTagNumber = "P-101A", VendorName = "KSB", Status = "Draft", IsIndexOverdue = true, HardCopyShippedDate = new DateTime(2026, 9, 1) }), v =>
            {
                v["Projects"] = Projects();
                v["Vendors"] = new List<dynamic> { Obj(("VendorId", 4), ("Name", "KSB")) };
                v["SelectedVendorId"] = 4;
            });

        AssertBuiltFromTheComponent(html, "MDB", 1);
        Assert.Contains("name=\"vendorId\"", html);
        Assert.Contains("KSB", Regex.Match(html, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline).Value);
        Assert.Contains("vs-pill--success", html);                             // hard copy shipped
        Assert.Contains("vs-pill--danger", html);                              // index overdue
    }

    // ---------- DocumentWorkflow ----------

    [Fact]
    public async Task DocumentWorkflow_HasTheQueueWithItsActions_AndTheHistoryAsASecondPanel()
    {
        var html = await Render<DocumentWorkflowController>("/Views/DocumentWorkflow/Index.cshtml",
            Page(new DocumentWorkflowPendingItemDto { DocumentId = 1, DocumentNo = "JPC-001", Title = "P&ID", Status = "Submitted", RevisionNo = "A" },
                 new DocumentWorkflowPendingItemDto { DocumentId = 2, DocumentNo = "JPC-002", Title = "PFD", Status = "Checked", RevisionNo = "B" }), v =>
            {
                v["History"] = new List<DocumentWorkflow>
                {
                    new() { Document = new EngineeringDocument { DocumentNo = "JPC-001" }, FromStatus = "Draft", ToStatus = "Submitted", ActionType = "Submit", ActionDate = new DateTime(2026, 10, 4, 9, 30, 0) }
                };
            });

        AssertBuiltFromTheComponent(html, "DocumentWorkflow", 3);              // 2 queue rows + 1 history row
        Assert.Equal(2, Regex.Matches(html, "class=\"lp-panel\"").Count);
        Assert.Contains("name=\"documentId\" value=\"1\"", html);
        Assert.Equal(2, Regex.Matches(html, "bi-x-lg").Count);                 // reject for Submitted and for Checked
        Assert.Contains("bi-clipboard-check", html);
        Assert.Contains("bi-check-lg", html);
        Assert.Contains("2026-10-04 09:30", html);
    }

    [Fact]
    public void Dcc_IsADeskNotAList_AndStaysOutOfTheListComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var page in new[] { "DocumentRegister", "DocumentRevisions", "Transmittal", "MDB", "DocumentWorkflow" })
        {
            var view = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", page, "Index.cshtml"));
            Assert.Contains("<pems-list-page texts=", view);
            Assert.Contains("~/_content/PEMS.UI/pems-list.css", view);
            Assert.Contains("~/_content/PEMS.UI/pems-list.js", view);
        }
        Assert.DoesNotContain("<pems-list-page", File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", "DCC", "Index.cshtml")));
    }
}
