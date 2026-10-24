using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ذخیره‌ی خودکار و ریزدانه‌ی Permission Matrix (یک یا چند خانه در یک درخواست). جدا از IPermissionService
/// است تا پیاده‌سازی‌ها/Fakeهای موجود آن اینترفیس تغییر نکنند؛ همان منبع (RolePermission) را می‌نویسد
/// و Grantهای همان ماژول را هم‌گام می‌کند، دقیقاً مثل SetPermissionsAsync ولی فقط برای ماژول‌های درگیر.
/// </summary>
public interface IPermissionMatrixEditor
{
    /// <param name="allowedModules">ماژول‌های قابل ویرایش در UI؛ هر ماژول دیگری رد می‌شود.</param>
    Task<PermissionMatrixEditResult> ApplyAsync(
        string roleName,
        IReadOnlyCollection<PermissionCellChange> cells,
        IReadOnlyCollection<string> allowedModules,
        string actor);
}
