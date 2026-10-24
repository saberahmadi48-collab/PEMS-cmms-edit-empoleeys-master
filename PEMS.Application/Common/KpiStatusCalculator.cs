using PEMS.Application.DTOs;

namespace PEMS.Application.Common;

/// <summary>وضعیت یک شاخص نسبت به هدفش. بدون مقدار هرگز «محقق/نشده» گزارش نمی‌شود (NoData).</summary>
public static class KpiStatusCalculator
{
    public static string Evaluate(string direction, decimal? value, decimal? target, decimal? warn)
    {
        if (value == null) return KpiStatuses.NoData;
        if (target == null) return KpiStatuses.NoTarget;

        if (direction == KpiDirections.LowerIsBetter)
        {
            if (value <= target) return KpiStatuses.Met;
            return warn != null && value <= warn ? KpiStatuses.Near : KpiStatuses.Missed;
        }

        if (value >= target) return KpiStatuses.Met;
        return warn != null && value >= warn ? KpiStatuses.Near : KpiStatuses.Missed;
    }
}
