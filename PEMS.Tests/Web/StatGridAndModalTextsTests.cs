using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-551: the stat tiles (<c>&lt;pems-stat-grid&gt;</c>) are a grid on every page, not only inside admin pages.
/// SCRUM-552: the modal titles, badges and labels found in the Sprint 1 review come from resources, not hard-coded English.
/// </summary>
public class StatGridAndModalTextsTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    [Fact]
    public void TheStatGrid_IsAGridOnEveryPage_4_2_1Columns()
    {
        var css = Read("PEMS.UI", "wwwroot", "pems-ui.css");
        Assert.Matches(@"(^|\n)\.admin-kpi-grid \{ display: grid; grid-template-columns: repeat\(4, minmax\(0, 1fr\)\)", css);   // not scoped to .admin-page
        Assert.Contains("@media (max-width: 992px) { .admin-kpi-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }", css);
        Assert.Contains("@media (max-width: 576px) { .admin-kpi-grid { grid-template-columns: 1fr; } }", css);
    }

    [Theory]
    [InlineData("Areas/Warehouse/Views/MrsImport/Review.cshtml", "label=\"Matched Existing\"", "label=\"Duplicate In Batch\"", "فایل کامل را آپلود کنید")]
    [InlineData("Views/TableExtraction/Review.cshtml", "label=\"Matched - no Instrument yet\"", "label=\"Matched - has Instrument\"")]
    [InlineData("Views/Letter/Details.cshtml", ">Reject Letter<", ">Sign / Acknowledge<", "/> Sign / Acknowledge</a>")]
    [InlineData("Views/Asset/Details.cshtml", ">Edit Asset<")]
    [InlineData("Views/EngineeringDocuments/Details.cshtml", "/> Freeze Document<", ">Create New Revision<", "/> Create New Revision<", ">Record Client Response<", "/> Engineering Document Approval Workflow<", ">Client Decision<", "This is the client's (owner's) response")]
    [InlineData("Views/MaintenanceMasterData/Index.cshtml", ">Maintenance Strategy Type جدید<", ">Standard Reference جدید<", ">Maintenance Activity جدید<", "/> Standard Reference جدید")]
    [InlineData("Views/WorkOrder/Details.cshtml", "\">@Model.Status</pems-badge>", "\">@Model.Priority</pems-badge>", ">@Model.WorkOrderType</pems-badge>", "این WorkOrder رد شود")]
    [InlineData("Views/CriticalityAssessment/Details.cshtml", "\"1 — Negligible\"", "_ => \"Not scored\"")]
    public void TheReviewedTexts_AreNoLongerHardCoded(string view, params string[] literals)
    {
        var text = Read(["PEMS.Web", .. view.Split('/')]);
        foreach (var literal in literals) Assert.DoesNotContain(literal, text);
    }

    [Fact]
    public void TheMixedPersianEnglishTitles_HaveRealPersianValues()
    {
        var fa = Read("PEMS.Web", "Resources", "SharedResource.fa.resx");
        foreach (var key in new[] { "Maintenance Type جدید", "Equipment Class جدید", "Strategy Type جدید", "Evaluation Profile جدید", "Maintenance Strategy Type جدید", "Standard Reference جدید", "Maintenance Activity جدید" })
        {
            var value = Regex.Match(fa, "<data name=\"" + Regex.Escape(key) + "\"[^>]*>\\s*<value>([^<]*)</value>").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(value), key);
            Assert.DoesNotMatch("[A-Za-z]", value);   // the Persian page shows Persian only
        }
    }
}
