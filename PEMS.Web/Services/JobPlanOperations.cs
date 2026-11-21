using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-711: the read side of JobPlanController over IJobPlanService and the choice lists it read from the context.</summary>
public sealed class JobPlanOperations(IJobPlanService service, PEMSDbContext context) : IJobPlanOperations
{
    private static string S(int id) => id.ToString(CultureInfo.InvariantCulture);

    public async Task<List<JobPlanRow>> ListAsync() => (await service.GetListAsync(1, 500)).Items.Select(j => new JobPlanRow(j.JobPlanId, j.JobPlanNumber, j.Title, j.EstimatedDurationHours, j.TaskCount, j.PlanUsageCount, j.IsActive, j.EquipmentClassName ?? "", j.PermitRequired, j.LOTORequired)).ToList();

    public async Task<List<JobPlanHeadRow>> HeadAsync(int id)
        => await service.GetDetailsAsync(id) is { } j ? [new JobPlanHeadRow(j.JobPlanId, j.JobPlanNumber, j.Title, j.Scope ?? "", j.EstimatedDurationHours, j.SafetyRequirements ?? "", j.IsActive, j.EquipmentClassName ?? "", j.PermitRequired, j.LOTORequired)] : [];

    public async Task<List<JobPlanTaskRow>> TasksAsync(int id) => (await service.GetDetailsAsync(id))?.Tasks.Select(t => new JobPlanTaskRow(t.MaintenanceTaskId, t.TaskNumber, t.Title, t.Description ?? "", t.EstimatedDurationMinutes)).ToList() ?? [];
    public async Task<List<JobPlanSkillRow>> SkillsAsync(int id) => (await service.GetDetailsAsync(id))?.RequiredSkills.Select(s => new JobPlanSkillRow(s.JobPlanRequiredSkillId, s.SkillName, s.MinimumProficiencyLevel ?? "", s.PlannedHours)).ToList() ?? [];
    public async Task<List<JobPlanMaterialRow>> MaterialsAsync(int id) => (await service.GetDetailsAsync(id))?.RequiredMaterials.Select(m => new JobPlanMaterialRow(m.JobPlanRequiredMaterialId, m.MaterialCode, m.MaterialDescription ?? "", m.RequiredQuantity)).ToList() ?? [];

    public async Task<List<ReportChoiceRow>> EquipmentClassChoicesAsync() => (await context.EquipmentClasses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.EquipmentClassId, x.Name, x.Code }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EquipmentClassId), x.Code + " " + x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> SkillChoicesAsync() => (await context.Skills.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.SkillId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.SkillId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> MaterialChoicesAsync() => (await context.MaterialItems.AsNoTracking().Where(x => x.IsActive && x.IsStockItem).OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaterialItemId), x.MaterialCode + " " + x.Description)).ToList();
}
