using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-668: the engineering copilot reads in the shapes of <see cref="IAiInsightsOperations"/>; the numbers still come from <see cref="IEngineeringCopilotService"/>.</summary>
public sealed class AiInsightsOperations(IEngineeringCopilotService copilot) : IAiInsightsOperations
{
    public async Task<List<AiNcrPatternRow>> NcrPatternsAsync(int? projectId = null)
        => (await copilot.GetRecurringNcrPatternsAsync(projectId)).Select(p => new AiNcrPatternRow(p.EquipmentId, p.EquipmentTagNumber, p.EquipmentName, p.NcrCount, string.Join(", ", p.NcrNumbers), p.DominantQCDiscipline, p.HasOpenMOC, p.Suggestion)).ToList();

    public async Task<AiDelaySummaryRow> DelaySummaryAsync(int? projectId = null)
    {
        var delay = await copilot.GetDelayRootCauseAnalysisAsync(projectId);
        return new AiDelaySummaryRow(delay.TotalDelayedDocuments, delay.AverageDelayDays, delay.NarrativeSummary);
    }

    public async Task<List<AiDelayGroupRow>> DelayByDisciplineAsync(int? projectId = null)
        => (await copilot.GetDelayRootCauseAnalysisAsync(projectId)).ByDiscipline.Select(g => new AiDelayGroupRow(g.GroupName, g.Count, g.AvgDelayDays)).ToList();

    public async Task<List<AiDelayGroupRow>> DelayByStageAsync(int? projectId = null)
        => (await copilot.GetDelayRootCauseAnalysisAsync(projectId)).ByStage.Select(g => new AiDelayGroupRow(g.GroupName, g.Count, g.AvgDelayDays)).ToList();

    public async Task<List<AiDelayGroupRow>> DelayByVendorAsync(int? projectId = null)
        => (await copilot.GetDelayRootCauseAnalysisAsync(projectId)).ByVendor.Select(g => new AiDelayGroupRow(g.GroupName, g.Count, g.AvgDelayDays)).ToList();
}
