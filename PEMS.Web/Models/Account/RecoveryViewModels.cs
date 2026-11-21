using System.ComponentModel.DataAnnotations;
using PEMS.Application.DTOs;

namespace PEMS.Web.Models.Account;

/// <summary>SCRUM-355: فرم بازیابی حساب مدیر با کد یکبارمصرف (بدون ورود).</summary>
public class RecoveryCodeRedeemViewModel
{
    [Required, StringLength(256)]
    public string UserName { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>SCRUM-355: صفحه‌ی مدیریت کدهای بازیابی در پروفایل مدیر.</summary>
public class RecoveryCodesViewModel
{
    public int Remaining { get; set; }

    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;
}

/// <summary>SCRUM-356: صفحه‌ی break-glass روی سرور.</summary>
public class BreakGlassViewModel
{
    public BreakGlassWindow Window { get; set; } = new();
    public IReadOnlyList<BreakGlassAdminDto> Admins { get; set; } = Array.Empty<BreakGlassAdminDto>();
    public int? TargetUserId { get; set; }
    public string? VendorToken { get; set; }
    public string? Error { get; set; }
}
