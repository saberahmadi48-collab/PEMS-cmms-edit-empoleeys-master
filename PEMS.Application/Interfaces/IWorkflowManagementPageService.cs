using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public sealed record WfResultDto(bool Success, string? Error);

public class WfOptionRowDto { public string Value { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

public class WfDefinitionRecordDto
{
    public int DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int? PublishedVersion { get; set; }
    public int? OpenDraftVersion { get; set; }
    public bool Editable { get; set; }
    public string EditableReason { get; set; } = string.Empty;
    public int TotalInstances { get; set; }
}

public class WfVersionRowDto
{
    public int DefinitionId { get; set; }
    public int VersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Stages { get; set; }
    public int Transitions { get; set; }
    public DateTime? PublishedDate { get; set; }
    public DateTime? RetiredDate { get; set; }
    public int? ClonedFromVersion { get; set; }
    public string? ChangeSummary { get; set; }
    public bool IsBound { get; set; }
    public int RunningInstances { get; set; }
    public int TotalInstances { get; set; }
}

public class WfUsageRowDto
{
    public int DefinitionId { get; set; }
    public int VersionNumber { get; set; }
    public int Running { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Rejected { get; set; }
}

public class WfAuditRowDto
{
    public int DefinitionId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Actor { get; set; }
    public int? VersionNumber { get; set; }
    public string? Previous { get; set; }
    public string? Current { get; set; }
    public string? Details { get; set; }
}

public class WfVersionRecordDto
{
    public int VersionId { get; set; }
    public int DefinitionId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool CanEditStructure { get; set; }
    public string? ChangeSummary { get; set; }
    public string? LastEditor { get; set; }
    public int RunningInstances { get; set; }
}

public class WfStageRowDto
{
    public int VersionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int StageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? BusinessStatus { get; set; }
    public int StageOrder { get; set; }
    public string StageType { get; set; } = string.Empty;
    public string? ResponsibleRole { get; set; }
    public string ApprovalStrategy { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public int? TimeoutHours { get; set; }
}

public class WfTransitionRowDto
{
    public int VersionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int TransitionId { get; set; }
    public int? FromStageId { get; set; }
    public int? ToStageId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? RequiredPermission { get; set; }
    public string? ConditionExpression { get; set; }
    public int Priority { get; set; }
    public bool ForbidInitiator { get; set; }
}

public class WfBindingImpactRowDto
{
    public int BindingId { get; set; }
    public int TargetVersionId { get; set; }
    public string Definition { get; set; } = string.Empty;
    public int? CurrentVersion { get; set; }
    public int TargetVersion { get; set; }
    public int RunningInstances { get; set; }
}

/// <summary>SCRUM-651: the workflow management screens as operations of a page service (the actor is the current user; every rule and permission check stays in IWorkflowAdministrationService). The ids of stage and transition rows are "versionId-id".</summary>
public interface IWorkflowManagementPageService
{
    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Definitions", Roles = "SystemAdmin")]
    Task<List<WorkflowDefinitionRow>> DefinitionsAsync();

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "DefinitionRecord", Roles = "SystemAdmin")]
    Task<List<WfDefinitionRecordDto>> DefinitionRecordAsync(int definitionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Versions", Roles = "SystemAdmin")]
    Task<List<WfVersionRowDto>> VersionsAsync(int definitionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Bindings", Roles = "SystemAdmin")]
    Task<List<WorkflowBindingRow>> BindingsAsync(int? definitionId = null);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Usage", Roles = "SystemAdmin")]
    Task<List<WfUsageRowDto>> UsageAsync(int definitionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Audit", Roles = "SystemAdmin")]
    Task<List<WfAuditRowDto>> AuditAsync(int definitionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "VersionRecord", Roles = "SystemAdmin")]
    Task<List<WfVersionRecordDto>> VersionRecordAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Stages", Roles = "SystemAdmin")]
    Task<List<WfStageRowDto>> StagesAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Transitions", Roles = "SystemAdmin")]
    Task<List<WfTransitionRowDto>> TransitionsAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "StageOf", Roles = "SystemAdmin")]
    Task<List<WorkflowStageEditModel>> StageOfAsync(string id);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "TransitionOf", Roles = "SystemAdmin")]
    Task<List<WorkflowTransitionEditModel>> TransitionOfAsync(string id);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "BindingImpact", Roles = "SystemAdmin")]
    Task<List<WfBindingImpactRowDto>> BindingImpactAsync(int bindingId, int targetVersionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "EmployeeOptions", Roles = "SystemAdmin")]
    Task<List<WfOptionRowDto>> EmployeeOptionsAsync();

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "UpdateDefinition", Roles = "SystemAdmin")]
    Task<WfResultDto> UpdateDefinitionAsync(int definitionId, string name, string? nameFa, string? description);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Archive", Roles = "SystemAdmin")]
    Task<WfResultDto> ArchiveAsync(int definitionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Clone", Roles = "SystemAdmin")]
    Task<WfResultDto> CloneAsync(int versionId, string? summary);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Discard", Roles = "SystemAdmin")]
    Task<WfResultDto> DiscardAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Validate", Roles = "SystemAdmin")]
    Task<WfResultDto> ValidateAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Publish", Roles = "SystemAdmin")]
    Task<WfResultDto> PublishAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "Retire", Roles = "SystemAdmin")]
    Task<WfResultDto> RetireAsync(int versionId);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "SaveStage", Roles = "SystemAdmin")]
    Task<WfResultDto> SaveStageAsync(int versionId, WorkflowStageEditModel stage);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "SaveStageOf", Roles = "SystemAdmin")]
    Task<WfResultDto> SaveStageOfAsync(string id, WorkflowStageEditModel stage);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "SaveTransitionOf", Roles = "SystemAdmin")]
    Task<WfResultDto> SaveTransitionOfAsync(string id, WorkflowTransitionEditModel transition);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "DeleteStage", Roles = "SystemAdmin")]
    Task<WfResultDto> DeleteStageAsync(string id);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "SaveTransition", Roles = "SystemAdmin")]
    Task<WfResultDto> SaveTransitionAsync(int versionId, WorkflowTransitionEditModel transition);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "DeleteTransition", Roles = "SystemAdmin")]
    Task<WfResultDto> DeleteTransitionAsync(string id);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "ChangeBinding", Roles = "SystemAdmin")]
    Task<WfResultDto> ChangeBindingAsync(int bindingId, int targetVersionId, bool confirmed);

    [PEMS.Application.Common.PemsOperation("WorkflowManagement", "DeactivateBinding", Roles = "SystemAdmin")]
    Task<WfResultDto> DeactivateBindingAsync(int bindingId, bool confirmed);
}
