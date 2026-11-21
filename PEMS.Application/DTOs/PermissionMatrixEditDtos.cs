namespace PEMS.Application.DTOs;

/// <summary>
/// تغییر یک خانه‌ی Permission Matrix (نقش × ماژول × عمل). Action یکی از ۱۰ عمل ثابت ماتریس است
/// (View, Create, Edit, Delete, Approve, ManualNumber, Freeze, Unfreeze, Hold, ReleaseHold).
/// </summary>
public class PermissionCellChange
{
    public string ModuleName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

/// <summary>وضعیت نهایی یک ردیف ماژول بعد از ذخیره: کلید = نام عمل، مقدار = فعال بودن.</summary>
public class PermissionMatrixModuleState
{
    public string ModuleName { get; set; } = string.Empty;
    public Dictionary<string, bool> Flags { get; set; } = new();
}

public class PermissionMatrixEditResult
{
    public bool Success { get; set; }

    /// <summary>کد خطا وقتی Success=false (UnknownRole, SystemAdminExcluded, InvalidCell, UnknownModule).</summary>
    public string? Error { get; set; }

    public List<PermissionMatrixModuleState> Modules { get; set; } = new();

    /// <summary>تعداد ماژول‌هایی که این نقش حداقل یک مجوز در آن‌ها دارد (برای نشان‌گر «x/y modules»).</summary>
    public int ConfiguredModules { get; set; }
}
