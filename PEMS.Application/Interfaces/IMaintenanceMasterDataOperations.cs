using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>One master data line of any list of the maintenance master data page: code, name, the details in one text, active.</summary>
public sealed record MasterDataRow(int Id, string Code, string Name, string Detail, bool IsActive);

/// <summary>
/// The maintenance master data (SCRUM-723) as operations. The lists of the old page are read operations (one row shape), and every create, toggle, move and update command of MaintenanceMasterDataController is an operation with the
/// same validation and messages (MaintenanceMasterData.Create for the new rows, MaintenanceMasterData.Edit for the toggles and updates). Each message of the old page is the answer text; the success texts are in the action definitions.
/// </summary>
public interface IMaintenanceMasterDataOperations
{
    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Groups", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> GroupsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Skills", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> SkillsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Qualifications", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> QualificationsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Certifications", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> CertificationsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "MaintenanceTypes", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> MaintenanceTypesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "StrategyTypes", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> StrategyTypesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "EquipmentClasses", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> EquipmentClassesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Standards", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> StandardsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "Activities", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> ActivitiesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "FailureModes", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> FailureModesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "PmTemplates", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> PmTemplatesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "EvaluationProfiles", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> EvaluationProfilesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "EvaluationLimits", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> EvaluationLimitsAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "ConditionPriorities", "MaintenanceMasterData.View")]
    Task<List<MasterDataRow>> ConditionPrioritiesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "DisciplinesChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> DisciplinesChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "GroupsChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> GroupsChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "MaintenanceTypesChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> MaintenanceTypesChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "StandardsChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> StandardsChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "EquipmentClassesChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> EquipmentClassesChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterDataPage", "ProfilesChoices", "MaintenanceMasterData.View")]
    Task<List<ReportChoiceRow>> ProfilesChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateGroup", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateGroupAsync(string code, string name, int disciplineId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleGroupActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleGroupActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateSubGroup", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateSubGroupAsync(string code, string name, int disciplineId, int parentMaintenanceGroupId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "MoveGroup", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> MoveGroupAsync(int maintenanceGroupId, int? newParentMaintenanceGroupId);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateSkill", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateSkillAsync(string name, int? disciplineId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleSkillActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleSkillActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateQualification", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateQualificationAsync(string name, int? disciplineId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleQualificationActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleQualificationActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateCertification", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateCertificationAsync(string name, int? disciplineId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleCertificationActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleCertificationActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateMaintenanceType", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateMaintenanceTypeAsync(string code, string name, string? nameFa, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleMaintenanceTypeActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleMaintenanceTypeActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateMaintenanceStrategyType", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateMaintenanceStrategyTypeAsync(string code, string name, string? nameFa, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleMaintenanceStrategyTypeActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleMaintenanceStrategyTypeActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateEquipmentClass", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateEquipmentClassAsync(string code, string name, string? nameFa, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleEquipmentClassActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleEquipmentClassActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateStandardReference", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateStandardReferenceAsync(string code, string organization, string standardNumber, string title, string? category, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleStandardReferenceActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleStandardReferenceActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateMaintenanceActivity", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateMaintenanceActivityAsync(string code, string name, string? nameFa, int maintenanceGroupId, int? maintenanceTypeId, string? description);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleMaintenanceActivityActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleMaintenanceActivityActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleFailureModeActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleFailureModeActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "TogglePMTemplateActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> TogglePMTemplateActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateEvaluationProfile", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateEvaluationProfileAsync(string code, string name, string? nameFa, string variableType, int? standardReferenceId, int? equipmentClassId, string? operatingCondition, DateTime? effectiveFrom);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleEvaluationProfileActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleEvaluationProfileActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "AddEvaluationLimit", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> AddEvaluationLimitAsync(int evaluationProfileId, string status, decimal? lowValue, decimal? highValue, bool lowInclusive, bool highInclusive);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleEvaluationLimitActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleEvaluationLimitActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "CreateConditionPriorityMapping", "MaintenanceMasterData.Create")]
    Task<(bool Success, string? Error)> CreateConditionPriorityMappingAsync(string condition, string priority);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "UpdateConditionPriorityMapping", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> UpdateConditionPriorityMappingAsync(int id, string priority);

    [PEMS.Application.Common.PemsOperation("MaintenanceMasterData", "ToggleConditionPriorityMappingActive", "MaintenanceMasterData.Edit")]
    Task<(bool Success, string? Error)> ToggleConditionPriorityMappingActiveAsync(int id);
}
