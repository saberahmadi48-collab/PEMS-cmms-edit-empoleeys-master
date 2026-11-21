using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 RelatedList: the child collection of a detail page (section with title, count and empty text, child table,
/// add form, delete form per row, in-place row editing) and the first pages built from it (JobPlan details and the three
/// Administration editors).
/// </summary>
public class RelatedListComponentTests
{
    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    private static async Task<string> Run(TagHelper helper, string tag, string childHtml = "")
    {
        var items = new Dictionary<object, object>();
        var context = new TagHelperContext(tag, new TagHelperAttributeList(), items, "u1");
        var output = new TagHelperOutput(tag, new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(childHtml)));
        helper.Init(context);
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task Section_HasATitledHeadingWithCount_AndTheEmptyTextOnlyWhenEmpty()
    {
        var html = await Run(new PemsRelatedListTagHelper(Renderer) { Id = "tasks", Icon = "bi-list-ol", Title = "Tasks", Count = 3, EmptyText = "None yet" }, "pems-related-list", "<p>rows</p>");
        Assert.StartsWith("<section", html);
        Assert.Contains("aria-labelledby=\"tasksTitle\"", html);
        Assert.Contains("<h3 id=\"tasksTitle\">", html);
        Assert.Contains("<span class=\"lp-total\">3</span>", html);
        Assert.Contains("<p>rows</p>", html);
        Assert.DoesNotContain("None yet", html);

        var empty = await Run(new PemsRelatedListTagHelper(Renderer) { Title = "Tasks", Empty = true, EmptyText = "None yet" }, "pems-related-list");
        Assert.Contains("<p class=\"lp-rel-empty\">None yet</p>", empty);
    }

    [Fact]
    public async Task AddForm_PostsTheParentKey_AndTheFields()
    {
        var html = await Run(new PemsRelatedAddTagHelper(Renderer) { Action = "/JobPlan/AddTask", ParentName = "jobPlanId", ParentValue = "7", SubmitLabel = "Add" },
            "pems-related-add", "<div><input name=\"title\" /></div>");
        Assert.Matches("<form action=\"/JobPlan/AddTask\" class=\"lp-rel-add\" method=\"post\">", html);
        Assert.Contains("<input name=\"jobPlanId\" type=\"hidden\" value=\"7\" />", html);
        Assert.Contains("<input name=\"title\" />", html);
        Assert.Matches("<button class=\"[^\"]*lp-rel-submit\" type=\"submit\">", html);
    }

    [Fact]
    public async Task Remove_PostsTheRowAndParentKeys_WithALabelledButton_AndAsksOnlyWhenTold()
    {
        var html = await Run(new PemsRelatedRemoveTagHelper(Renderer) { Action = "/JobPlan/RemoveTask", KeyName = "maintenanceTaskId", KeyValue = "5", ParentName = "jobPlanId", ParentValue = "7", Label = "Remove" },
            "pems-related-remove");
        Assert.Contains("name=\"maintenanceTaskId\" type=\"hidden\" value=\"5\"", html);
        Assert.Contains("name=\"jobPlanId\" type=\"hidden\" value=\"7\"", html);
        Assert.Contains("title=\"Remove\"", html);
        Assert.Contains("<span class=\"visually-hidden\">Remove</span>", html);
        Assert.DoesNotContain("confirm(", html);

        var asked = await Run(new PemsRelatedRemoveTagHelper(Renderer) { Action = "/x", KeyName = "id", KeyValue = "1", Label = "Remove", Confirm = "Sure?" }, "pems-related-remove");
        Assert.Contains("data-confirm=\"Sure?\"", asked);
        Assert.Contains("onsubmit=\"return confirm(this.dataset.confirm)\"", asked);
    }

    [Fact]
    public async Task RowForm_IsHiddenAndNamed_AndSaveSubmitsIt()
    {
        var form = await Run(new PemsRelatedRowFormTagHelper(Renderer) { Id = "num_PO", Action = "/Admin/Numbering/Save" }, "pems-related-row-form", "<input type=\"hidden\" name=\"DocumentType\" value=\"PO\" />");
        Assert.Matches("<form action=\"/Admin/Numbering/Save\" hidden=\"hidden\" id=\"num_PO\" method=\"post\">", form);
        Assert.Contains("name=\"DocumentType\"", form);

        var save = await Run(new PemsRelatedSaveTagHelper(Renderer) { FormId = "num_PO", Label = "Save" }, "pems-related-save");
        Assert.Contains("form=\"num_PO\"", save);
        Assert.Contains("type=\"submit\"", save);
        Assert.Contains("title=\"Save\"", save);
    }

    // ---------- pages ----------

    private static Task<string> Render<TController>(string view, object model, Action<ViewDataDictionary> data) where TController : Controller
    {
        var controller = (Controller)RuntimeHelpers.GetUninitializedObject(typeof(TController));
        data(controller.ViewData);
        var result = new ViewResult { ViewData = new ViewDataDictionary(controller.ViewData) { Model = model } };
        return RazorViewRenderer.RenderAsync(controller, result, view);
    }

    private static dynamic Obj(params (string Key, object? Value)[] values)
    {
        IDictionary<string, object?> o = new ExpandoObject();
        foreach (var (k, v) in values) o[k] = v;
        return o;
    }

    [Fact]
    public async Task JobPlan_TasksAndMaterials_AreRelatedLists_WithAddAndRemove()
    {
        var plan = new JobPlanDetailDto
        {
            JobPlanId = 4, Title = "PM pump",
            Tasks = new() { new MaintenanceTaskDto { MaintenanceTaskId = 11, TaskNumber = 10, Title = "Isolate" } },
            RequiredMaterials = new(),
        };
        var html = await Render<JobPlanController>("/Views/JobPlan/Details.cshtml", plan, v =>
        {
            v["AllSkills"] = new List<dynamic>();
            v["MaterialItems"] = new List<dynamic> { Obj(("MaterialItemId", 3), ("MaterialCode", "M-3"), ("Description", "Bearing")) };
            v["EquipmentClasses"] = new List<dynamic>();
        });

        Assert.Contains("id=\"jpTasks\"", html);
        Assert.Contains("id=\"jpMaterials\"", html);
        Assert.Contains("name=\"maintenanceTaskId\" type=\"hidden\" value=\"11\"", html);
        Assert.Equal(2, Regex.Matches(html, "class=\"lp-rel-add\"").Count);
        Assert.Contains("<p class=\"lp-rel-empty\">", html);                               // no materials yet
        Assert.Contains("value=\"3\">M-3 - Bearing</option>", html);
    }

    // ---------- wave R2 ----------

    [Fact]
    public async Task Tender_VendorsBidsAndClarifications_AreRelatedSections()
    {
        var tender = new PEMS.Domain.Entities.Tender { TenderId = 8, TenderNumber = "TND-8", Title = "Pumps", Status = "Draft" };
        var html = await Render<TenderController>("/Views/Tender/Details.cshtml", tender, v =>
        {
            v["Vendors"] = new List<dynamic> { Obj(("VendorId", 2), ("Name", "ACME")) };
            v["Clarifications"] = new List<PEMS.Domain.Entities.ProcurementCommunication>();
        });

        Assert.Contains("id=\"tenderVendors\"", html);
        Assert.Contains("id=\"tenderBids\"", html);
        Assert.Contains("id=\"tenderClarifications\"", html);
        Assert.Contains("name=\"tenderId\" type=\"hidden\" value=\"8\"", html);               // invite form posts the tender
        Assert.Contains("value=\"2\">ACME</option>", html);
        Assert.Equal(3, Regex.Matches(html, "class=\"lp-rel-empty\"").Count);                // no vendors, bids or clarifications yet
    }

    [Fact]
    public void Wave2Pages_UseTheRelatedSections()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var web = Path.Combine(dir!.FullName, "PEMS.Web");
        foreach (var (page, sections) in new[]
        {
            ("Views/Tender/Details.cshtml", new[] { "tenderVendors", "tenderInvitations", "tenderBids", "tenderClarifications" }),
        })
        {
            var view = File.ReadAllText(Path.Combine(web, page.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var id in sections) Assert.Contains($"<pems-related-list id=\"{id}\"", view);
            Assert.Contains("_content/PEMS.UI/pems-list.css", view);
            Assert.DoesNotContain("<table class=\"table", view);                                   // every table comes from the component
        }
    }

    [Fact]
    public void Wave3aPages_TakeTheirTablesFromTheComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var web = Path.Combine(dir!.FullName, "PEMS.Web");
        var pcr = File.ReadAllText(Path.Combine(web, "Areas", "Warehouse", "Views", "PartCatalogueRequest", "Edit.cshtml"));
        Assert.Contains("<pems-related-table id=\"pcAttrTable\">", pcr);                 // pems-part-catalogue.js finds it by id
        Assert.Contains("<pems-related-remove action=\"@Url.Action(\"RemoveEquipment\")\"", pcr);
        var rcm = File.ReadAllText(Path.Combine(web, "Views", "RcmFmea", "ClassDetail.cshtml"));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(rcm, "<pems-related-table>").Count);
        foreach (var view in new[] { pcr, rcm })
        {
            Assert.DoesNotContain("<table", view);
            Assert.Contains("_content/PEMS.UI/pems-list.css", view);
        }
    }

    [Fact]
    public void Wave3bPermit_TakesItsSevenTablesFromTheComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var permit = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", "Permit", "Details.cshtml"));
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(permit, "<pems-related-table>").Count);
        Assert.DoesNotContain("<table", permit);
        Assert.Contains("_content/PEMS.UI/pems-list.css", permit);
    }

    [Fact]
    public void Wave3cVdr_TakesItsThreeTablesFromTheComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var vdr = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", "VDR", "Details.cshtml"));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(vdr, "<pems-related-table>").Count);   // SCRUM-58: the fourth (workflow history) is now <pems-workflow-history>
        Assert.Contains("<pems-workflow-history", vdr);
        Assert.DoesNotContain("<table", vdr);
        Assert.Contains("_content/PEMS.UI/pems-list.css", vdr);
    }

    [Fact]
    public void Wave3dWorkOrder_TakesItsFiveTablesFromTheComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var wo = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "Views", "WorkOrder", "Details.cshtml"));
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(wo, "<pems-related-table>").Count);
        Assert.DoesNotContain("<table", wo);
        Assert.Contains("_content/PEMS.UI/pems-list.css", wo);
    }
}
