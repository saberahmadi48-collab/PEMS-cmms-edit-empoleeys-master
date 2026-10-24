namespace PEMS.Application.Common;

/// <summary>نتیجه‌ی ارزیابی ریسک/SLA یک Work Request.</summary>
public sealed record WorkRequestRiskResult(int? Score, string? RiskLevel, string Priority, string Rationale, DateTime ResponseDue, DateTime ResolutionDue);

/// <summary>
/// A4: ماتریس ریسک ۵×۵، اولویت مشتق‌شده و اهداف SLA. خالص و بدون وابستگی (قابل تست پارامتریک).
/// امتیاز = احتمال × شدت (۱..۲۵): ≥۱۵ Critical، ≥۱۰ High، ≥۵ Medium، وگرنه Low.
/// اولویت نهایی = max(اعلامی، ریسک)، HSE → حداقل High، Emergency → Critical. SLA (ساعت پاسخ/رفع): Critical 1/8، High 4/24، Medium 24/120، Low 72/336.
/// </summary>
public static class WorkRequestRiskAssessor
{
    public static readonly string[] Priorities = { "Low", "Medium", "High", "Critical" };

    private static readonly Dictionary<string, (int Response, int Resolution)> Sla = new(StringComparer.Ordinal)
    {
        ["Critical"] = (1, 8), ["High"] = (4, 24), ["Medium"] = (24, 120), ["Low"] = (72, 336)
    };

    public static (int ResponseHours, int ResolutionHours) SlaHours(string priority)
        => Sla.TryGetValue(priority, out var s) ? s : Sla["Medium"];

    public static string NormalizePriority(string? priority)
        => Priorities.FirstOrDefault(p => string.Equals(p, priority?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Medium";

    public static string LevelForScore(int score) => score >= 15 ? "Critical" : score >= 10 ? "High" : score >= 5 ? "Medium" : "Low";

    /// <summary>احتمال/شدت خارج از ۱..۵ نامعتبر است (null = ارزیابی نشده).</summary>
    public static bool IsValidRating(int? value) => !value.HasValue || (value.Value >= 1 && value.Value <= 5);

    public static WorkRequestRiskResult Assess(int? likelihood, int? severity, bool isHse, bool isEmergency, string? requestedPriority, DateTime now)
    {
        var requested = NormalizePriority(requestedPriority);
        var final = requested;
        var notes = new List<string> { $"requested={requested}" };

        int? score = null; string? level = null;
        if (likelihood.HasValue && severity.HasValue)
        {
            score = likelihood.Value * severity.Value;
            level = LevelForScore(score.Value);
            notes.Add($"risk {likelihood}x{severity}={score} → {level}");
            final = Max(final, level);
        }
        if (isHse) { final = Max(final, "High"); notes.Add("HSE → min High"); }
        if (isEmergency) { final = "Critical"; notes.Add("Emergency → Critical"); }

        var (response, resolution) = SlaHours(final);
        return new WorkRequestRiskResult(score, level, final, string.Join("; ", notes) + $"; final={final}", now.AddHours(response), now.AddHours(resolution));
    }

    private static string Max(string a, string b) => Array.IndexOf(Priorities, a) >= Array.IndexOf(Priorities, b) ? a : b;
}
