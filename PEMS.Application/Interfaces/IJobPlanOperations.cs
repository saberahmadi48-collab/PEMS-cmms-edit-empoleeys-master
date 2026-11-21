namespace PEMS.Application.Interfaces;

public sealed record JobPlanRow(int JobPlanId, string JobPlanNumber, string Title, decimal? EstimatedDurationHours, int TaskCount, int PlanUsageCount, bool IsActive, string EquipmentClass, bool PermitRequired, bool LotoRequired);
public sealed record JobPlanHeadRow(int JobPlanId, string JobPlanNumber, string Title, string Scope, decimal? EstimatedDurationHours, string SafetyRequirements, bool IsActive, string EquipmentClass, bool PermitRequired, bool LotoRequired);
public sealed record JobPlanTaskRow(int Id, int TaskNumber, string Title, string Description, int? EstimatedDurationMinutes);
public sealed record JobPlanSkillRow(int Id, string Skill, string MinimumProficiency, decimal? PlannedHours);
public sealed record JobPlanMaterialRow(int Id, string MaterialCode, string Description, decimal RequiredQuantity);

/// <summary>
/// Job plans (SCRUM-711) as read operations (list, head, tasks, required skills and materials, the choices of the forms). The commands are attributes on IJobPlanService (create, update, add or remove a task, skill or material;
/// JobPlan.Create and JobPlan.Edit as before).
/// </summary>
public interface IJobPlanOperations
{
    [PEMS.Application.Common.PemsOperation("JobPlanPage", "List", "JobPlan.View")]
    Task<List<JobPlanRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "Head", "JobPlan.View")]
    Task<List<JobPlanHeadRow>> HeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "Tasks", "JobPlan.View")]
    Task<List<JobPlanTaskRow>> TasksAsync(int id);

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "Skills", "JobPlan.View")]
    Task<List<JobPlanSkillRow>> SkillsAsync(int id);

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "Materials", "JobPlan.View")]
    Task<List<JobPlanMaterialRow>> MaterialsAsync(int id);

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "EquipmentClassChoices", "JobPlan.View")]
    Task<List<ReportChoiceRow>> EquipmentClassChoicesAsync();

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "SkillChoices", "JobPlan.View")]
    Task<List<ReportChoiceRow>> SkillChoicesAsync();

    [PEMS.Application.Common.PemsOperation("JobPlanPage", "MaterialChoices", "JobPlan.View")]
    Task<List<ReportChoiceRow>> MaterialChoicesAsync();
}
