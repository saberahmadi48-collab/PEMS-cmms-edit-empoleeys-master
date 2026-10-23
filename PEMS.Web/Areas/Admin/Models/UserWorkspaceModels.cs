using PEMS.Application.DTOs;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Admin.Models;

// طبق «PEMS — Enterprise User-Centric Authorization Workspace Consolidation»: این مدل‌ها فقط برای
// نمایش‌اند. هیچ تصمیم مجوزدهی/SoD/ناوبری‌ای این‌جا محاسبه نمی‌شود - همه از IAccessControlService
// (از طریق IUserAuthorizationSummaryService)، IPermissionService، و NavigationVisibilityResolver
// موجود خوانده می‌شود.

public class UserWorkspaceVm
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public bool LockedOut { get; set; }
    public DateTime? LastLoginDate { get; set; }

    // Organization
    public string? DepartmentName { get; set; }
    public string? DisciplineName { get; set; }
    public string? Position { get; set; }
    public bool HasLinkedEmployee { get; set; }

    // Roles
    public List<string> RoleNames { get; set; } = new();

    // Access & Authorization / Effective
    public List<EffectivePermissionDto> EffectivePermissions { get; set; } = new();
    public int AllowedCount => EffectivePermissions.Count(p => p.Allowed);

    // مجوزهای غیرقابل‌ویرایش از همین تب (نام مجوز -> کد دلیل از PermissionChangeReasons)؛ نبودن یعنی قابل‌ویرایش
    public IReadOnlyDictionary<string, string> NonEditablePermissions { get; set; } = new Dictionary<string, string>();

    // تعداد کاربران هر نقش این کاربر: تغییر مجوز روی Grant نقش اعمال می‌شود، نه فقط همین کاربر
    public Dictionary<string, int> RoleUserCounts { get; set; } = new();

    // Access & Authorization / Matrix - فقط ردیف‌های نقش‌های همین کاربر از همان ماتریس کامل موجود
    public List<RolePermissionDto> MatrixRows { get; set; } = new();

    // Access & Authorization / Simulator
    public SimulateFormVm Simulator { get; set; } = new();

    // Access & Authorization / SoD - طبق مستندسازی DTO: «قرارگرفتن در معرض»، نه «تخلف قطعی»
    public List<SoDExposureDto> SoDExposure { get; set; } = new();
    public int SoDExposureCount => SoDExposure.Count(x => x.UserHoldsBoth);

    // Access & Authorization / Navigation
    public List<NavigationUserModuleNode> NavigationTree { get; set; } = new();
    public int VisibleModuleCount => NavigationTree.Count(m => m.Visible);

    // Audit - آخرین چند ردیف؛ برای فهرست کامل با فیلتر، لینک به Admin/AuthorizationAudit?userId=
    public List<UserAuditLogRowVm> RecentAuditLogs { get; set; } = new();
}

/// <summary>یک ردیف تب Effective؛ LockedReason (کد از PermissionChangeReasons) یعنی از این صفحه قابل‌ویرایش نیست.</summary>
public class UserEffectivePermissionRowVm
{
    public EffectivePermissionDto Row { get; set; } = new();
    public string? LockedReason { get; set; }
}

/// <summary>بدنه‌ی درخواست فعال/غیرفعال‌سازی مجوزها از تب Effective (JSON).</summary>
public class SetPermissionAccessRequest
{
    public string? TargetRole { get; set; }
    public List<PermissionToggleRequest> Changes { get; set; } = new();
}

public class UserAuditLogRowVm
{
    public DateTime Timestamp { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
