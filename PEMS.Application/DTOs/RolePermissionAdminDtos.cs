namespace PEMS.Application.DTOs;

/// <summary>
/// درخواست فعال/غیرفعال‌سازی یک مجوز «Resource.Action» از صفحه‌ی Admin/Users/Details (تب Effective).
/// </summary>
public class PermissionToggleRequest
{
    public string PermissionName { get; set; } = string.Empty;
    public bool Enable { get; set; }
}

/// <summary>کدهای دلیل برای مجوزهای غیرقابل‌ویرایش یا تغییرهای اعمال‌نشده (UI آن‌ها را ترجمه می‌کند).</summary>
public static class PermissionChangeReasons
{
    public const string SystemAdmin = "SystemAdmin";
    public const string NoRoles = "NoRoles";
    public const string OutsideMatrix = "OutsideMatrix";
    public const string RoleModuleAction = "RoleModuleAction";
    public const string Unknown = "Unknown";
    public const string NoChange = "NoChange";
    public const string TargetRoleRequired = "TargetRoleRequired";
}

/// <summary>نتیجه‌ی یک ردیف درخواست: Applied=false یعنی تغییری ثبت نشد و Reason دلیل آن است.</summary>
public class PermissionToggleResult
{
    public string PermissionName { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public string? Reason { get; set; }
}

public class RolePermissionChangeResult
{
    public List<PermissionToggleResult> Items { get; set; } = new();

    /// <summary>نقش‌هایی که Grant آن‌ها واقعاً تغییر کرد (روی همه‌ی کاربران همان نقش اثر می‌گذارد).</summary>
    public List<string> AffectedRoles { get; set; } = new();

    public int AppliedCount => Items.Count(i => i.Applied);
}
