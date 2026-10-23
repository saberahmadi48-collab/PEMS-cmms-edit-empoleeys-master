using System.Text;
using PEMS.Application.Common;

namespace PEMS.Persistence.DataMigrations;

/// <summary>
/// ==============================================================================
/// مهاجرت داده‌ی RCM (Idempotent و بدون از دست رفتن داده) - منبع واحد برای:
///   ۱) Migration «RcmClassLevelStrategy» (اجرای خودکار با dotnet ef database update)،
///   ۲) اسکریپت Dry-Run (scripts/rcm/Invoke-RcmDataMigrationDryRun.ps1) که بدون تغییر،
///      تعداد ردیف‌های متاثر هر مرحله را گزارش می‌کند.
///
/// مراحل:
///   S1 FamilyId یکتا برای هر ردیف قدیمی (ستون جدید با مقدار پیش‌فرض Guid.Empty ساخته می‌شود)
///   S2 تبدیل برچسب‌های فارسی/انگلیسی ذخیره‌شده به کد پایدار (StrategyType/Consequence/Frequency)
///   S3 EquipmentClassId الگو از کلاس تجهیز قدیمیِ همان استراتژی
///   S4 FailureModeId از روی FailureModeCode قدیمی (Code یا Name کاتالوگ ISO 14224)؛ نامنطبق = Null
///   S5 Status: فعال = Active؛ غیرفعال = Retired؛ ردیف‌های ناقض قاعده‌ی ایمنی JA1011 = Draft (نیازمند بازبینی)
///   S6 هر استراتژی تک‌تگی → یک Assignment (شناسه‌ی استراتژی و Planهای مرجع دست‌نخورده)
///   S7 اتصال Plan های موجود به Assignment متناظر (فقط وقتی تجهیز Plan با تجهیز استراتژی یکی است)
///   S8 ثبت رویداد «Created (Migrated)» در تاریخچه
/// ==============================================================================
/// </summary>
public static class RcmDataMigrationSql
{
    private const string Norm =
        "LOWER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM({0})),N' ',N''),N'-',N''),N'_',N''),NCHAR(8204),N''))";

    private static string CaseFor(string column, IReadOnlyDictionary<string, string> map)
    {
        var sb = new StringBuilder();
        sb.Append("CASE ").Append(string.Format(Norm, column));
        foreach (var kv in map)
            sb.Append(" WHEN N'").Append(kv.Key.Replace("'", "''")).Append("' THEN N'").Append(kv.Value).Append('\'');
        sb.Append(" ELSE ").Append(column).Append(" END");
        return sb.ToString();
    }

    private const string ViolatesSafetyRule =
        "(s.StrategyType IN (N'RunToFailure', N'Corrective') AND (s.ConsequenceCategory IN (N'Safety', N'Environmental', N'Hidden') OR s.IsHiddenFailure = 1))";

    public static IReadOnlyList<string> UpStatements { get; } = new[]
    {
        // S1
        "UPDATE MaintenanceStrategies SET FamilyId = NEWID() WHERE FamilyId = '00000000-0000-0000-0000-000000000000'",

        // S2
        "UPDATE MaintenanceStrategies SET StrategyType = " + CaseFor("StrategyType", LegacyMaps.StrategyTypeMap) +
            " WHERE StrategyType IS NOT NULL",
        "UPDATE MaintenanceStrategies SET ConsequenceCategory = " + CaseFor("ConsequenceCategory", LegacyMaps.ConsequenceMap) +
            " WHERE ConsequenceCategory IS NOT NULL",
        "UPDATE MaintenanceStrategies SET Frequency = " + CaseFor("Frequency", LegacyMaps.FrequencyMap) +
            " WHERE Frequency IS NOT NULL",
        "UPDATE MaintenanceStrategies SET IsHiddenFailure = 1 WHERE ConsequenceCategory = N'Hidden' AND IsHiddenFailure = 0",
        // الگوی تناوب پیش‌فرض (روز) از Frequency کد پایدار
        "UPDATE MaintenanceStrategies SET DefaultIntervalDays = CASE Frequency WHEN N'Daily' THEN 1 WHEN N'Weekly' THEN 7 " +
            "WHEN N'Monthly' THEN 30 WHEN N'Quarterly' THEN 91 WHEN N'SemiAnnual' THEN 182 WHEN N'Yearly' THEN 365 END " +
            "WHERE DefaultIntervalDays IS NULL AND Frequency IN (N'Daily', N'Weekly', N'Monthly', N'Quarterly', N'SemiAnnual', N'Yearly')",

        // S3
        "UPDATE s SET s.EquipmentClassId = e.EquipmentClassId FROM MaintenanceStrategies s " +
            "JOIN Equipments e ON e.EquipmentId = s.EquipmentId WHERE s.EquipmentClassId IS NULL AND e.EquipmentClassId IS NOT NULL",

        // S4
        "UPDATE s SET s.FailureModeId = fm.FailureModeId, s.FailureModeCode = fm.Code FROM MaintenanceStrategies s " +
            "CROSS APPLY (SELECT TOP 1 m.FailureModeId, m.Code FROM FailureModes m " +
            "WHERE m.IsActive = 1 AND (m.Code = s.FailureModeCode OR LOWER(m.Name) = LOWER(s.FailureModeCode)) " +
            "ORDER BY CASE WHEN m.EquipmentClassId = s.EquipmentClassId THEN 0 WHEN m.EquipmentClassId IS NULL THEN 1 ELSE 2 END) fm " +
            "WHERE s.FailureModeId IS NULL AND s.FailureModeCode IS NOT NULL",

        // S5 - ابتدا فعال/غیرفعال، سپس ردیف‌های ناقض قاعده‌ی ایمنی به Draft (فقط ردیفی که هنوز Draft پیش‌فرض ستون است)
        "UPDATE s SET s.Status = CASE WHEN s.IsActive = 1 THEN N'Active' ELSE N'Retired' END, " +
            "s.RetiredAt = CASE WHEN s.IsActive = 0 THEN COALESCE(s.RetiredAt, GETDATE()) ELSE s.RetiredAt END " +
            "FROM MaintenanceStrategies s WHERE s.Status = N'Draft' AND s.EquipmentId IS NOT NULL AND s.SupersedesStrategyId IS NULL " +
            "AND NOT EXISTS (SELECT 1 FROM MaintenanceStrategyHistories h WHERE h.MaintenanceStrategyId = s.MaintenanceStrategyId)",
        "UPDATE s SET s.Status = N'Draft' FROM MaintenanceStrategies s WHERE s.Status = N'Active' AND s.IsActive = 1 AND " + ViolatesSafetyRule,
        // ردیف‌های فعالِ بدون حالت خرابی کاتالوگ/توجیه کافی: فعال می‌مانند ولی فوراً در صف بازنگری قرار می‌گیرند
        "UPDATE s SET s.ReviewDate = COALESCE(s.ReviewDate, GETDATE()) FROM MaintenanceStrategies s " +
            "WHERE s.Status = N'Active' AND s.EquipmentId IS NOT NULL AND (s.FailureModeId IS NULL OR LEN(LTRIM(RTRIM(COALESCE(s.Justification, N'')))) < 20)",

        // S6
        "INSERT INTO MaintenanceStrategyAssignments (MaintenanceStrategyId, EquipmentId, Status, AssignedDate, OverrideStatus, IsActive, CreatedDate, CreatedBy) " +
            "SELECT s.MaintenanceStrategyId, s.EquipmentId, CASE WHEN s.IsActive = 1 THEN N'Active' ELSE N'Retired' END, s.CreatedDate, N'None', s.IsActive, s.CreatedDate, N'RcmMigration' " +
            "FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL " +
            "AND NOT EXISTS (SELECT 1 FROM MaintenanceStrategyAssignments a WHERE a.MaintenanceStrategyId = s.MaintenanceStrategyId AND a.EquipmentId = s.EquipmentId)",

        // S7
        "UPDATE p SET p.MaintenanceStrategyAssignmentId = a.MaintenanceStrategyAssignmentId FROM MaintenancePlans p " +
            "JOIN MaintenanceStrategyAssignments a ON a.MaintenanceStrategyId = p.MaintenanceStrategyId AND a.EquipmentId = p.EquipmentId " +
            "WHERE p.MaintenanceStrategyAssignmentId IS NULL",

        // S8
        "INSERT INTO MaintenanceStrategyHistories (MaintenanceStrategyId, MaintenanceStrategyAssignmentId, VersionNumber, Action, FromStatus, ToStatus, Comment, ActorName, Timestamp) " +
            "SELECT s.MaintenanceStrategyId, NULL, s.VersionNumber, N'Created', NULL, s.Status, N'Migrated from tag-level strategy', N'RcmMigration', GETDATE() " +
            "FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL " +
            "AND NOT EXISTS (SELECT 1 FROM MaintenanceStrategyHistories h WHERE h.MaintenanceStrategyId = s.MaintenanceStrategyId)"
    };

    /// <summary>
    /// گزارش فقط‌خواندنی «اگر مهاجرت اجرا شود چه می‌شود» (بدون هیچ تغییری). فقط به ستون‌های قدیمی تکیه دارد تا
    /// پیش از اعمال Migration (روی Schema قبلی) هم قابل اجرا باشد. نسخه‌ی فایلی: scripts/rcm/rcm-dry-run-report.sql
    /// (تست یکسان‌بودن آن با این ثابت دارد).
    /// </summary>
    public const string DryRunReportSql = @"
SET NOCOUNT ON;
SELECT 'legacy strategies (tag-level)' AS Item, COUNT(*) AS N FROM MaintenanceStrategies WHERE EquipmentId IS NOT NULL
UNION ALL SELECT 'active strategies', COUNT(*) FROM MaintenanceStrategies WHERE IsActive = 1
UNION ALL SELECT 'StrategyType values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE StrategyType NOT IN (N'Preventive',N'Predictive',N'CBM',N'Corrective',N'RunToFailure',N'FailureFinding',N'Redesign')
UNION ALL SELECT 'ConsequenceCategory values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE ConsequenceCategory NOT IN (N'Hidden',N'Safety',N'Environmental',N'Operational',N'NonOperational')
UNION ALL SELECT 'Frequency values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE Frequency IS NOT NULL AND Frequency NOT IN (N'Daily',N'Weekly',N'Monthly',N'Quarterly',N'SemiAnnual',N'Yearly')
UNION ALL SELECT 'strategies with no class on equipment (stay class-less)', COUNT(*) FROM MaintenanceStrategies s JOIN Equipments e ON e.EquipmentId = s.EquipmentId WHERE e.EquipmentClassId IS NULL
UNION ALL SELECT 'strategies whose FailureModeCode matches the ISO catalog', COUNT(*) FROM MaintenanceStrategies s WHERE EXISTS (SELECT 1 FROM FailureModes m WHERE m.IsActive = 1 AND (m.Code = s.FailureModeCode OR LOWER(m.Name) = LOWER(s.FailureModeCode)))
UNION ALL SELECT 'strategies with NO catalog failure mode (need remediation)', COUNT(*) FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM FailureModes m WHERE m.IsActive = 1 AND (m.Code = s.FailureModeCode OR LOWER(m.Name) = LOWER(s.FailureModeCode)))
UNION ALL SELECT 'assignments to be created', COUNT(*) FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL
UNION ALL SELECT 'plans to be linked to an assignment', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.EquipmentId = p.EquipmentId
UNION ALL SELECT 'plans NOT linkable (plan equipment <> strategy equipment)', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.EquipmentId IS NOT NULL AND s.EquipmentId <> p.EquipmentId
UNION ALL SELECT 'reactive strategies on safety/env/hidden consequence (-> Draft)', COUNT(*) FROM MaintenanceStrategies s WHERE s.IsActive = 1 AND s.StrategyType IN (N'RunToFailure',N'Corrective',N'rtf',N'cm') AND s.ConsequenceCategory IN (N'Safety',N'Environmental',N'Hidden')
UNION ALL SELECT 'plans on RunToFailure/Corrective strategies (will stop generating WOs)', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.StrategyType IN (N'RunToFailure',N'Corrective');
";
}
