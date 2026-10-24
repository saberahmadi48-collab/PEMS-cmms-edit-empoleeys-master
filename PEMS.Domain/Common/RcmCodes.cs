namespace PEMS.Domain.Common;

/// <summary>
/// کدهای پایدار و مستقل از زبان برای ماژول استراتژی نگهداری (RCM - SAE JA1011 / EN 13306).
/// هیچ‌یک از مقادیر زیر برچسب نمایشی نیستند: در دیتابیس فقط همین کدها ذخیره می‌شوند و
/// ترجمه‌ی نمایشی در لحظه‌ی نمایش از فایل‌های Resource انجام می‌شود (کلید: Rcm_&lt;Group&gt;_&lt;Code&gt;).
/// </summary>
public static class RcmCodes
{
    public static class StrategyType
    {
        public const string Preventive = "Preventive";       // Scheduled restoration / discard (calendar or meter)
        public const string Predictive = "Predictive";       // Statistical / trend based inspection
        public const string Cbm = "CBM";                      // Scheduled on-condition task
        public const string Corrective = "Corrective";       // Repair after failure (planned)
        public const string RunToFailure = "RunToFailure";   // No scheduled task (JA1011 "no scheduled maintenance")
        public const string FailureFinding = "FailureFinding"; // Scheduled failure-finding for hidden failures
        public const string Redesign = "Redesign";           // Redesign / modification recommendation

        public static readonly string[] All = { Preventive, Predictive, Cbm, Corrective, RunToFailure, FailureFinding, Redesign };

        /// <summary>انواعی که یک کار زمان‌بندی‌شده (Task) دارند و می‌توانند MaintenancePlan تولید کنند.</summary>
        public static readonly string[] PlanGenerating = { Preventive, Predictive, Cbm, FailureFinding };

        /// <summary>انواعی که بر پایه‌ی وضعیت (نه کارکرد/تقویم خام) تعریف می‌شوند.</summary>
        public static readonly string[] ConditionBased = { Cbm, Predictive };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>طبقه‌بندی پیامد طبق JA1011: پنهان، ایمنی، زیست‌محیطی، عملیاتی، غیرعملیاتی.</summary>
    public static class Consequence
    {
        public const string Hidden = "Hidden";
        public const string Safety = "Safety";
        public const string Environmental = "Environmental";
        public const string Operational = "Operational";
        public const string NonOperational = "NonOperational";

        public static readonly string[] All = { Hidden, Safety, Environmental, Operational, NonOperational };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    public static class Frequency
    {
        public const string Daily = "Daily";
        public const string Weekly = "Weekly";
        public const string Monthly = "Monthly";
        public const string Quarterly = "Quarterly";
        public const string SemiAnnual = "SemiAnnual";
        public const string Yearly = "Yearly";

        public static readonly string[] All = { Daily, Weekly, Monthly, Quarterly, SemiAnnual, Yearly };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);

        public static int? ToDays(string? code) => code switch
        {
            Daily => 1,
            Weekly => 7,
            Monthly => 30,
            Quarterly => 91,
            SemiAnnual => 182,
            Yearly => 365,
            _ => null
        };
    }

    public static class Trigger
    {
        public const string Calendar = "Calendar";
        public const string Meter = "Meter";
        public const string Both = "Both";

        public static readonly string[] All = { Calendar, Meter, Both };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    public static class MeterType
    {
        public const string RunningHours = "RunningHours";
        public const string Cycles = "Cycles";
        public const string ProductionUnits = "ProductionUnits";

        public static readonly string[] All = { RunningHours, Cycles, ProductionUnits };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    public static class TaskType
    {
        public const string Inspection = "Inspection";
        public const string Lubrication = "Lubrication";
        public const string Calibration = "Calibration";
        public const string FunctionTest = "FunctionTest";
        public const string Restoration = "Restoration";
        public const string Replacement = "Replacement";
        public const string ConditionMonitoring = "ConditionMonitoring";
        public const string Overhaul = "Overhaul";

        public static readonly string[] All =
            { Inspection, Lubrication, Calibration, FunctionTest, Restoration, Replacement, ConditionMonitoring, Overhaul };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    public static class Source
    {
        public const string Oem = "OEM";
        public const string RcmStudy = "RcmStudy";
        public const string History = "History";
        public const string Standard = "Standard";
        public const string Regulation = "Regulation";

        public static readonly string[] All = { Oem, RcmStudy, History, Standard, Regulation };

        public static bool IsValid(string? code) => code != null && All.Contains(code, StringComparer.Ordinal);
    }

    /// <summary>چرخه‌عمر استراتژی: Draft → InReview → Approved → Active → Superseded / Retired.</summary>
    public static class Status
    {
        public const string Draft = "Draft";
        public const string InReview = "InReview";
        public const string Approved = "Approved";
        public const string Active = "Active";
        public const string Superseded = "Superseded";
        public const string Retired = "Retired";

        public static readonly string[] All = { Draft, InReview, Approved, Active, Superseded, Retired };

        /// <summary>فقط این وضعیت‌ها اجازه‌ی تخصیص به تگ و ساخت Plan را می‌دهند.</summary>
        public static readonly string[] Usable = { Approved, Active };

        /// <summary>وضعیت‌هایی که هنوز «ردیف زنده» محسوب می‌شوند (برای تشخیص تکراری).</summary>
        public static readonly string[] Live = { Draft, InReview, Approved, Active };

        public static bool IsEditable(string? status) => status is Draft;
    }

    public static class AssignmentStatus
    {
        public const string Active = "Active";
        public const string Suspended = "Suspended";
        public const string Retired = "Retired";
    }

    public static class OverrideStatus
    {
        public const string None = "None";
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public static class ScheduleMode
    {
        /// <summary>سررسید بعدی روی سررسید قبلی لنگر می‌شود (زمان‌بندی ثابت) و بدون جبران عقب‌افتادگی جلو می‌رود.</summary>
        public const string Fixed = "Fixed";

        /// <summary>سررسید بعدی از تاریخ تکمیل واقعی Work Order محاسبه می‌شود.</summary>
        public const string Floating = "Floating";

        public static bool IsValid(string? code) => code is Fixed or Floating;
    }

    public static class HistoryAction
    {
        public const string Created = "Created";
        public const string Edited = "Edited";
        public const string Submitted = "Submitted";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Activated = "Activated";
        public const string NewVersion = "NewVersion";
        public const string Superseded = "Superseded";
        public const string Retired = "Retired";
        public const string Deactivated = "Deactivated";
        public const string Assigned = "Assigned";
        public const string Unassigned = "Unassigned";
        public const string OverrideRequested = "OverrideRequested";
        public const string OverrideApproved = "OverrideApproved";
        public const string OverrideRejected = "OverrideRejected";
    }

    /// <summary>کلاس بحرانی تجهیز (EquipmentCriticality.CriticalityClass): A = بحرانی‌ترین.</summary>
    public static class Criticality
    {
        public const string A = "A";
        public const string B = "B";
        public const string C = "C";
        public const string Unrated = "Unrated";
    }

    public static class Priority
    {
        public const string Low = "Low";
        public const string Medium = "Medium";
        public const string High = "High";
        public const string Critical = "Critical";
    }
}
