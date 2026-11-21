namespace PEMS.WorkflowPlatform.Interfaces;

/// <summary>
/// SCRUM-103: what a module's code relies on in its workflow definition. Everything else in the definition (participants,
/// SLA, approval strategy, conditions, extra stages and transitions, labels) is free to edit in Workflow Management;
/// a version that breaks the contract fails validation and therefore cannot be published.
/// A definition whose (Module, Category) has no contract is not driven by module code (a user-built BPMS process) and
/// has no such limits.
/// </summary>
/// <param name="Module">WorkflowDefinition.Module.</param>
/// <param name="Category">WorkflowDefinition.Category (the binding context).</param>
/// <param name="EntityType">BusinessObjectType the module passes to StartInstanceAsync (the binding entity type).</param>
/// <param name="RequiredActions">Action types the module executes; each must exist on at least one transition.</param>
/// <param name="RequiredStages">Stages the module looks up by name, optionally with the stage type it needs.</param>
/// <param name="RequiredTransitions">Hand-overs the module depends on, e.g. the action that passes dynamic participants to the next stage.</param>
/// <param name="RequiresBusinessStatus">The module derives the record status from WorkflowStage.BusinessStatus, so every stage needs one.</param>
public sealed record WorkflowModuleContract(
    string Module,
    string Category,
    string EntityType,
    IReadOnlyList<string> RequiredActions,
    IReadOnlyList<WorkflowStageRequirement> RequiredStages,
    IReadOnlyList<WorkflowTransitionRequirement> RequiredTransitions,
    bool RequiresBusinessStatus = false);

/// <param name="Name">WorkflowStage.Name (case-sensitive, as the module code compares it).</param>
/// <param name="StageType">"Sequential" / "Parallel" when the module needs that type (dynamic participants need Parallel); null = any.</param>
public sealed record WorkflowStageRequirement(string Name, string? StageType = null);

/// <param name="FromStage">Source stage name; null = the start transition.</param>
/// <param name="ActionType">Action on that transition.</param>
/// <param name="ToStage">Target stage name; null = ends the workflow.</param>
public sealed record WorkflowTransitionRequirement(string? FromStage, string ActionType, string? ToStage);

public interface IWorkflowModuleContractRegistry
{
    void Register(WorkflowModuleContract contract);
    WorkflowModuleContract? Find(string module, string category);
    IReadOnlyList<WorkflowModuleContract> All();
}
