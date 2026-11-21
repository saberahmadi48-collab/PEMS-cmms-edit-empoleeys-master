namespace PEMS.Application.Organization;

public sealed record OrgOpResult(bool Success, string? ErrorKey = null, string? Detail = null)
{
    public static OrgOpResult Ok() => new(true);
    public static OrgOpResult Fail(string key, string? detail = null) => new(false, key, detail);
}

/// <summary>درخواست تخصیص کارمند به سمت (مدیریت سازمان). هیچ شناسه‌ی مدیر/تأییدکننده‌ای در آن نیست؛ مدیر از سلسله‌مراتب سمت‌ها به‌دست می‌آید.</summary>
public sealed record OrganizationAssignmentRequest(
    int PositionId, int EmployeeId, bool IsPrimary, DateTime StartDate, DateTime? EndDate, string? Notes);

/// <summary>درخواست‌هایی که به‌دلیل ناقص‌بودن ساختار سازمانی در «Organization Resolution Required» مانده‌اند.</summary>
public sealed record OrganizationBlockedRequest(
    string Module, int RequestId, string? RequestNumber, string RequesterName, int RequesterEmployeeId, string? Reason, DateTime? SubmittedDate);

/// <summary>مدیر مستقیم یک کارمند از سلسله‌مراتب سازمانی (برای نمایش پروفایل). IssueCode ≠ null یعنی قابل‌تعیین نیست.</summary>
public sealed record DirectManagerInfo(string? PositionLabel, IReadOnlyList<(int EmployeeId, string Name)> Managers, string? IssueCode, IReadOnlyList<string> Path);

/// <summary>شمارنده‌های آمادگی داده‌ی سازمانی (فقط شمارش؛ هیچ داده‌ای ساخته یا استنباط نمی‌شود).</summary>
public sealed record OrganizationReadiness(
    int ActivePositions, int PositionsWithParent, int RootPositions, int VacantPositions, int ActivePositionsWithoutDepartment,
    int ActiveEmployees, int ActiveEmployeesWithPrimaryPosition, int ActiveEmployeesWithoutPersonnelCode, int ActiveUsersWithoutPrimaryPosition);

/// <summary>گره‌ی نمودار سازمانی (فقط داده‌ی لازم برای نمایش؛ بدون Entity، بدون داده‌ی امنیتی). گره = سمت؛ کارمند فقط دارنده‌ی سمت است.</summary>
public sealed class OrganizationChartNodeDto
{
    /// <summary>"p{PositionId}" یا "org-root" (ظرف بصریِ چند ریشه؛ سمت نیست).</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>خالی = ریشه. هرگز مدیر/والد ساختگی نیست (به‌جز ظرف بصری org-root).</summary>
    public string ParentId { get; init; } = string.Empty;
    public int? PositionId { get; init; }
    public string? PositionCode { get; init; }
    public string PositionTitle { get; init; } = string.Empty;
    public int? EmployeeId { get; init; }
    public string? EmployeeName { get; init; }
    public string? DepartmentName { get; init; }
    public string? TeamName { get; init; }
    public bool IsVacant { get; init; }
    public string Lifecycle => IsVacant ? "VACANT" : "OCCUPIED";
    public bool HolderIsPrimary { get; init; }
    public string? AssignmentFrom { get; init; }
    public string? AssignmentTo { get; init; }
    /// <summary>root = ریشه‌ی معتبر؛ position = زیرمجموعه؛ orphan = والد نامعتبر/غیرفعال؛ cycle = عضو دور؛ container = ظرف بصری.</summary>
    public string NodeKind { get; init; } = "position";
    public int ChildCount { get; init; }
    public IReadOnlyList<string> IssueCodes { get; init; } = Array.Empty<string>();
}

public sealed record OrganizationChartWarning(string Code, int Count);

public sealed class OrganizationChartDto
{
    public List<OrganizationChartNodeDto> Nodes { get; init; } = new();
    public List<OrganizationChartWarning> Warnings { get; init; } = new();
    public int RootCount { get; init; }
}

public sealed class OrganizationPositionRow
{
    public int PositionId { get; init; }
    public string? PositionCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public int? ParentPositionId { get; init; }
    public string? ParentLabel { get; init; }
    public bool IsActive { get; init; }
    public int? AssignmentId { get; init; }
    public int? HolderEmployeeId { get; init; }
    public string? HolderName { get; init; }
    public bool HolderIsPrimary { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool IsVacant => AssignmentId == null;
    /// <summary>وضعیت چرخه‌ی عمر مشتق از داده‌ی موجود: INACTIVE | VACANT | OCCUPIED (PROPOSED/ARCHIVED نیازمند ستون وضعیت است؛ فعلاً فقط در پیشنهاد/IsActive+تاریخچه).</summary>
    public string Lifecycle => !IsActive ? "INACTIVE" : IsVacant ? "VACANT" : "OCCUPIED";
    public int ChildCount { get; init; }
    public int IssueCount { get; set; }
}

/// <summary>
/// مدیریت سلسله‌مراتب سازمانی (Position.ParentPositionId) و تخصیص‌های اصلی/فرعی با تاریخ اعتبار.
/// جایگزین هیچ سرویس موجودی نیست؛ IOrganizationStructureService (Team/Position/Assignment) دست‌نخورده می‌ماند.
/// </summary>
public interface IOrganizationHierarchyService
{
    Task<List<OrganizationPositionRow>> GetPositionRowsAsync(CancellationToken ct = default);

    /// <summary>مدیر مستقیم کارمند (درخواست‌دهنده ← سمت اصلی ← مافوق ← دارنده). فقط خواندنی؛ هیچ حدسی زده نمی‌شود.</summary>
    Task<DirectManagerInfo> GetDirectManagerAsync(int employeeId, CancellationToken ct = default);

    /// <summary>داده‌ی نمودار سازمانی از همان مدل (Position → ParentPosition → EmployeeAssignment). فقط‌خواندنی؛ ساختار ساختگی نمی‌سازد.</summary>
    Task<OrganizationChartDto> GetChartAsync(CancellationToken ct = default);

    Task<OrganizationReadiness> GetReadinessAsync(CancellationToken ct = default);

    /// <summary>درخواست‌های متوقف‌شده به‌دلیل ساختار سازمانی (فعلاً Part Catalogue).</summary>
    Task<List<OrganizationBlockedRequest>> GetBlockedRequestsAsync(CancellationToken ct = default);

    /// <summary>فهرست کارمندان فعال برای انتخاب دارنده (Id، نام، کد پرسنلی).</summary>
    Task<List<(int EmployeeId, string Label)>> GetActiveEmployeesAsync(CancellationToken ct = default);

    /// <summary>سمت جدید (Master-data). Department/Parent اختیاری؛ در صورت وجود باید فعال باشند. هرگز سمت ساختگی از متن آزاد ساخته نمی‌شود.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "CreatePosition", Roles = "SystemAdmin")]
    Task<OrgOpResult> CreatePositionAsync(string title, string? positionCode, int? departmentId, string? description, int? parentPositionId, CancellationToken ct = default);

    /// <summary>ویرایش عنوان/کد/دپارتمان/توضیح. تغییر مافوق از SetPositionHierarchyAsync (اعتبارسنجی دور/خودارجاع) انجام می‌شود.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "UpdatePosition", Roles = "SystemAdmin")]
    Task<OrgOpResult> UpdatePositionAsync(int positionId, string title, string? positionCode, int? departmentId, string? description, CancellationToken ct = default);

    /// <summary>غیرفعال‌سازی فقط وقتی دارنده‌ی فعال ندارد (تخصیص بی‌صدا بسته نمی‌شود)؛ فعال‌سازی مجدد آزاد است. حذف فیزیکی وجود ندارد.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "SetPositionActive", Roles = "SystemAdmin")]
    Task<OrgOpResult> SetPositionActiveAsync(int positionId, bool isActive, CancellationToken ct = default);

    Task<List<(int DepartmentId, string Name)>> GetActiveDepartmentsAsync(CancellationToken ct = default);

    /// <summary>سمت‌های فعالِ بدون دارنده (قابل انتخاب برای تخصیص) + سمت اصلیِ فعلی هر کارمند (برای فرم‌ها).</summary>
    Task<List<(int PositionId, string Label)>> GetVacantPositionsAsync(CancellationToken ct = default);
    Task<Dictionary<int, (int PositionId, string Label)>> GetPrimaryPositionsAsync(IEnumerable<int> employeeIds, CancellationToken ct = default);

    /// <summary>تعیین سمت اصلیِ کارمند (تغییر/پایان). سمت فعلی (در صورت وجود) پایان می‌یابد و سمت جدید با همان قوانین AssignAsync تخصیص می‌یابد؛ null = فقط پایان سمت اصلی.</summary>
    Task<OrgOpResult> SetPrimaryPositionAsync(int employeeId, int? positionId, CancellationToken ct = default);

    /// <summary>تعیین کد سمت و سمت مافوق. ParentPositionId=null یعنی سمت ریشه. Self/Cycle/Inactive رد می‌شود.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "SetPositionHierarchy", Roles = "SystemAdmin")]
    Task<OrgOpResult> SetPositionHierarchyAsync(int positionId, string? positionCode, int? parentPositionId, CancellationToken ct = default);

    /// <summary>اشغال سمت. سمت باید فعال و فاقد دارنده‌ی فعال باشد؛ کارمند فعال؛ حداکثر یک تخصیص اصلیِ فعال برای هر کارمند.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "Assign", Roles = "SystemAdmin")]
    Task<OrgOpResult> AssignAsync(OrganizationAssignmentRequest request, CancellationToken ct = default);

    [PEMS.Application.Common.PemsOperation("Organization", "UpdateAssignment", Roles = "SystemAdmin")]
    Task<OrgOpResult> UpdateAssignmentAsync(int assignmentId, bool isPrimary, DateTime startDate, DateTime? endDate, CancellationToken ct = default);

    /// <summary>پایان تخصیص (سمت Vacant می‌شود؛ تاریخچه حفظ می‌شود).</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "EndAssignment", Roles = "SystemAdmin")]
    Task<OrgOpResult> EndAssignmentAsync(int assignmentId, string? notes, CancellationToken ct = default);
}
