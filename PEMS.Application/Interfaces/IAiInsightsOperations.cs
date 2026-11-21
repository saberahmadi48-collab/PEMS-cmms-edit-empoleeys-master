namespace PEMS.Application.Interfaces;

/// <summary>One equipment with recurring NCRs (SCRUM-668).</summary>
public sealed record AiNcrPatternRow(int EquipmentId, string EquipmentTagNumber, string EquipmentName, int NcrCount, string NcrNumbers, string? DominantQcDiscipline, bool HasOpenMoc, string Suggestion);

/// <summary>One group of the delay analysis (discipline, stage or vendor).</summary>
public sealed record AiDelayGroupRow(string GroupName, int Count, double AvgDelayDays);

/// <summary>The totals and the narrative of the delay analysis (one row).</summary>
public sealed record AiDelaySummaryRow(int TotalDelayedDocuments, double AverageDelayDays, string? NarrativeSummary);

/// <summary>The reads of the engineering copilot page as operations, in the shapes a grid or a record binding can show (SCRUM-668).</summary>
public interface IAiInsightsOperations
{
    [PEMS.Application.Common.PemsOperation("AiInsights", "NcrPatterns")]
    Task<List<AiNcrPatternRow>> NcrPatternsAsync(int? projectId = null);

    [PEMS.Application.Common.PemsOperation("AiInsights", "DelaySummary")]
    Task<AiDelaySummaryRow> DelaySummaryAsync(int? projectId = null);

    [PEMS.Application.Common.PemsOperation("AiInsights", "DelayByDiscipline")]
    Task<List<AiDelayGroupRow>> DelayByDisciplineAsync(int? projectId = null);

    [PEMS.Application.Common.PemsOperation("AiInsights", "DelayByStage")]
    Task<List<AiDelayGroupRow>> DelayByStageAsync(int? projectId = null);

    [PEMS.Application.Common.PemsOperation("AiInsights", "DelayByVendor")]
    Task<List<AiDelayGroupRow>> DelayByVendorAsync(int? projectId = null);
}
