using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-674: the readiness summary, the class activation backlog and the equipment rows, from IEquipmentReadinessService unchanged.</summary>
public sealed class CmmsReadinessReads(IEquipmentReadinessService readiness) : ICmmsReadinessReads
{
    public async Task<CmmsReadinessTotalsRow> TotalsAsync()
    {
        var s = await readiness.GetOperationalReadinessSummaryAsync();
        return new CmmsReadinessTotalsRow(1, s.TotalActiveEquipment, s.ClassifiedEquipment, s.UnclassifiedEquipment, s.CriticalityReadyEquipment, s.StrategyReadyEquipment, s.PmPlanReadyEquipment, s.JobPlanReadyEquipment, s.SafetyReadyEquipment, s.ExecutionReadyEquipment);
    }

    public async Task<List<CmmsClassBacklogRow>> ClassBacklogAsync()
        => (await readiness.GetClassActivationBacklogAsync()).Select(c => new CmmsClassBacklogRow(c.EquipmentClassId, c.Code, c.Name, c.EquipmentCount, c.CriticalityReadyCount, c.StrategyReadyCount, c.PmPlanReadyCount, c.ExecutionReadyCount, string.Join("; ", c.MissingRequirements))).ToList();

    public async Task<List<CmmsEquipmentReadinessRow>> EquipmentAsync()
    {
        var all = new List<CmmsEquipmentReadinessRow>();
        for (var page = 1; page <= 100; page++)
        {
            var result = await readiness.GetEquipmentReadinessAsync(null, page, 100);
            all.AddRange(result.Items.Select(r => new CmmsEquipmentReadinessRow(r.EquipmentId, r.TagNumber, r.EquipmentClassName ?? "", r.Classification, r.Criticality, r.Strategy, r.PmPlan, r.JobPlan, r.Safety, r.ExecutionReady)));
            if (result.Items.Count == 0 || all.Count >= result.TotalCount) break;
        }
        return all;
    }
}
