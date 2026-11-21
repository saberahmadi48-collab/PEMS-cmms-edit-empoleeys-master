using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Tests.Engineering;

/// <summary>
/// SCRUM-559 (owner decision 2026-11-10): VDR details gets a More actions menu: edit item details, generate numbers (link to Admin),
/// hold / release (the shared document hold) and deactivate (soft delete). A held or deactivated item takes no review step.
/// </summary>
public class VdrMoreActionsTests
{
    private static PEMSDbContext NewDb() => new(new DbContextOptionsBuilder<PEMSDbContext>()
        .UseInMemoryDatabase("vdrma_" + Guid.NewGuid().ToString("N"))
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static async Task<(PEMSDbContext Db, VDRService Service, int ItemId)> NewAsync()
    {
        var db = NewDb();
        var project = new Project { ProjectCode = "PPJPC", ProjectName = "Polymer Plant Development", IsActive = true };
        var vendor = new Vendor { Name = "Hiva Sanat", IsActive = true };
        db.AddRange(project, vendor);
        await db.SaveChangesAsync();
        db.Equipments.AddRange(
            new Equipment { TagNumber = "P-101A", Name = "Pump", ProjectId = project.ProjectId },
            new Equipment { TagNumber = "K-201", Name = "Compressor", ProjectId = project.ProjectId });
        var item = new VDRItem
        {
            SequenceNo = 1, ProjectId = project.ProjectId, VendorId = vendor.VendorId, DocumentTypeCode = "LST", DocumentNumber = "J-88-HSC-HV-LST-1040-0144-013",
            DocumentNo = "J-88-HSC-HV-LST-1040-0144-013", Title = "Sub-Vendor List", DocumentKind = "VDR", DocumentDescription = "Sub-Vendor List for Exhaust Fan",
            WorkflowStatus = "Closed", IsActive = true
        };
        db.VDRItems.Add(item);
        await db.SaveChangesAsync();
        return (db, new VDRService(db), item.DocumentId);
    }

    // ------------------------------------------------------------ edit item details

    [Fact]
    public async Task Edit_saves_the_fields_resolves_the_equipment_tag_and_writes_one_audit_line()
    {
        var (db, service, id) = await NewAsync();

        var r = await service.UpdateDetailsAsync(id, new VdrItemEditInput(" New description ", 2, new DateTime(2026, 12, 1), "p-101a", " hv ", "VF-77"), "qa.doc");

        Assert.True(r.Success, r.Error);
        var item = await db.VDRItems.AsNoTracking().SingleAsync();
        Assert.Equal(("New description", (int?)2, "2", "HV", "VF-77"), (item.DocumentDescription, item.DocumentClass, item.DocumentClassRaw, item.DisciplineCodeRaw, item.VendorFormatDocumentNumber));
        Assert.Equal(db.Equipments.Single(e => e.TagNumber == "P-101A").EquipmentId, item.EquipmentId);
        Assert.Equal(new DateTime(2026, 12, 1), item.PlannedFirstIssueDate);
        // the context also writes its own change rows; count only this action's line
        IQueryable<AuditLog> Mine() => db.AuditLogs.Where(a => a.EntityName == nameof(VDRItem) && a.Action == "Update" && a.UserName == "qa.doc");
        var audit = Mine().Single();
        Assert.Equal(("Update", "qa.doc"), (audit.Action, audit.UserName));
        Assert.Contains("Equipment: - -> ", audit.ChangedFieldsSummary);

        // the same values again change nothing and write no second line; an empty tag clears the equipment
        Assert.True((await service.UpdateDetailsAsync(id, new VdrItemEditInput("New description", 2, new DateTime(2026, 12, 1), "P-101A", "HV", "VF-77"), "qa.doc")).Success);
        Assert.Single(Mine());
        Assert.True((await service.UpdateDetailsAsync(id, new VdrItemEditInput("New description", 2, new DateTime(2026, 12, 1), "", "HV", "VF-77"), "qa.doc")).Success);
        Assert.Null((await db.VDRItems.AsNoTracking().SingleAsync()).EquipmentId);
    }

    [Fact]
    public async Task Edit_refuses_an_unknown_tag_a_bad_class_and_an_inactive_item()
    {
        var (db, service, id) = await NewAsync();

        Assert.Equal("Vdr_Err_Equipment", (await service.UpdateDetailsAsync(id, new VdrItemEditInput("x", null, null, "NO-SUCH-TAG", null, null), "u")).Error);
        Assert.Equal("Vdr_Err_Class", (await service.UpdateDetailsAsync(id, new VdrItemEditInput("x", 0, null, null, null, null), "u")).Error);
        Assert.Equal("Vdr_Err_NotFound", (await service.UpdateDetailsAsync(id + 99, new VdrItemEditInput("x", null, null, null, null, null), "u")).Error);
        Assert.Equal("Sub-Vendor List for Exhaust Fan", (await db.VDRItems.AsNoTracking().SingleAsync()).DocumentDescription);

        Assert.True((await service.DeactivateAsync(id, "duplicate of 0144-012", "u")).Success);
        Assert.Equal("Vdr_Err_Inactive", (await service.UpdateDetailsAsync(id, new VdrItemEditInput("x", null, null, null, null, null), "u")).Error);
    }

    // ------------------------------------------------------------ deactivate

    [Fact]
    public async Task Deactivate_needs_a_reason_is_refused_while_a_review_runs_and_hides_the_item_from_the_lists()
    {
        var (db, service, id) = await NewAsync();
        Assert.Equal("Vdr_Err_ReasonRequired", (await service.DeactivateAsync(id, "  ", "u")).Error);

        var running = new WorkflowInstance { Module = "VDR", BusinessObjectType = "VDRItem", BusinessObjectId = id, Status = "Running" };
        db.WorkflowInstances.Add(running);
        await db.SaveChangesAsync();
        Assert.Equal("Vdr_Err_ReviewRunning", (await service.DeactivateAsync(id, "wrong item", "u")).Error);
        running.Status = "Completed";
        await db.SaveChangesAsync();

        Assert.Equal(1, (await service.GetPagedAsync(null, null, null, 1, 20)).TotalCount);
        var r = await service.DeactivateAsync(id, "wrong item", "qa.doc");

        Assert.True(r.Success, r.Error);
        Assert.Equal(0, (await service.GetPagedAsync(null, null, null, 1, 20)).TotalCount);
        var dto = (await service.GetByIdAsync(id))!;   // the details page still opens and shows the inactive banner
        Assert.False(dto.IsActive);
        Assert.Contains(db.AuditLogs, a => a.Action == "Deactivate" && a.ChangedFieldsSummary == "Reason: wrong item");
        Assert.Equal("Vdr_Err_Inactive", (await service.DeactivateAsync(id, "again", "u")).Error);
    }

    // ------------------------------------------------------------ hold

    [Fact]
    public async Task A_held_item_shows_the_open_hold_and_takes_no_review_step_until_it_is_released()
    {
        var (db, service, id) = await NewAsync();
        var holds = new DocumentHoldService(db);
        var workflow = new VdrWorkflowService(db, new FakeWorkflowRuntime(), new FakeLetterService());

        await holds.PlaceHoldAsync(id, "vendor sent the wrong revision", null, null);

        var dto = (await service.GetByIdAsync(id))!;
        Assert.Equal((true, "vendor sent the wrong revision"), (dto.IsOnHold, dto.HoldReason));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => workflow.SubmitForReviewAsync(id, 1, "VT-1", null, "IFA", null, null, new[] { "DocumentController" }));
        Assert.Contains("Hold", ex.Message);
        await Assert.ThrowsAnyAsync<Exception>(() => workflow.LeadReviewAsync(id, "AP", null, null, 1, new[] { "EngineeringManager" }));

        await holds.ReleaseHoldAsync(id, "correct revision received", null);
        Assert.False((await service.GetByIdAsync(id))!.IsOnHold);
    }

    [Fact]
    public async Task A_deactivated_item_takes_no_review_step()
    {
        var (db, service, id) = await NewAsync();
        var workflow = new VdrWorkflowService(db, new FakeWorkflowRuntime(), new FakeLetterService());
        await service.DeactivateAsync(id, "wrong item", "u");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => workflow.SubmitForReviewAsync(id, 1, "VT-1", null, "IFA", null, null, new[] { "DocumentController" }));
        Assert.Contains("غیرفعال", ex.Message);
    }

    // ------------------------------------------------------------ page, grants

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SupportedReleases.RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void The_menu_shows_each_item_only_with_its_permission_and_posts_to_the_new_actions()
    {
        var view = Read("PEMS.Web", "Views", "VDR", "Details.cshtml");
        var menu = view[view.IndexOf("id=\"vdrMoreActions\"", StringComparison.Ordinal)..view.IndexOf("</pems-dropdown-menu>", StringComparison.Ordinal)];

        Assert.Contains("@if (vdrCanEdit)", menu);
        Assert.Contains("@if (vdrCanHold)", menu);
        Assert.Contains("@if (vdrCanRelease)", menu);
        Assert.Contains("@if (vdrCanDeactivate)", menu);
        Assert.Contains("asp-controller=\"Numbering\"", menu);   // SCRUM-529 link moved into the menu
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(view, "asp-controller=\"Numbering\"").Count);
        Assert.Contains("var vdrCanEdit = Model.IsActive && ViewBag.CanEditItem is true;", view);
        foreach (var action in new[] { "asp-action=\"UpdateDetails\"", "\"Hold\"", "\"ReleaseHold\"", "\"Deactivate\"" })
            Assert.Contains(action, view);

        var controller = Read("PEMS.Web", "Controllers", "VDRController.cs");
        foreach (var (name, perm) in new[] { ("CanEditItem", "Edit"), ("CanHold", "Hold"), ("CanReleaseHold", "ReleaseHold"), ("CanDeactivate", "Delete") })
            Assert.Contains($"ViewBag.{name} = await _permissionService.HasPermissionAsync(_currentUser.Roles, \"VDR\", \"{perm}\");", controller);
        Assert.Contains("EnsurePermissionAsync(\"Delete\", vdrItemId", controller);
        Assert.Contains("EnsurePermissionAsync(\"Edit\", vdrItemId, T(\"Vdr_Err_Forbidden\"))", controller);
    }

    [Fact]
    public void Hold_grants_follow_engineering_documents_for_new_and_existing_databases()
    {
        var seed = Read("PEMS.Persistence", "Servises", "PermissionService.cs");
        Assert.Contains("New(SystemRoles.EngineeringManager, \"VDR\", create: true, edit: true, del: true, approve: true, hold: true, releaseHold: true)", seed);
        Assert.Contains("New(SystemRoles.EngineeringSupervisor, \"VDR\", hold: true, releaseHold: true)", seed);
        Assert.Contains("New(SystemRoles.DocumentController, \"VDR\", create: true, edit: true, hold: true, releaseHold: true)", seed);
        Assert.Contains("New(SystemRoles.DisciplineEngineer, \"VDR\", edit: true, approve: true, hold: true)", seed);

        var file = Directory.GetFiles(Path.Combine(SupportedReleases.RepoRoot(), "PEMS.Migrations", "Migrations"), "*_Scrum559VdrHoldGrants.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var sql = File.ReadAllText(file).Replace("\r\n", "\n");
        Assert.Contains("SET [CanHold] = 1, [CanReleaseHold] = 1\nWHERE [ModuleName] = N'VDR' AND [RoleName] IN (N'EngineeringManager', N'DocumentController', N'EngineeringSupervisor')", sql);
        Assert.Contains("SET [CanHold] = 1\nWHERE [ModuleName] = N'VDR' AND [RoleName] = N'DisciplineEngineer'", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }
}
