using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 7» بند ۲۰: پیاده‌سازی حلقه‌ی بازخورد RCM. GenerateEvidenceAsync فقط
/// می‌خواند و رکورد شواهد می‌سازد - هرگز MaintenanceStrategy/MaintenancePlan/PMTemplate/
/// RCMDecision/Criticality را تغییر نمی‌دهد. ReviewRecommendationAsync هم فقط وضعیت خودِ
/// توصیه را تغییر می‌دهد (PENDING → ACCEPTED/REJECTED) - تغییر واقعی در استراتژی، اگر
/// پذیرفته شود، جدا و دستی توسط همان مهندس انجام می‌شود.
/// </summary>
public class RcmFeedbackService : IRcmFeedbackService
{
    private readonly PEMSDbContext _context;

    // طبق بند ۲۰: آستانه‌های صریح و مستندشده - نه یک قانون پنهان.
    private const int HighFrequencyFailureThreshold = 3;
    private const int RepeatFailureMinimumOccurrences = 2;

    public RcmFeedbackService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<GenerateEvidenceResultDto> GenerateEvidenceAsync(int windowDays = 90, int downtimeThresholdHours = 24)
    {
        var result = new GenerateEvidenceResultDto();
        var now = DateTime.Now;
        var windowStart = now.AddDays(-windowDays);

        // RCM کلاس‌محور: ارزیابی به‌ازای هر جفت (استراتژی، تگ) انجام می‌شود - تگ‌ها از تخصیص‌های فعال
        // می‌آیند و استراتژی‌های قدیمی تک‌تگی (EquipmentId مستقیم) هم همچنان پوشش داده می‌شوند.
        var legacyPairs = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.IsActive && x.EquipmentId != null)
            .ToListAsync();
        var assignmentPairs = await _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.Status == "Active" && a.MaintenanceStrategy!.IsActive)
            .Select(a => new { a.MaintenanceStrategy, a.EquipmentId })
            .ToListAsync();

        var pairs = legacyPairs.Select(x => (Strategy: x, EquipmentId: x.EquipmentId!.Value))
            .Concat(assignmentPairs.Select(a => (Strategy: a.MaintenanceStrategy!, EquipmentId: a.EquipmentId)))
            .GroupBy(p => (p.Strategy.MaintenanceStrategyId, p.EquipmentId))
            .Select(g => g.First())
            .ToList();

        result.StrategiesEvaluated = pairs.Count;

        foreach (var (strategy, strategyEquipmentId) in pairs)
        {
            var failuresQuery = _context.FailureRecords.AsNoTracking()
                .Where(x => x.IsActive && x.EquipmentId == strategyEquipmentId && x.FailureDate >= windowStart && x.FailureDate <= now);

            // طبق بند ۲۰: اگر استراتژی به یک حالت‌خرابی مشخص وصل است، فقط خرابی‌های همان حالت
            // شمرده می‌شوند - وگرنه (استراتژی عمومی بدون FailureModeCode) همه‌ی خرابی‌های همان
            // تجهیز در این بازه در نظر گرفته می‌شوند.
            if (!string.IsNullOrWhiteSpace(strategy.FailureModeCode))
            {
                failuresQuery = failuresQuery.Where(x =>
                    (x.FailureModeRef != null && x.FailureModeRef.Code == strategy.FailureModeCode) ||
                    x.FailureMode == strategy.FailureModeCode);
            }

            var failureDates = await failuresQuery.OrderBy(x => x.FailureDate).Select(x => x.FailureDate).ToListAsync();
            var failureCountInWindow = failureDates.Count;
            if (failureCountInWindow == 0) continue;

            var downtimeInWindow = await failuresQuery.SumAsync(x => (decimal?)x.DowntimeHours) ?? 0;

            var repeatFailureCount = 0;
            for (var i = 1; i < failureDates.Count; i++)
            {
                if ((failureDates[i] - failureDates[i - 1]).TotalDays <= windowDays) repeatFailureCount++;
            }
            var hasRepeatFailure = failureDates.Count >= RepeatFailureMinimumOccurrences && repeatFailureCount > 0;

            var triggers = new List<(string Reason, string Evidence)>();
            if (hasRepeatFailure)
                triggers.Add(("RepeatFailure", $"{failureDates.Count} occurrences of this failure mode within {windowDays} days, including at least one pair {windowDays} days or less apart."));
            if (failureCountInWindow >= HighFrequencyFailureThreshold)
                triggers.Add(("HighFailureFrequency", $"{failureCountInWindow} qualifying failures recorded in the last {windowDays} days (threshold: {HighFrequencyFailureThreshold})."));
            if (downtimeInWindow >= downtimeThresholdHours)
                triggers.Add(("HighDowntime", $"{downtimeInWindow:0.#} hours of recorded downtime in the last {windowDays} days (threshold: {downtimeThresholdHours}h)."));

            foreach (var (reason, evidence) in triggers)
            {
                var alreadyPending = await _context.RcmReviewRecommendations
                    .AnyAsync(x => x.MaintenanceStrategyId == strategy.MaintenanceStrategyId && x.TriggerReason == reason && x.Status == "PENDING");

                if (alreadyPending)
                {
                    result.StrategiesSkippedAlreadyPending++;
                    continue;
                }

                _context.RcmReviewRecommendations.Add(new RcmReviewRecommendation
                {
                    EquipmentId = strategyEquipmentId,
                    MaintenanceStrategyId = strategy.MaintenanceStrategyId,
                    TriggerReason = reason,
                    Evidence = evidence,
                    RepeatFailureCount = hasRepeatFailure ? failureDates.Count : null,
                    FailureCountInWindow = failureCountInWindow,
                    TotalDowntimeHoursInWindow = downtimeInWindow,
                    WindowDays = windowDays,
                    Status = "PENDING",
                    GeneratedDate = now,
                    IsActive = true,
                    CreatedDate = now
                });

                result.RecommendationsGenerated++;
                result.GeneratedForTagNumbers.Add(strategy.Equipment?.TagNumber ?? strategy.EquipmentId.ToString());
            }
        }

        if (result.RecommendationsGenerated > 0)
            await _context.SaveChangesAsync();

        return result;
    }

    public async Task<List<RcmReviewRecommendationDto>> GetRecommendationsAsync(string? status = null)
    {
        var query = _context.RcmReviewRecommendations.AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Equipment)
            .Include(x => x.MaintenanceStrategy)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        return await query
            .OrderByDescending(x => x.GeneratedDate)
            .Select(x => new RcmReviewRecommendationDto
            {
                RcmReviewRecommendationId = x.RcmReviewRecommendationId,
                EquipmentId = x.EquipmentId,
                EquipmentTagNumber = x.Equipment!.TagNumber,
                MaintenanceStrategyId = x.MaintenanceStrategyId,
                MaintenanceStrategyType = x.MaintenanceStrategy!.StrategyType,
                FailureModeCode = x.MaintenanceStrategy.FailureModeCode,
                RCMDecisionId = x.RCMDecisionId,
                TriggerReason = x.TriggerReason,
                Evidence = x.Evidence,
                RepeatFailureCount = x.RepeatFailureCount,
                FailureCountInWindow = x.FailureCountInWindow,
                TotalDowntimeHoursInWindow = x.TotalDowntimeHoursInWindow,
                WindowDays = x.WindowDays,
                Status = x.Status,
                GeneratedDate = x.GeneratedDate,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                ReviewNotes = x.ReviewNotes
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> ReviewRecommendationAsync(int recommendationId, bool accepted, string reviewedBy, string? notes)
    {
        var recommendation = await _context.RcmReviewRecommendations.FirstOrDefaultAsync(x => x.RcmReviewRecommendationId == recommendationId);
        if (recommendation == null) return (false, "توصیه یافت نشد.");
        if (recommendation.Status != "PENDING") return (false, $"این توصیه از قبل بازبینی شده (وضعیت فعلی: {recommendation.Status}).");

        recommendation.Status = accepted ? "ACCEPTED" : "REJECTED";
        recommendation.ReviewedBy = reviewedBy;
        recommendation.ReviewedAt = DateTime.Now;
        recommendation.ReviewNotes = notes;

        await _context.SaveChangesAsync();
        return (true, null);
    }
}
