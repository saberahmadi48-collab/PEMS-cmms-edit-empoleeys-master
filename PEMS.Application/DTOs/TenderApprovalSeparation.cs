namespace PEMS.Application.DTOs;

/// <summary>
/// P0-10 / D-OPEN-04 (تصمیم مالک ۲۰۲۶-۱۰-۱۴): ایجادکننده‌ی مناقصه نباید همان مناقصه را تأیید کند (انتخاب برنده، ساخت قرارداد
/// از برنده، تأیید مذاکره). SystemAdmin مستثناست، ولی هر استفاده از این استثنا در AuthorizationAuditLog ثبت می‌شود.
/// </summary>
public enum TenderApprovalSeparation
{
    /// <summary>کاربر ایجادکننده نیست، یا ایجادکننده‌ی مناقصه ثبت نشده (رکورد قدیمی).</summary>
    Allowed,

    /// <summary>کاربر ایجادکننده است و SystemAdmin است؛ استثنا ثبت شد.</summary>
    AllowedBySystemAdminExemption,

    /// <summary>کاربر ایجادکننده‌ی همین مناقصه است.</summary>
    DeniedCreator,

    /// <summary>هویت کاربر یا ثبت ممیزی استثنا ممکن نبود (Fail-Closed).</summary>
    DeniedUnverifiable,

    /// <summary>مناقصه/مذاکره یافت نشد؛ مسیر عادی سرویس خطای مناسب را برمی‌گرداند.</summary>
    NotFound
}
