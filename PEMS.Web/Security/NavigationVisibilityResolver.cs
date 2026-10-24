using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Security;

/// <summary>
/// طبق سند «PEMS Phase 6 — User Module & Navigation Visibility Architecture»: تنها نقطه‌ی ترکیب
/// سه لایه‌ی Visibility - Override دستی مدیر (NavigationVisibilityOverride) > پیش‌فرض مجوزمحور
/// (فقط آیتم‌های CmmsIntegration، از طریق IAccessControlService.AuthorizeForRolesAsync - همون
/// مسیر سبک و بدون Audit که در EffectiveRole/EffectiveUser فاز ۴ استفاده شده) > Fallback نهایی
/// (MenuAccess موجود، بدون تغییر).
///
/// این کلاس هرگز تصمیم Authorization نمی‌گیرد و هرگز مصرف‌کننده‌ی نتیجه‌اش برای رد کردن یک
/// درخواست نیست - فقط می‌گوید «چه چیزی نشان داده شود»، نه «چه کاری مجاز است». حذف کامل این کلاس
/// نباید هیچ [Authorize]/HasPermissionAsync موجودی را بشکند (Hidden ≠ Unauthorized).
///
/// معناشناسی اجتماع قطعی چند-نقشی (بدون Deny-Override دلبخواهی): برای هر نقش کاربر، اگر آن نقش
/// Override صریح دارد همان حاکم است؛ وگرنه پیش‌فرض. اگر برای *هیچ‌کدام* از نقش‌ها Override صریحِ
/// «مخفی» تمام نقش‌ها را نپوشانده باشد، پیش‌فرض برای نقش‌های باقی‌مانده محاسبه و با OR ترکیب
/// می‌شود - یک Override مخفی روی نقش A هرگز چیزی را از یک کاربر با نقش B که پیش‌فرضش «نمایان»
/// است، پنهان نمی‌کند.
/// </summary>
public class NavigationVisibilityResolver
{
    private readonly INavigationVisibilityOverrideService _overrides;
    private readonly IAccessControlService _accessControl;

    public NavigationVisibilityResolver(INavigationVisibilityOverrideService overrides, IAccessControlService accessControl)
    {
        _overrides = overrides;
        _accessControl = accessControl;
    }

    /// <param name="roleNames">نقش‌های کاربر جاری (CurrentUserService.Roles).</param>
    /// <param name="precomputedModuleDefaults">
    /// طبق تصمیم صریح: QuickSetup و ImportCenter از قبل، همین امروز، یک محاسبه‌ی دقیق و واقعی
    /// دارند (canSeeQuickSetup مجوزمحور از PermissionService، canSeeImportCenter از
    /// ImportCatalog.VisibleByRole) - این Resolver عمداً آن‌ها را از نو بازتولید نمی‌کند (ریسک
    /// انحراف از منبع واقعی)، فقط همان دو مقدار از قبل محاسبه‌شده را به‌عنوان پیش‌فرض می‌پذیرد تا
    /// روی آن‌ها هم Override اعمال شود.
    /// </param>
    public async Task<NavigationVisibilitySnapshot> ResolveAsync(
        IReadOnlyList<string> roleNames,
        IReadOnlyDictionary<string, bool>? precomputedModuleDefaults = null)
    {
        var overrideRows = await _overrides.GetOverridesForRoleNamesAsync(roleNames);

        var visibleModules = new HashSet<string>();
        foreach (var moduleKey in NavigationCatalog.ModuleKeys)
        {
            Func<List<string>, Task<bool>> computeDefault;
            if (precomputedModuleDefaults != null && precomputedModuleDefaults.TryGetValue(moduleKey, out var precomputed))
            {
                computeDefault = remaining => Task.FromResult(remaining.Count > 0 && precomputed);
            }
            else if (moduleKey == "QuickSetup")
            {
                // طبق _SidebarMenu.cshtml (canSeeQuickSetup): وقتی فراخوان (مثلاً صفحه‌ی Effective
                // User مدیر) پیش‌محاسبه نمی‌دهد، همچنان باید مجوزمحور واقعی بماند - نه false ثابت
                computeDefault = async remaining => remaining.Count > 0 && (await _accessControl.AuthorizeForRolesAsync(remaining, "Setup", "View")).Allowed;
            }
            else
            {
                computeDefault = remaining => Task.FromResult(ModuleDefaultByMenuAccess(moduleKey, remaining));
            }

            if (await IsVisibleAsync(roleNames, overrideRows, moduleKey, null, computeDefault)
                || ExplicitItemShowOpensModule(roleNames, overrideRows, moduleKey))
            {
                visibleModules.Add(moduleKey);
            }
        }

        var visibleCmmsItems = new HashSet<string>();
        if (visibleModules.Contains("CmmsIntegration"))
        {
            foreach (var (itemKey, resource) in NavigationCatalog.CmmsIntegrationItemResourceMap)
            {
                var isVisible = await IsVisibleAsync(roleNames, overrideRows, "CmmsIntegration", itemKey,
                    defaultForRemainingRoles: async remaining =>
                    {
                        // بدون Resource نگاشته‌شده (Instrument/Asset/SparePart) = بدون سیگنال مجوزی واقعی -
                        // پیش‌فرض «نمایان» (همون رفتار قبل از فاز ۶، فقط دروازه‌ی سطح ماژول حاکم بود)
                        // (نقش‌هایی که ماژول را فقط با Override کاتالوگ/مدیر می‌بینند و MenuAccess ندارند، آیتم‌های بدون سیگنال مجوزی را نمی‌بینند - لینک مرده نمی‌شود)
                        if (resource == null) return remaining.Any(MenuAccess.CmmsIntegration.Contains);
                        if (remaining.Count == 0) return false;
                        var decision = await _accessControl.AuthorizeForRolesAsync(remaining, resource, "View");
                        return decision.Allowed;
                    });

                if (isVisible) visibleCmmsItems.Add(itemKey);
            }
        }

        return new NavigationVisibilitySnapshot(visibleModules, visibleCmmsItems);
    }

    /// <summary>
    /// اصلاح باگ «Override آیتم ثبت می‌شود ولی کاربر چیزی در سایدبار نمی‌بیند»: آیتم‌های سطح-دوم فقط وقتی محاسبه
    /// می‌شوند که ماژول نمایان باشد، و دروازه‌ی پیش‌فرض ماژول فهرست ثابت MenuAccess است. پس برای نقشی خارج از
    /// آن فهرست (مثلاً MNT.*) یک Override صریح «نمایان» روی آیتم هیچ‌وقت رندر نمی‌شد. اکنون Override صریح
    /// «نمایان» روی یک آیتم، ماژول والد را هم باز می‌کند - مگر اینکه همان نقش ماژول را صریحاً مخفی کرده باشد
    /// (Hide صریح ماژول همچنان حاکم است). فقط Override صریح مدیر ماژول را باز می‌کند؛ مجوز (Permission) به‌تنهایی نه،
    /// تا پیش‌فرض نقش‌های سیستمی تغییر نکند و چیزی بدون تصمیم صریح مدیر جدید نمایان نشود.
    /// </summary>
    internal static bool ExplicitItemShowOpensModule(
        IReadOnlyList<string> roleNames, List<NavigationVisibilityOverrideDto> overrideRows, string moduleKey)
    {
        var rolesHidingModule = overrideRows
            .Where(o => o.ModuleKey == moduleKey && o.ItemKey == null && !o.IsVisible)
            .Select(o => o.RoleName).ToHashSet();

        return overrideRows.Any(o => o.ModuleKey == moduleKey && o.ItemKey != null && o.IsVisible
                                     && roleNames.Contains(o.RoleName) && !rolesHidingModule.Contains(o.RoleName));
    }

    private static async Task<bool> IsVisibleAsync(
        IReadOnlyList<string> roleNames,
        List<NavigationVisibilityOverrideDto> overrideRows,
        string moduleKey,
        string? itemKey,
        Func<List<string>, Task<bool>> defaultForRemainingRoles)
    {
        var matching = overrideRows.Where(o => o.ModuleKey == moduleKey && o.ItemKey == itemKey).ToList();

        // هر نقشی که Override صریحِ «نمایان» دارد، بلافاصله برنده است - بدون نیاز به محاسبه‌ی پیش‌فرض
        if (roleNames.Any(r => matching.Any(o => o.RoleName == r && o.IsVisible)))
            return true;

        // نقش‌هایی که Override صریحِ «مخفی» دارند از محاسبه‌ی پیش‌فرض کنار گذاشته می‌شوند (حاکم روی خودشان‌اند)؛
        // فقط نقش‌های بدون هیچ Override‌ای به پیش‌فرض می‌رسند
        var rolesWithHideOverride = matching.Where(o => !o.IsVisible).Select(o => o.RoleName).ToHashSet();
        var remainingRoles = roleNames.Where(r => !rolesWithHideOverride.Contains(r)).ToList();

        if (remainingRoles.Count == 0) return false;

        return await defaultForRemainingRoles(remainingRoles);
    }

    /// <summary>
    /// طبق سند فاز ۶ (Role-level module/item visibility admin UI + توضیح منبع): برخلاف
    /// ResolveAsync (که فقط «نمایان/مخفی» نهایی برمی‌گرداند)، این متد صرفاً برای صفحه‌ی مدیریت
    /// است - Default/Override/Effective هرکدام را جدا برمی‌گرداند تا مدیر ببیند چرا یک آیتم
    /// نمایان/مخفی است، نه فقط نتیجه‌ی نهایی. تک-نقشی عمداً (صفحه‌ی مدیریت همیشه روی یک نقش خاص
    /// کار می‌کند)، هیچ‌جای مسیر واقعی سایدبار (ResolveAsync) از این استفاده نمی‌کند.
    /// </summary>
    public async Task<List<NavigationVisibilityRowDescription>> DescribeForRoleAsync(string roleName)
    {
        var overrideRows = await _overrides.GetOverridesForRoleNamesAsync(new[] { roleName });
        var result = new List<NavigationVisibilityRowDescription>();
        var moduleVisible = new Dictionary<string, bool>();

        foreach (var moduleKey in NavigationCatalog.ModuleKeys)
        {
            bool defaultVisible;
            string source;
            if (moduleKey == "QuickSetup")
            {
                defaultVisible = (await _accessControl.AuthorizeForRolesAsync(new[] { roleName }, "Setup", "View")).Allowed;
                source = "Permission: Setup.View";
            }
            else
            {
                defaultVisible = ModuleDefaultByMenuAccess(moduleKey, new List<string> { roleName });
                source = moduleKey == "ImportCenter" ? "ImportCatalog role set" : "MenuAccess role list";
            }

            var ov = overrideRows.FirstOrDefault(o => o.ModuleKey == moduleKey && o.ItemKey == null);
            var openedByItem = ov == null && !defaultVisible && ExplicitItemShowOpensModule(new[] { roleName }, overrideRows, moduleKey);
            var effective = ov?.IsVisible ?? (defaultVisible || openedByItem);
            if (openedByItem) source = "Opened by an explicit item-level override";
            moduleVisible[moduleKey] = effective;

            result.Add(new NavigationVisibilityRowDescription
            {
                ModuleKey = moduleKey,
                ItemKey = null,
                DefaultVisible = defaultVisible,
                OverrideVisible = ov?.IsVisible,
                EffectiveVisible = effective,
                DefaultSource = source
            });
        }

        {
            // طبق تست ۸ سند اصلاحی (Permission Matrix مستقل از Navigation Visibility): برخلاف
            // ResolveAsync (مسیر واقعی سایدبار، که عمداً فقط وقتی ماژول نمایان است آیتم‌ها را
            // محاسبه می‌کند - یک بهینه‌سازی کارایی درست برای آن مسیر داغ)، این متد فقط برای صفحه‌ی
            // تشخیصی مدیر است - باید همیشه وضعیت واقعی هر آیتم را نشان دهد، حتی وقتی خودِ ماژول
            // امروز پیش‌فرضش مخفی است (مثلاً یک نقش تازه که هنوز MenuAccess.CmmsIntegration را
            // ندارد ولی از قبل Permission واقعی روی یک آیتم خاص گرفته). وگرنه مدیر هرگز نمی‌تواند
            // ببیند «این نقش اگر ماژول را هم نمایان کنم، کدام آیتم‌ها را از قبل می‌بیند».
            foreach (var (itemKey, resource) in NavigationCatalog.CmmsIntegrationItemResourceMap)
            {
                bool defaultVisible;
                string source;
                if (resource == null)
                {
                    defaultVisible = MenuAccess.CmmsIntegration.Contains(roleName);
                    source = "No permission signal (visible only for MenuAccess CMMS roles)";
                }
                else
                {
                    defaultVisible = (await _accessControl.AuthorizeForRolesAsync(new[] { roleName }, resource, "View")).Allowed;
                    source = $"Permission: {resource}.View";
                }

                var ov = overrideRows.FirstOrDefault(o => o.ModuleKey == "CmmsIntegration" && o.ItemKey == itemKey);
                var effective = ov?.IsVisible ?? defaultVisible;

                result.Add(new NavigationVisibilityRowDescription
                {
                    ModuleKey = "CmmsIntegration",
                    ItemKey = itemKey,
                    DefaultVisible = defaultVisible,
                    OverrideVisible = ov?.IsVisible,
                    EffectiveVisible = effective,
                    DefaultSource = source
                });
            }
        }

        return result;
    }

    private static bool ModuleDefaultByMenuAccess(string moduleKey, List<string> roles) => moduleKey switch
    {
        "EngineeringManagement" => roles.Any(MenuAccess.EngineeringManagement.Contains),
        "AIIntelligence" => roles.Any(MenuAccess.AIIntelligence.Contains),
        "CorrespondenceDocControl" => roles.Any(MenuAccess.CorrespondenceDocControl.Contains),
        "QualityControl" => roles.Any(MenuAccess.QualityControl.Contains),
        "ContractManagement" => roles.Any(MenuAccess.ContractManagement.Contains),
        "ProjectControls" => roles.Any(MenuAccess.ProjectControls.Contains),
        // طبق _SidebarMenu.cshtml: بخش «Inventory & Materials» امروز هم دقیقاً با همین آرایه دروازه دارد
        "CmmsIntegration" or "Inventory" => roles.Any(MenuAccess.CmmsIntegration.Contains),
        "Operations" => roles.Any(MenuAccess.Operations.Contains),
        "Administration" => roles.Any(MenuAccess.Administration.Contains),
        "ImportCenter" => roles.Any(MenuAccess.ImportCenter.Contains),
        _ => false
    };
}
