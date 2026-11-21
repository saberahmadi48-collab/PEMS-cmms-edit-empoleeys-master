using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-428: an MR item's part number is resolved on the server against the registered parts (completed part catalogue requests,
/// then Material Master). A registered part is linked and fills an empty description and unit; a typed part number that is not
/// registered stays free text ("not catalogued"); the lookup carries the unit; the create form wires the lookup.
/// </summary>
public class MrRegisteredPartTests
{
    private static async Task<(PEMSDbContext Db, MaterialRequisitionService Mr)> EnvAsync(string name)
    {
        var db = PemsDbContextFactory.CreateInMemory("mr428_" + name);
        db.Projects.Add(new Project { ProjectId = 1, ProjectCode = "TP", ProjectName = "Test Project", IsActive = true });
        db.Disciplines.Add(new Discipline { DisciplineId = 1, Code = "MECH", Name = "Mechanical", IsActive = true });
        var each = new MaterialUOM { Code = "EA", Name = "Each" };
        var set = new MaterialUOM { Code = "SET", Name = "Set" };
        db.AddRange(each, set);
        await db.SaveChangesAsync();

        var request = new PartCatalogueRequest
        {
            RequesterEmployeeId = 1, Status = "Completed", AssignedPartNumber = "PCR-ABC-001", UnitOfMeasureId = set.MaterialUOMId,
            ManufacturerPartNumber = "6ES7-214", NewManufacturerName = "Siemens", IsNewManufacturer = true, CompletedDate = DateTime.Now
        };
        request.Attributes.Add(new PartCatalogueRequestAttribute { Name = PartCatalogueRequestAttribute.FreeFormatText, Value = "PLC CPU 1214C" });
        db.PartCatalogueRequests.Add(request);
        // a draft with the same number never counts as registered
        db.PartCatalogueRequests.Add(new PartCatalogueRequest { RequesterEmployeeId = 1, Status = "Draft", AssignedPartNumber = "DRAFT-1" });
        db.MaterialItems.Add(new MaterialItem { MaterialCode = "MAT-9", Description = "Gasket 2in", UomId = each.MaterialUOMId, IsActive = true });
        await db.SaveChangesAsync();
        return (db, new MaterialRequisitionService(db, parts: new PartCatalogueDuplicateService(db)));
    }

    [Fact]
    public async Task Registered_parts_are_linked_and_fill_an_empty_description_and_unit_typed_ones_stay_free_text()
    {
        var (db, mr) = await EnvAsync(nameof(Registered_parts_are_linked_and_fill_an_empty_description_and_unit_typed_ones_stay_free_text));

        var (ok, error, id) = await mr.CreateAsync("MR-428-1", 1, 1, null, "test", null, null, null, new()
        {
            ("", " pcr abc/001 ", 2, null),              // catalogue part, other spelling, no description: all from the part
            ("Own text", "mat9", 5, "PCS"),             // Material Master part: link, but the typed description and unit stay
            ("Valve 3in", "VLV-NEW-77", 1, "EA"),       // typed, not registered: free text
            ("", "DRAFT-1", 1, null),                   // a draft is not registered and there is no description: row skipped
            ("Bolt", null, 10, "EA"),                   // no part number at all
        });
        Assert.True(ok, error);

        var items = await db.MaterialRequisitionItems.Where(i => i.MRId == id).OrderBy(i => i.MRItemId).ToListAsync();
        Assert.Equal(4, items.Count);

        var catalogue = items[0];
        Assert.Equal(("PCR-ABC-001", "PLC CPU 1214C", "SET"), (catalogue.PartNumber, catalogue.ItemDescription, catalogue.Unit));
        Assert.NotNull(catalogue.PartCatalogueRequestId);
        Assert.Null(catalogue.MaterialItemId);

        var material = items[1];
        Assert.Equal(("MAT-9", "Own text", "PCS"), (material.PartNumber, material.ItemDescription, material.Unit));
        Assert.NotNull(material.MaterialItemId);

        var typed = items[2];
        Assert.Equal("VLV-NEW-77", typed.PartNumber);
        Assert.False(typed.IsCatalogued);

        Assert.Null(items[3].PartNumber);
        Assert.False(items[3].IsCatalogued);

        var dto = await mr.GetByIdAsync(id!.Value);
        Assert.Equal(new[] { true, true, false, false }, dto!.Items.OrderBy(i => i.MRItemId).Select(i => i.IsCatalogued));
    }

    [Fact]
    public async Task Exact_lookup_prefers_the_catalogue_and_the_search_carries_the_unit()
    {
        var (db, _) = await EnvAsync(nameof(Exact_lookup_prefers_the_catalogue_and_the_search_carries_the_unit));
        db.MaterialItems.Add(new MaterialItem { MaterialCode = "PCR-ABC-001", Description = "same part in Material Master", IsActive = true });
        await db.SaveChangesAsync();
        var parts = new PartCatalogueDuplicateService(db);

        var hit = await parts.FindRegisteredPartAsync("PCR ABC 001");
        Assert.Equal(("CatalogueRequest", "PCR-ABC-001", "SET"), (hit!.Source, hit.PartNumber, hit.Unit));
        Assert.Null(await parts.FindRegisteredPartAsync("PCR-ABC"));      // exact only, never a partial match
        Assert.Null(await parts.FindRegisteredPartAsync("  "));

        var search = await parts.SearchExistingPartsAsync("MAT-9");
        Assert.Equal("EA", Assert.Single(search).Unit);
    }

    [Fact]
    public void Create_form_wires_the_part_lookup_and_the_item_script()
    {
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "MaterialRequisition", "Create.cshtml"));
        Assert.Contains("data-pems-select-url=\"@Url.Action(\"PartLookup\")\"", view);
        Assert.Contains("data-pems-select-create", view);
        Assert.Contains("~/js/mr-items.js", view);
        Assert.DoesNotContain("<script>", view);

        var select = File.ReadAllText(Path.Combine(root, "PEMS.UI", "wwwroot", "pems-select.js"));
        Assert.Contains("data-pems-select-url", select);
        Assert.Contains("settings.create = true", select);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
