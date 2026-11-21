using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// ==============================================================================
/// طبق تطبیق با RCM (Reliability Centered Maintenance - SAE JA1011/JA1012)، استاندارد
/// جهانی برای تصمیم‌گیری «کدام استراتژی برای کدام خرابی مناسبه»:
///
///   ۱. هر استراتژی به یک حالت خرابی مشخص (FailureModeCode - همون واژگان ISO 14224 که
///      روی WorkOrder هم استفاده کردیم) وصل می‌شه، نه فقط یک برچسب کلی روی کل تجهیز.
///   ۲. طبقه‌بندی پیامد (Safety/Environmental/Operational/Economic) طبق RCM Decision Logic
///      مشخص می‌کنه که اصلاً باید فعالانه جلوی خرابی رو گرفت یا Run-to-Failure قابل‌قبوله.
///   ۳. Justification: چرا این استراتژی انتخاب شده - بدون این، انتخاب فقط سلیقه‌ست نه RCM.
/// ==============================================================================
/// </summary>
public class MaintenanceStrategy : BaseEntity
{
    [Key]
    public int MaintenanceStrategyId { get; set; }

    // ==============================================================================
    // طبق بازطراحی RCM (کلاس‌محور): استراتژی یک «الگو» (Template) روی کلاس تجهیز است و به تگ‌ها از
    // طریق MaintenanceStrategyAssignment وصل می‌شود. EquipmentId فقط برای سازگاری با داده‌ی قدیمی
    // (استراتژی‌های تک‌تگی) نگه داشته شده و برای الگوهای جدید همیشه Null است - مرجع واقعی تخصیص
    // همان Assignment است، نه این ستون.
    // ==============================================================================
    public int? EquipmentId { get; set; }

    public Equipment? Equipment { get; set; }

    // کلاس تجهیز (ISO 14224) که این الگو برای آن تعریف شده - Null فقط برای استراتژی‌های قدیمی
    // تک‌تگی که تجهیزشان هنوز طبقه‌بندی نشده بود.
    public int? EquipmentClassId { get; set; }
    public EquipmentClass? EquipmentClass { get; set; }

    // عنوان الگو (مثلاً «Centrifugal Pump - Seal Leakage - CBM»)
    public string? Title { get; set; }

    // نوع استراتژی
    // Preventive, Predictive, Corrective, CBM, RunToFailure
    public string StrategyType { get; set; } = string.Empty;

    // ==============================================================================
    // طبق «PEMS — Enterprise Maintenance Master Data & Standards Implementation» بند ۹/۱۷:
    // ارتباط افزودنی/اختیاری به جدول مرجع MaintenanceStrategyType - StrategyType (رشته‌ی بالا)
    // برای Backward Compatibility دست‌نخورده می‌ماند؛ این فیلد فقط زیرساخت AI-Ready/کد پایدار
    // است، نه جایگزین فوری آن.
    // ==============================================================================
    public int? MaintenanceStrategyTypeId { get; set; }
    public MaintenanceStrategyType? MaintenanceStrategyTypeRef { get; set; }

    // ==============================================================================
    // طبق RCM: این استراتژی برای کدام حالت خرابی مشخص تعریف شده - همون واژگان ISO 14224
    // که روی WorkOrder.FailureModeCode هم داریم (Leakage, Fracture, Vibration, ...)
    // ==============================================================================
    public string? FailureModeCode { get; set; }

    // پیوند به کاتالوگ کنترل‌شده‌ی ISO 14224 (FailureMode) - FailureModeCode همواره Code همین ردیف است.
    public int? FailureModeId { get; set; }
    public FailureMode? FailureMode { get; set; }

    public int? FailureCauseId { get; set; }
    public FailureCause? FailureCause { get; set; }

    // طبق JA1011: شکست عملکردی، اثر خرابی، و پنهان/آشکار بودن خرابی
    public string? FunctionalFailure { get; set; }
    public string? FailureEffect { get; set; }
    public bool IsHiddenFailure { get; set; }

    // ==============================================================================
    // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۴:
    // بدون این پیوند، یک استراتژی با StrategyType="CBM" صرفاً یک برچسب متنی بود - هیچ اتصال
    // واقعی به داده‌ی پایش وضعیت نداشت. این FK اختیاری (فقط برای CBM معنا دارد؛ برای
    // Preventive/Predictive/Corrective/RunToFailure همیشه Null می‌ماند) دقیقاً همون نقطه‌ی
    // پایشی را مشخص می‌کند که این استراتژی برای پاسخ به آن تعریف شده - بدون ساخت یک Entity
    // «CM Plan» جدید (طبق بند ۲۷ سند: MaintenanceStrategy+MaintenancePlan+PMTemplate موجود کافی است).
    // ==============================================================================
    public int? ConditionMonitoringPointId { get; set; }
    public ConditionMonitoringPoint? ConditionMonitoringPoint { get; set; }

    // طبق RCM Decision Logic: Safety, Environmental, Operational, Economic - تعیین می‌کنه
    // که آیا اصلاً باید فعالانه جلوی این خرابی گرفته بشه یا Run-to-Failure قابل‌قبوله
    public string ConsequenceCategory { get; set; } = "Operational";

    // چرا این استراتژی (نه یکی دیگه) انتخاب شده - بدون این مستندسازی، RCM واقعی نیست
    public string? Justification { get; set; }

    // پیشنهاد نوع تریگر برای Planهایی که از این استراتژی ساخته می‌شن (Calendar/Meter/Both) -
    // طبق رفع ناهماهنگی قبلی بین Strategy و Plan
    public string RecommendedTriggerType { get; set; } = "Calendar";

    // تناوب انجام - طبق رفع ناهماهنگی قبلی: این‌جا فقط یک برچسب راهنماست (نه منبع رسمی
    // زمان‌بندی)؛ زمان‌بندی واقعی روی MaintenancePlan.IntervalDays/MeterInterval تعریف می‌شه
    // Daily, Weekly, Monthly, Yearly
    public string? Frequency { get; set; }

    // شرح فعالیت
    public string? Description { get; set; }

    // ==============================================================================
    // تعریف کار (Task) طبق JA1011
    // ==============================================================================
    // کد ثابت RcmCodes.TaskType
    public string? TaskType { get; set; }

    // رشته‌ی مسئول انجام کار (Discipline) - به WO تولیدشده کپی می‌شود
    public int? OwnerDisciplineId { get; set; }
    public Discipline? OwnerDiscipline { get; set; }

    public string? RequiredSkills { get; set; }

    public decimal? EstimatedDurationHours { get; set; }

    // تناوب پیشنهادی (روز) - پیش‌فرض Plan؛ از Frequency هم قابل استخراج است
    public int? DefaultIntervalDays { get; set; }

    // ==============================================================================
    // پارامترهای CBM / P-F (فقط برای CBM و Predictive معنا دارند)
    // ==============================================================================
    public string? CbmParameter { get; set; }
    public string? ThresholdUnit { get; set; }
    public decimal? AlarmThreshold { get; set; }
    public decimal? TripThreshold { get; set; }
    public int? PfIntervalDays { get; set; }
    public int? InspectionIntervalDays { get; set; }

    // منبع تصمیم (RcmCodes.Source) و تاریخ بازنگری دوره‌ای
    public string? Source { get; set; }
    public DateTime? ReviewDate { get; set; }

    // ==============================================================================
    // چرخه‌عمر و نسخه‌بندی (RcmCodes.Status): Draft → InReview → Approved → Active →
    // Superseded/Retired. نسخه‌ی تاییدشده هرگز ویرایش درجا نمی‌شود؛ تغییر = نسخه‌ی جدید.
    // ==============================================================================
    public string Status { get; set; } = RcmCodes.Status.Draft;

    public int VersionNumber { get; set; } = 1;

    // همه‌ی نسخه‌های یک استراتژی یک FamilyId مشترک دارند
    public Guid FamilyId { get; set; } = Guid.NewGuid();

    public int? SupersedesStrategyId { get; set; }

    // تفکیک وظایف: نویسنده (Employee) نمی‌تواند تاییدکننده باشد
    public int? AuthorEmployeeId { get; set; }
    public int? SubmittedByEmployeeId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int? ApprovedByEmployeeId { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RetiredAt { get; set; }

    public ICollection<MaintenanceStrategyAssignment> Assignments { get; set; } = new List<MaintenanceStrategyAssignment>();

    // تاریخ شروع
    public DateTime? StartDate { get; set; }

    // وضعیت
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}