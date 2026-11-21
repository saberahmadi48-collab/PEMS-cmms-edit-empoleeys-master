using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-658: what PartCatalogueAdminController showed and did, as operations over the Part Catalogue admin, lookup and settings services.</summary>
public sealed class PartCatalogueAdminOperations(IPartCatalogueAdminService admin, IPartCatalogueLookupService lookups, IPartCatalogueSettingsService settings, IRoleModuleActionService actions,
    IStringLocalizer<SharedResource> localizer) : IPartCatalogueAdminOperations
{
    private const long MaxImportBytes = 15L * 1024 * 1024;

    // the administration needs no employee record (a system admin without one is fine): the Admin permission decides
    private async Task<PcActor> ActorAsync(List<string> roles, int? employeeId, int? userId)
    {
        if (!await actions.HasActionAsync(roles, "PartCatalogue", "Admin")) throw new UnauthorizedAccessException();
        return new PcActor(employeeId ?? 0, userId, roles);
    }

    private async Task<PcAdminOverview> OverviewAsync(List<string> roles, int? employeeId, int? userId)
    {
        var overview = await admin.GetOverviewAsync(await ActorAsync(roles, employeeId, userId));
        return overview.Allowed && overview.Overview is not null ? overview.Overview : throw new UnauthorizedAccessException();
    }

    private (bool Success, string? Error) Back(PcResult r) => r.Success ? (true, null) : (false, string.Join(" | ", PcMessages.FormatAll(localizer, r.Errors)));

    public async Task<List<PcAdminCountRow>> CountsAsync(List<string> userRoles, int? employeeId, int? userId) => (await OverviewAsync(userRoles, employeeId, userId)).Counts.Select(c => new PcAdminCountRow(c.Key, c.Value)).ToList();

    public async Task<List<PcAdminSettingsRow>> SettingsAsync(List<string> userRoles, int? employeeId, int? userId)
    {
        await ActorAsync(userRoles, employeeId, userId);
        var s = await settings.GetAsync();
        return [new PcAdminSettingsRow(s.PartNoGenerationMethod, s.PartNoFormat ?? "", s.ModifiedDate, s.ModifiedBy ?? "", s.IsPersisted)];
    }

    public async Task<List<PcAdminSectionLine>> SectionsAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Sections.Select(s => new PcAdminSectionLine(s.SectionId, s.Code, s.NameFa ?? "", s.RequiresSparePartsReview, s.RoutingNeedsConfirmation, s.IsActive)).ToList();

    public async Task<List<PcAdminSectionGroupLine>> SectionGroupsAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Sections.SelectMany(s => s.Groups.Select(g => new PcAdminSectionGroupLine(g.MapId, s.Code, g.GroupText, g.NeedsConfirmation))).ToList();

    public async Task<List<PcAdminStepLine>> StepsAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Steps.Select(s => new PcAdminStepLine(s.StepId, s.StepKey, s.Order, s.NameEn, s.NameFa, s.Kind, s.Source, s.Condition, s.IsActive, s.NeedsConfirmation)).ToList();

    public async Task<List<PcAdminApproverLine>> ApproversAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Steps.SelectMany(s => s.Approvers.Select(a => new PcAdminApproverLine(a.Id, s.StepKey, a.EmployeeName))).ToList();

    public async Task<List<PcAdminGroup2Line>> Group2MapsAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Group2Maps.Select(m => new PcAdminGroup2Line(m.Id, m.Parent, m.Child, m.NeedsConfirmation)).ToList();

    public async Task<List<PcAdminResponsibilityLine>> ResponsibilitiesAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Responsibilities.Select(r => new PcAdminResponsibilityLine(r.Id, r.Key, r.DisplayName, r.Kind, r.RequiredAction ?? "", r.RoleName ?? "", r.ScopeType, r.ScopeValue ?? "", r.ManagerLevel, r.Strategy, r.IsActive,
            string.Join(", ", r.Positions.Select(p => $"{p.Title} ({p.Priority})")))).ToList();

    public async Task<List<PcAdminCommodityResponsibleLine>> CommodityResponsiblesAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).CommodityResponsibles.Select(c => new PcAdminCommodityResponsibleLine(c.Id, c.ResponsibilityKey, c.GroupText, c.PositionTitle ?? "", c.EmployeeName ?? "", c.Priority, c.IsActive)).ToList();

    public async Task<List<PcAdminFindingLine>> FindingsAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Findings.Select(f => new PcAdminFindingLine(f.Severity, f.Code, f.ResponsibilityKey, f.Message)).ToList();

    public async Task<List<PcAdminStageLine>> WorkflowStagesAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).WorkflowStages.Select(w => new PcAdminStageLine(w.Name, w.Order, w.ResponsibilityKey ?? "", w.EntryCondition ?? "")).ToList();

    public async Task<List<ReportChoiceRow>> SectionChoicesAsync(List<string> userRoles, int? employeeId, int? userId)
        => (await OverviewAsync(userRoles, employeeId, userId)).Sections.Select(s => new ReportChoiceRow(s.SectionId.ToString(CultureInfo.InvariantCulture), s.Code)).ToList();

    public async Task<object?> PreviewAsync(List<string> userRoles, int? employeeId, int? userId, string key, int requesterEmployeeId, int? commodityGroupId = null)
        => await admin.PreviewResolutionAsync(key, requesterEmployeeId, commodityGroupId, await ActorAsync(userRoles, employeeId, userId));

    public async Task<object?> CommodityGroupsAsync(List<string> userRoles, string? q = null)
    {
        await ActorAsync(userRoles, null, null);
        return await lookups.SearchCommodityGroupsAsync(q);
    }

    public async Task<object?> EmployeesAsync(List<string> userRoles, string? q = null)
    {
        await ActorAsync(userRoles, null, null);
        return await admin.SearchEmployeesAsync(q);
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(List<string> userRoles, int? employeeId, int? userId, string? partNoGenerationMethod, string? partNoFormat)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId);
        return Back(await settings.SaveAsync(partNoGenerationMethod, partNoFormat, (await settings.GetAsync()).RowVersion, actor));
    }

    public async Task<(bool Success, string? Error)> ImportAsync(List<string> userRoles, int? employeeId, int? userId, string kind, byte[] file)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId);
        if (file is null || file.Length == 0 || file.Length > MaxImportBytes) return (false, localizer["Pc_Err_ImportFile"].Value);
        await using var stream = new MemoryStream(file);
        var result = kind switch
        {
            "TechnicalClass" => await admin.ImportTechnicalClassesAsync(stream, actor),
            "CommodityGroup" => await admin.ImportCommodityGroupsAsync(stream, actor),
            "ProductFamily" => await admin.ImportProductFamiliesAsync(stream, actor),
            "Manufacturer" or "Supplier" => await admin.ImportNameListAsync(kind, stream, actor),
            _ => new PcImportResult { Kind = kind, Errors = { "Pc_Err_InvalidReference" } }
        };
        if (result.Errors.Count > 0 && result.Added + result.Updated == 0)
            return (false, string.Join(" | ", result.Errors.Take(5).Select(e => e.StartsWith("Pc_", StringComparison.Ordinal) ? localizer[e].Value : e)));
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetSectionAsync(List<string> userRoles, int? employeeId, int? userId, int sectionId, bool requiresSparePartsReview, bool isActive)
        => Back(await admin.SetSectionAsync(sectionId, requiresSparePartsReview, isActive, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> AddSectionGroupAsync(List<string> userRoles, int? employeeId, int? userId, int sectionId, int commodityGroupId)
        => Back(await admin.AddSectionGroupAsync(sectionId, commodityGroupId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> RemoveSectionGroupAsync(List<string> userRoles, int? employeeId, int? userId, int mapId)
        => Back(await admin.RemoveSectionGroupAsync(mapId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> AddGroupChildAsync(List<string> userRoles, int? employeeId, int? userId, int parentGroupId, int childGroupId)
        => Back(await admin.AddGroupChildAsync(parentGroupId, childGroupId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> RemoveGroupChildAsync(List<string> userRoles, int? employeeId, int? userId, int mapId)
        => Back(await admin.RemoveGroupChildAsync(mapId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> UpdateStepAsync(List<string> userRoles, int? employeeId, int? userId, int stepId, int order, bool isActive, string conditionKey)
        => Back(await admin.UpdateStepAsync(stepId, order, isActive, conditionKey, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> AddApproverAsync(List<string> userRoles, int? employeeId, int? userId, string stepKey, int approverEmployeeId)
        => Back(await admin.AddApproverAsync(stepKey, approverEmployeeId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> RemoveApproverAsync(List<string> userRoles, int? employeeId, int? userId, int assignmentId)
        => Back(await admin.RemoveApproverAsync(assignmentId, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> SaveResponsibilityAsync(List<string> userRoles, int? employeeId, int? userId, PcResponsibilityInput input)
        => Back(await admin.SaveResponsibilityAsync(input, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> AddCommodityResponsibleAsync(List<string> userRoles, int? employeeId, int? userId, string responsibilityKey, int commodityGroupId, int? positionId, int? responsibleEmployeeId, int priority = 1)
        => Back(await admin.AddCommodityResponsibleAsync(responsibilityKey, commodityGroupId, positionId, responsibleEmployeeId, priority, await ActorAsync(userRoles, employeeId, userId)));

    public async Task<(bool Success, string? Error)> RemoveCommodityResponsibleAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => Back(await admin.RemoveCommodityResponsibleAsync(id, await ActorAsync(userRoles, employeeId, userId)));
}
