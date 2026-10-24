using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// سایدبار داده‌محور. منو برای همه‌ی کاربران یکسان است (قاعده‌ی نمایش برای هر کاربر جدا در Web ارزیابی می‌شود)، پس یک کش سراسری
/// کافی است و با هر تغییر مدیر باطل می‌شود. همگام‌سازی کاتالوگ کد فقط افزایشی است و فیلدهای مدیر را بازنویسی نمی‌کند.
/// </summary>
public class NavigationMenuService : INavigationMenuService
{
    public const string CacheKey = "nav-menu:v1";
    public const string CustomKeyPrefix = "custom.";

    // آیکون Bootstrap Icons: فقط حروف کوچک/رقم/خط تیره (از تزریق کلاس/HTML جلوگیری می‌کند)
    private static readonly Regex IconPattern = new(@"^bi-[a-z0-9-]{1,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PermissionPattern = new(@"^[A-Za-z][A-Za-z0-9]{0,79}\.[A-Za-z][A-Za-z0-9]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RoutePart = new(@"^[A-Za-z][A-Za-z0-9]{0,99}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PEMSDbContext _ctx;
    private readonly IMemoryCache _cache;

    public NavigationMenuService(PEMSDbContext ctx, IMemoryCache cache)
    {
        _ctx = ctx;
        _cache = cache;
    }

    public static bool IsValidIcon(string? icon) => string.IsNullOrEmpty(icon) || IconPattern.IsMatch(icon);

    public async Task<IReadOnlyList<NavigationMenuItemDto>> GetMenuAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<NavigationMenuItemDto>? cached) && cached != null) return cached;

        var items = await _ctx.NavigationMenuItems.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NavigationMenuItemId)
            .ToListAsync(ct);
        var result = items.Select(Map).ToList();
        _cache.Set(CacheKey, (IReadOnlyList<NavigationMenuItemDto>)result, TimeSpan.FromMinutes(10));
        return result;
    }

    public async Task<IReadOnlyList<NavigationMenuItemDto>> GetAllForAdminAsync(CancellationToken ct = default)
        => (await _ctx.NavigationMenuItems.AsNoTracking()
                .OrderBy(x => x.SortOrder).ThenBy(x => x.NavigationMenuItemId)
                .ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<int> SyncDefaultsAsync(IReadOnlyList<NavigationMenuItemDefinition> definitions, CancellationToken ct = default, IReadOnlyCollection<string>? retiredKeys = null)
    {
        var existing = await _ctx.NavigationMenuItems.ToDictionaryAsync(x => x.Key, StringComparer.OrdinalIgnoreCase, ct);
        var changed = 0;

        foreach (var d in definitions)
        {
            if (!existing.TryGetValue(d.Key, out var row))
            {
                _ctx.NavigationMenuItems.Add(new NavigationMenuItem
                {
                    Key = d.Key, ParentKey = d.ParentKey, Kind = d.Kind, SortOrder = d.SortOrder, TitleKey = d.TitleKey, Icon = d.Icon,
                    VisibilityRule = d.VisibilityRule, Area = d.Area, Controller = d.Controller, Action = d.Action, ExternalPath = d.ExternalPath,
                    OpenInNewTab = d.OpenInNewTab, IsDefaultForController = d.IsDefaultForController,
                    ExtraActiveControllers = d.ExtraActiveControllers, ActiveArea = d.ActiveArea, BadgeKey = d.BadgeKey,
                    IsActive = true, IsSystem = true, CreatedDate = DateTime.Now
                });
                changed++;
                continue;
            }

            if (!row.IsSystem) continue;   // کلید سفارشی هم‌نام هرگز تصاحب نمی‌شود

            // فقط فیلدهای مالکیت کد؛ ترتیب/والد/آیکون/عنوان/نمایان‌بودن/شمارنده‌ی مدیر دست نمی‌خورد
            var dirty = false;
            void Set<T>(T current, T value, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, value)) { apply(value); dirty = true; } }
            Set(row.Kind, d.Kind, v => row.Kind = v);
            Set(row.TitleKey, d.TitleKey, v => row.TitleKey = v);
            Set(row.VisibilityRule, d.VisibilityRule, v => row.VisibilityRule = v);
            Set(row.Area, d.Area, v => row.Area = v);
            Set(row.Controller, d.Controller, v => row.Controller = v);
            Set(row.Action, d.Action, v => row.Action = v);
            Set(row.ExternalPath, d.ExternalPath, v => row.ExternalPath = v);
            Set(row.OpenInNewTab, d.OpenInNewTab, v => row.OpenInNewTab = v);
            Set(row.IsDefaultForController, d.IsDefaultForController, v => row.IsDefaultForController = v);
            Set(row.ExtraActiveControllers, d.ExtraActiveControllers, v => row.ExtraActiveControllers = v);
            Set(row.ActiveArea, d.ActiveArea, v => row.ActiveArea = v);
            if (dirty) { row.ModifiedDate = DateTime.Now; changed++; }
        }

        // کلیدهای بازنشسته: ردیف سیستمیِ قدیمی غیرفعال می‌شود (حذف نه)
        foreach (var key in retiredKeys ?? Array.Empty<string>())
            if (existing.TryGetValue(key, out var old) && old.IsSystem && old.IsActive)
            {
                old.IsActive = false; old.ModifiedDate = DateTime.Now; changed++;
            }

        if (changed > 0)
        {
            await _ctx.SaveChangesAsync(ct);
            _cache.Remove(CacheKey);
        }
        return changed;
    }

    public async Task<NavigationMenuResult> UpdateAsync(NavigationMenuItemEditInput input, CancellationToken ct = default)
    {
        var row = await _ctx.NavigationMenuItems.FirstOrDefaultAsync(x => x.Key == input.Key, ct);
        if (row == null) return NavigationMenuResult.Fail("Nav_Err_NotFound");
        if (!IsValidIcon(input.Icon)) return NavigationMenuResult.Fail("Nav_Err_Icon");
        if (Trim(input.TitleEn)?.Length > 200 || Trim(input.TitleFa)?.Length > 200) return NavigationMenuResult.Fail("Nav_Err_TitleTooLong");

        var parentCheck = await ValidateParentAsync(row, input.ParentKey, ct);
        if (parentCheck != null) return parentCheck;

        if (!string.IsNullOrEmpty(input.RowVersion))
            _ctx.Entry(row).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(input.RowVersion);

        if (!string.Equals(row.ParentKey, Trim(input.ParentKey), StringComparison.Ordinal))
        {
            row.ParentKey = Trim(input.ParentKey);
            row.SortOrder = await NextSortOrderAsync(row.ParentKey, ct);   // انتقال به انتهای والد تازه
        }
        row.TitleEn = Trim(input.TitleEn);
        row.TitleFa = Trim(input.TitleFa);
        row.Icon = Trim(input.Icon);
        row.BadgeKey = Trim(input.BadgeKey);
        row.IsActive = input.IsActive;
        row.ModifiedDate = DateTime.Now;

        return await SaveAsync(ct);
    }

    public async Task<NavigationMenuResult> MoveAsync(string key, bool up, CancellationToken ct = default)
    {
        var row = await _ctx.NavigationMenuItems.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row == null) return NavigationMenuResult.Fail("Nav_Err_NotFound");

        var siblings = await _ctx.NavigationMenuItems.Where(x => x.ParentKey == row.ParentKey)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.NavigationMenuItemId).ToListAsync(ct);
        var index = siblings.FindIndex(x => x.NavigationMenuItemId == row.NavigationMenuItemId);
        var target = up ? index - 1 : index + 1;
        if (target < 0 || target >= siblings.Count) return NavigationMenuResult.Ok();   // ابتدای/انتهای فهرست: بدون تغییر

        // شماره‌گذاری دوباره‌ی هم‌والدها (ترتیب‌های تکراری قدیمی هم یکتا می‌شوند)، سپس جابه‌جایی
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
        {
            var order = (i + 1) * 10;
            if (siblings[i].SortOrder != order) { siblings[i].SortOrder = order; siblings[i].ModifiedDate = DateTime.Now; }
        }
        return await SaveAsync(ct);
    }

    public async Task<NavigationMenuResult> AddCustomLinkAsync(NavigationMenuCustomLinkInput input,
        Func<string, Task<bool>> permissionExists, Func<string?, string, string, bool> routeExists, CancellationToken ct = default)
    {
        var titleEn = Trim(input.TitleEn);
        var titleFa = Trim(input.TitleFa);
        if (titleEn == null || titleFa == null) return NavigationMenuResult.Fail("Nav_Err_TitleRequired");
        if (titleEn.Length > 200 || titleFa.Length > 200) return NavigationMenuResult.Fail("Nav_Err_TitleTooLong");
        if (!IsValidIcon(Trim(input.Icon))) return NavigationMenuResult.Fail("Nav_Err_Icon");

        var area = Trim(input.Area);
        var controller = Trim(input.Controller);
        var action = Trim(input.Action) ?? "Index";
        if (controller == null || !RoutePart.IsMatch(controller) || !RoutePart.IsMatch(action) || (area != null && !RoutePart.IsMatch(area))
            || !routeExists(area, controller, action))
            return NavigationMenuResult.Fail("Nav_Err_Route");

        var rule = NavigationVisibilityRules.Always;
        var permission = Trim(input.RequiredPermission);
        if (permission != null)
        {
            if (!PermissionPattern.IsMatch(permission) || !await permissionExists(permission)) return NavigationMenuResult.Fail("Nav_Err_Permission");
            rule = "Perm:" + permission;
        }

        var parent = await _ctx.NavigationMenuItems.FirstOrDefaultAsync(x => x.Key == input.ParentKey, ct);
        if (parent == null || parent.Kind == NavigationMenuItemKinds.Link) return NavigationMenuResult.Fail("Nav_Err_Parent");

        _ctx.NavigationMenuItems.Add(new NavigationMenuItem
        {
            Key = CustomKeyPrefix + Guid.NewGuid().ToString("N")[..12],
            ParentKey = parent.Key,
            Kind = NavigationMenuItemKinds.Link,
            SortOrder = await NextSortOrderAsync(parent.Key, ct),
            TitleKey = titleEn,
            TitleEn = titleEn,
            TitleFa = titleFa,
            Icon = Trim(input.Icon) ?? "bi-link-45deg",
            Area = area,
            Controller = controller,
            Action = action,
            VisibilityRule = rule,
            IsActive = true,
            IsSystem = false,
            CreatedDate = DateTime.Now
        });
        return await SaveAsync(ct);
    }

    public async Task<NavigationMenuResult> DeleteCustomAsync(string key, CancellationToken ct = default)
    {
        var row = await _ctx.NavigationMenuItems.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row == null) return NavigationMenuResult.Fail("Nav_Err_NotFound");
        if (row.IsSystem) return NavigationMenuResult.Fail("Nav_Err_SystemItem");
        _ctx.NavigationMenuItems.Remove(row);
        return await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetViewPermissionNamesAsync(CancellationToken ct = default)
        => await _ctx.Permissions.AsNoTracking()
            .Where(p => p.IsActive && p.ActionName == "View")
            .Select(p => p.ResourceName + ".View")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);

    private async Task<NavigationMenuResult?> ValidateParentAsync(NavigationMenuItem row, string? parentKey, CancellationToken ct)
    {
        parentKey = Trim(parentKey);
        if (parentKey == null)
            // سطح بالا: بخش‌ها و پیوندها مجازند؛ زیرتیتر فقط داخل بخش معنا دارد
            return row.Kind == NavigationMenuItemKinds.Subheading ? NavigationMenuResult.Fail("Nav_Err_Parent") : null;

        if (string.Equals(parentKey, row.Key, StringComparison.OrdinalIgnoreCase)) return NavigationMenuResult.Fail("Nav_Err_Parent");
        var parent = await _ctx.NavigationMenuItems.AsNoTracking().FirstOrDefaultAsync(x => x.Key == parentKey, ct);
        if (parent == null) return NavigationMenuResult.Fail("Nav_Err_Parent");

        // بخش فقط سطح بالا؛ زیرتیتر فقط زیر بخش؛ پیوند زیر بخش یا زیرتیتر
        var ok = row.Kind switch
        {
            NavigationMenuItemKinds.Section => false,
            NavigationMenuItemKinds.Subheading => parent.Kind == NavigationMenuItemKinds.Section,
            _ => parent.Kind is NavigationMenuItemKinds.Section or NavigationMenuItemKinds.Subheading
        };
        return ok ? null : NavigationMenuResult.Fail("Nav_Err_Parent");
    }

    private async Task<int> NextSortOrderAsync(string? parentKey, CancellationToken ct)
        => (await _ctx.NavigationMenuItems.Where(x => x.ParentKey == parentKey).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 10;

    private async Task<NavigationMenuResult> SaveAsync(CancellationToken ct)
    {
        try
        {
            await _ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _ctx.ChangeTracker.Clear();
            return NavigationMenuResult.Fail("Nav_Err_Concurrency");
        }
        _cache.Remove(CacheKey);
        return NavigationMenuResult.Ok();
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static NavigationMenuItemDto Map(NavigationMenuItem x) => new()
    {
        Id = x.NavigationMenuItemId, Key = x.Key, ParentKey = x.ParentKey, Kind = x.Kind, SortOrder = x.SortOrder, TitleKey = x.TitleKey,
        TitleEn = x.TitleEn, TitleFa = x.TitleFa, Icon = x.Icon, Area = x.Area, Controller = x.Controller, Action = x.Action,
        ExternalPath = x.ExternalPath, OpenInNewTab = x.OpenInNewTab, VisibilityRule = x.VisibilityRule,
        IsDefaultForController = x.IsDefaultForController, ExtraActiveControllers = x.ExtraActiveControllers, ActiveArea = x.ActiveArea,
        BadgeKey = x.BadgeKey, IsActive = x.IsActive, IsSystem = x.IsSystem,
        RowVersion = x.RowVersion is { Length: > 0 } ? Convert.ToBase64String(x.RowVersion) : null
    };
}

public class NavigationCountService : INavigationCountService
{
    private static readonly string[] ClosedWorkOrderStatuses = { "Closed", "Cancelled", "Rejected" };
    private readonly PEMSDbContext _ctx;

    public NavigationCountService(PEMSDbContext ctx) => _ctx = ctx;

    public Task<int> CountOpenWorkOrdersAsync(CancellationToken ct = default)
        => _ctx.WorkOrders.AsNoTracking().CountAsync(x => x.IsActive && !ClosedWorkOrderStatuses.Contains(x.Status), ct);

    public Task<int> CountStoreRequisitionsAwaitingApprovalAsync(CancellationToken ct = default)
        => _ctx.StoreRequisitions.AsNoTracking().CountAsync(x => x.IsActive && x.Status == StoreRequisitionStatuses.Submitted, ct);
}
