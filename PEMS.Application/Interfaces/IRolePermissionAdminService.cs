using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ویرایش مجوزها از دید یک کاربر (Admin/Users/Details &gt; Access &amp; Authorization &gt; Effective).
/// مجوز مؤثر یک کاربر از نقش‌هایش می‌آید و ذخیره‌ی «به‌ازای کاربر» وجود ندارد؛ بنابراین هر تغییر
/// روی Grant همان نقش اعمال می‌شود و همه‌ی کاربران آن نقش را تحت‌تأثیر قرار می‌دهد.
/// </summary>
public interface IRolePermissionAdminService
{
    /// <summary>
    /// نام مجوزهایی که از این صفحه قابل ویرایش نیستند، همراه با دلیل. مجوزی که در نتیجه نیست قابل‌ویرایش است.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetNonEditableAsync(IEnumerable<string> userRoles);

    /// <summary>
    /// تغییرها را اتمیک اعمال می‌کند. Enable روی <paramref name="targetRole"/> (یکی از نقش‌های کاربر) و
    /// Disable روی همه‌ی نقش‌های کاربر که آن مجوز را می‌دهند.
    /// </summary>
    Task<RolePermissionChangeResult> ApplyAsync(
        IReadOnlyCollection<string> userRoles,
        string? targetRole,
        IEnumerable<PermissionToggleRequest> changes,
        string actor);
}
