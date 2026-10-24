namespace PEMS.Application.DTOs;

/// <summary>پارامترهای داشبورد مدیریتی: فیلتر پروژه و حوزه‌ی دسترسی کاربر (که هر بخش را فقط با مجوز همان ماژول پر می‌کند).</summary>
public sealed class ExecutiveDashboardRequest
{
    public int? ProjectId { get; set; }

    // کاربر بدون ViewAllDisciplines فقط مدارک دیسیپلین خودش (و مدارک بدون دیسیپلین) را می‌بیند - همان قاعده‌ی لیست مدارک مهندسی
    public bool RestrictToOwnDiscipline { get; set; }
    public int? OwnDisciplineId { get; set; }

    public bool CanViewEngineering { get; set; } = true;
    public bool CanViewVdr { get; set; } = true;
    public bool CanViewProcurement { get; set; } = true;
    public bool CanViewCmms { get; set; } = true;
    public bool CanViewCorrespondence { get; set; } = true;
}

public sealed class DashboardCountDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class DashboardProjectOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int Documents { get; set; }
}

/// <summary>یک گروه وضعیت مدیریتی (Approved / InReview / Returned / Draft / Other) - کلید پایدار؛ برچسب را رابط کاربری ترجمه می‌کند.</summary>
public sealed class DashboardBucketDto
{
    public string Key { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class DashboardDisciplineRowDto
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Approved { get; set; }
    public int InReview { get; set; }
    public int Returned { get; set; }
    public int Draft { get; set; }
    public int ApprovedPercent { get; set; }
}

public sealed class DashboardProjectRowDto
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Approved { get; set; }
    public int ApprovedPercent { get; set; }

    // فقط برای ردیف «سایر پروژه‌ها» (ProjectId = 0): تعداد پروژه‌های ادغام‌شده
    public int OthersCount { get; set; }
}

public sealed class DashboardMonthPointDto
{
    // اولین روز ماه (ISO)
    public DateTime Date { get; set; }
    public int Issued { get; set; }
    public int Cumulative { get; set; }
}

public sealed class DashboardRevisionPressureDto
{
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Revisions { get; set; }
}

public sealed class DashboardCompletenessDto
{
    // هر مورد: چه درصدی از مدارک این فیلد را دارند؛ پایین بودن یعنی شاخص وابسته (مثلاً انحراف زمان‌بندی) قابل‌اتکا نیست
    public int WithDisciplinePercent { get; set; }
    public int WithPlannedDatePercent { get; set; }
    public int WithClientResponsePercent { get; set; }
    public int WithIssuedRevisionPercent { get; set; }
}

public sealed class DashboardEngineeringDto
{
    public int Total { get; set; }
    public int ProjectCount { get; set; }
    public int Approved { get; set; }
    public int InReview { get; set; }
    public int Returned { get; set; }
    public int Draft { get; set; }
    public int Other { get; set; }
    public int ApprovedPercent { get; set; }

    // رد واقعی و بازگشته با کامنت کارفرما (CO/AC) جدا شمرده می‌شوند (تعریف DashboardDefinitions)
    public int RejectedTrue { get; set; }
    public int ReturnedWithComments { get; set; }
    public int Overdue { get; set; }

    public int TotalRevisions { get; set; }
    public double RevisionsPerDocument { get; set; }
    public int IssuedRevisions { get; set; }

    public List<DashboardBucketDto> Buckets { get; set; } = new();
    public List<DashboardDisciplineRowDto> Disciplines { get; set; } = new();
    public List<DashboardMonthPointDto> Issuance { get; set; } = new();
    public List<DashboardCountDto> ClientResponses { get; set; } = new();
    public List<DashboardProjectRowDto> Projects { get; set; } = new();
    public List<DashboardRevisionPressureDto> RevisionPressure { get; set; } = new();
    public DashboardCompletenessDto Completeness { get; set; } = new();
}

public sealed class DashboardVdrDto
{
    public int Total { get; set; }
    public int Closed { get; set; }
    public int UnderReview { get; set; }
    public int PendingResubmission { get; set; }
    public int Overdue { get; set; }
    public int Submissions { get; set; }
    public List<DashboardCountDto> ByWorkflow { get; set; } = new();
    public List<DashboardCountDto> ByVendor { get; set; } = new();
}

public sealed class DashboardProcurementDto
{
    public int Packages { get; set; }
    public int Tenders { get; set; }
    public int Bids { get; set; }
    public int Contracts { get; set; }
    public int PurchaseOrders { get; set; }
    public int NtpsIssued { get; set; }
    public int MaterialRequisitions { get; set; }
    public int OpenMaterialRequisitions { get; set; }
    public List<DashboardCountDto> MrByStatus { get; set; } = new();
    public List<DashboardCurrencyValueDto> PurchaseOrderValue { get; set; } = new();
}

public sealed class DashboardCurrencyValueDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public int Count { get; set; }
}

public sealed class DashboardTransmittalDto
{
    public int Total { get; set; }
    public int Sent { get; set; }
    public List<DashboardMonthPointDto> ByMonth { get; set; } = new();
}

public sealed class DashboardOperationsDto
{
    public int TotalWorkOrders { get; set; }
    public int OpenWorkOrders { get; set; }
    public int CriticalWorkOrders { get; set; }
    public double? AvgCompletionDays { get; set; }
    public int Equipment { get; set; }
    public List<DashboardCountDto> ByPriority { get; set; } = new();
}

public sealed class DashboardCorrespondenceDto
{
    public int Total { get; set; }
    public int Incoming { get; set; }
    public int Outgoing { get; set; }
    public int OverdueReferrals { get; set; }
    public int OverdueResponses { get; set; }
}

/// <summary>یک مورد «نیازمند توجه مدیریت» با شدت و پیوند به صفحه‌ی مربوط؛ فقط وقتی شمارش > 0 باشد ساخته می‌شود.</summary>
public sealed class DashboardAttentionDto
{
    public string Key { get; set; } = string.Empty;

    // High / Medium / Info
    public string Severity { get; set; } = "Medium";
    public int Count { get; set; }
    public string Module { get; set; } = string.Empty;
}

public sealed class ExecutiveDashboardDto
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int? ProjectId { get; set; }
    public List<DashboardProjectOptionDto> Projects { get; set; } = new();

    public DashboardEngineeringDto? Engineering { get; set; }
    public DashboardVdrDto? Vdr { get; set; }
    public DashboardProcurementDto? Procurement { get; set; }
    public DashboardTransmittalDto? Transmittals { get; set; }
    public DashboardOperationsDto? Operations { get; set; }
    public DashboardCorrespondenceDto? Correspondence { get; set; }

    public List<DashboardAttentionDto> Attention { get; set; } = new();

    // بخش‌هایی که خواندنشان شکست خورد (مثلاً جدول هنوز Migrate نشده)؛ داشبورد بقیه را نشان می‌دهد
    public List<string> FailedSections { get; set; } = new();
}
