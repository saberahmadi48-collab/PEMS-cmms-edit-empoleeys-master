using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public sealed record PcAdminCountRow(string Key, int Count);
public sealed record PcAdminSettingsRow(string PartNoGenerationMethod, string PartNoFormat, DateTime? ModifiedDate, string ModifiedBy, bool IsPersisted);
public sealed record PcAdminSectionLine(int SectionId, string Code, string NameFa, bool RequiresSparePartsReview, bool RoutingNeedsConfirmation, bool IsActive);
public sealed record PcAdminSectionGroupLine(int MapId, string Section, string Group, bool NeedsConfirmation);
public sealed record PcAdminStepLine(int StepId, string StepKey, int Order, string NameEn, string NameFa, string Kind, string Source, string Condition, bool IsActive, bool NeedsConfirmation);
public sealed record PcAdminApproverLine(int AssignmentId, string StepKey, string Employee);
public sealed record PcAdminGroup2Line(int Id, string Parent, string Child, bool NeedsConfirmation);
public sealed record PcAdminResponsibilityLine(int Id, string Key, string Name, string Kind, string RequiredAction, string Role, string ScopeType, string ScopeValue, int? ManagerLevel, string Strategy, bool IsActive, string Positions);
public sealed record PcAdminCommodityResponsibleLine(int Id, string ResponsibilityKey, string Group, string Position, string Employee, int Priority, bool IsActive);
public sealed record PcAdminFindingLine(string Severity, string Code, string ResponsibilityKey, string Message);
public sealed record PcAdminStageLine(string Name, int Order, string ResponsibilityKey, string EntryCondition);

/// <summary>
/// The Part Catalogue administration (SCRUM-658) as operations: the overview (settings, sections and their commodity groups, workflow steps and approvers, group maps, responsibilities, findings) as read operations, the
/// diagnosis of who would be resolved for a responsibility and the two lookups of the page as file-host answers, and every command of the old controller (settings, the reference imports, sections, group maps, steps, approvers,
/// responsibilities, commodity responsibles). Every operation checks the PartCatalogue Admin action like the controller did (the services check it again). The service messages are shown as text of the page language.
/// </summary>
public interface IPartCatalogueAdminOperations
{
    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Counts")]
    Task<List<PcAdminCountRow>> CountsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Settings")]
    Task<List<PcAdminSettingsRow>> SettingsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Sections")]
    Task<List<PcAdminSectionLine>> SectionsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "SectionGroups")]
    Task<List<PcAdminSectionGroupLine>> SectionGroupsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Steps")]
    Task<List<PcAdminStepLine>> StepsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Approvers")]
    Task<List<PcAdminApproverLine>> ApproversAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Group2Maps")]
    Task<List<PcAdminGroup2Line>> Group2MapsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Responsibilities")]
    Task<List<PcAdminResponsibilityLine>> ResponsibilitiesAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "CommodityResponsibles")]
    Task<List<PcAdminCommodityResponsibleLine>> CommodityResponsiblesAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Findings")]
    Task<List<PcAdminFindingLine>> FindingsAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "WorkflowStages")]
    Task<List<PcAdminStageLine>> WorkflowStagesAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "SectionChoices")]
    Task<List<ReportChoiceRow>> SectionChoicesAsync(List<string> userRoles, int? employeeId, int? userId);

    /// <summary>Who is eligible for a responsibility (and who was removed and why) for a requester and a commodity group, as JSON.</summary>
    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Preview")]
    Task<object?> PreviewAsync(List<string> userRoles, int? employeeId, int? userId, string key, int requesterEmployeeId, int? commodityGroupId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "CommodityGroups")]
    Task<object?> CommodityGroupsAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Employees")]
    Task<object?> EmployeesAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "SaveSettings")]
    Task<(bool Success, string? Error)> SaveSettingsAsync(List<string> userRoles, int? employeeId, int? userId, string? partNoGenerationMethod, string? partNoFormat);

    /// <summary>Imports a reference workbook: kind is TechnicalClass, CommodityGroup, ProductFamily, Manufacturer or Supplier.</summary>
    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "Import")]
    Task<(bool Success, string? Error)> ImportAsync(List<string> userRoles, int? employeeId, int? userId, string kind, byte[] file);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "SetSection")]
    Task<(bool Success, string? Error)> SetSectionAsync(List<string> userRoles, int? employeeId, int? userId, int sectionId, bool requiresSparePartsReview, bool isActive);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "AddSectionGroup")]
    Task<(bool Success, string? Error)> AddSectionGroupAsync(List<string> userRoles, int? employeeId, int? userId, int sectionId, int commodityGroupId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "RemoveSectionGroup")]
    Task<(bool Success, string? Error)> RemoveSectionGroupAsync(List<string> userRoles, int? employeeId, int? userId, int mapId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "AddGroupChild")]
    Task<(bool Success, string? Error)> AddGroupChildAsync(List<string> userRoles, int? employeeId, int? userId, int parentGroupId, int childGroupId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "RemoveGroupChild")]
    Task<(bool Success, string? Error)> RemoveGroupChildAsync(List<string> userRoles, int? employeeId, int? userId, int mapId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "UpdateStep")]
    Task<(bool Success, string? Error)> UpdateStepAsync(List<string> userRoles, int? employeeId, int? userId, int stepId, int order, bool isActive, string conditionKey);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "AddApprover")]
    Task<(bool Success, string? Error)> AddApproverAsync(List<string> userRoles, int? employeeId, int? userId, string stepKey, int approverEmployeeId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "RemoveApprover")]
    Task<(bool Success, string? Error)> RemoveApproverAsync(List<string> userRoles, int? employeeId, int? userId, int assignmentId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "SaveResponsibility")]
    Task<(bool Success, string? Error)> SaveResponsibilityAsync(List<string> userRoles, int? employeeId, int? userId, PcResponsibilityInput input);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "AddCommodityResponsible")]
    Task<(bool Success, string? Error)> AddCommodityResponsibleAsync(List<string> userRoles, int? employeeId, int? userId, string responsibilityKey, int commodityGroupId, int? positionId, int? responsibleEmployeeId, int priority = 1);

    [PEMS.Application.Common.PemsOperation("PartCatalogueAdmin", "RemoveCommodityResponsible")]
    Task<(bool Success, string? Error)> RemoveCommodityResponsibleAsync(List<string> userRoles, int? employeeId, int? userId, int id);
}
