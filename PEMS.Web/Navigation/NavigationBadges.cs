using PEMS.Application.Interfaces;

namespace PEMS.Web.Navigation;

/// <summary>
/// فهرست بسته‌ی شمارنده‌های سایدبار. مدیر فقط از همین فهرست برای یک آیتم انتخاب می‌کند (نه SQL/فرمول آزاد). هر شمارنده یا «مال خود
/// کاربر» است (کارهای من، اعلان‌ها، اقدام‌ها) یا شمارش ماژولی که فقط برای دارنده‌ی مجوز مشخص‌شده محاسبه می‌شود؛ بدون مجوز، شمارنده
/// اصلاً محاسبه و ارسال نمی‌شود (شمارش هم داده است).
/// </summary>
public static class NavigationBadges
{
    public const string MyWorkPending = "mywork.pending";
    public const string MyWorkOverdue = "mywork.overdue";
    public const string NotificationsUnread = "notifications.unread";
    public const string TasksOpen = "tasks.open";
    public const string MyWorkPartCatalogue = "mywork.module.PartCatalogue";
    public const string MyWorkWorkOrder = "mywork.module.WorkOrder";
    public const string MyWorkWorkRequest = "mywork.module.WorkRequest";
    public const string MyWorkContract = "mywork.module.Contract";
    public const string MyWorkIrn = "mywork.module.IRN";
    public const string WorkOrdersOpen = "workorders.open";
    public const string StoreRequisitionsAwaitingApproval = "storerequisitions.awaitingapproval";

    /// <param name="TitleKey">کلید بومی‌سازی نام شمارنده در صفحه‌ی مدیریت.</param>
    /// <param name="RequiredPermission">«Resource.Action» لازم برای دیدن شمارش ماژولی؛ null = شمارش شخصی خود کاربر.</param>
    /// <param name="Tone">رنگ بج: primary (اطلاع) یا danger (عقب‌افتاده/نیازمند اقدام فوری).</param>
    public sealed record Definition(string Key, string TitleKey, string? RequiredPermission, string Tone = "primary");

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(MyWorkPending, "Nav_Badge_MyWorkPending", null),
        new Definition(MyWorkOverdue, "Nav_Badge_MyWorkOverdue", null, "danger"),
        new Definition(NotificationsUnread, "Nav_Badge_NotificationsUnread", null),
        new Definition(TasksOpen, "Nav_Badge_TasksOpen", null),
        new Definition(MyWorkPartCatalogue, "Nav_Badge_MyWorkPartCatalogue", null),
        new Definition(MyWorkWorkOrder, "Nav_Badge_MyWorkWorkOrder", null),
        new Definition(MyWorkWorkRequest, "Nav_Badge_MyWorkWorkRequest", null),
        new Definition(MyWorkContract, "Nav_Badge_MyWorkContract", null),
        new Definition(MyWorkIrn, "Nav_Badge_MyWorkIrn", null),
        new Definition(WorkOrdersOpen, "Nav_Badge_WorkOrdersOpen", "WorkOrder.View"),
        new Definition(StoreRequisitionsAwaitingApproval, "Nav_Badge_SrAwaitingApproval", "StoreRequisition.Approve", "danger"),
    };

    public static Definition? Find(string? key) => key == null ? null : All.FirstOrDefault(d => d.Key == key);
}

/// <summary>محاسبه‌ی شمارنده‌ها برای کاربر جاری؛ فقط کلیدهایی که در سایدبار همین کاربر نمایان‌اند درخواست می‌شوند.</summary>
public class NavigationBadgeService
{
    private const string MyWorkModulePrefix = "mywork.module.";

    private readonly IMyWorkService _myWork;
    private readonly INotificationService _notifications;
    private readonly ITaskService _tasks;
    private readonly INavigationCountService _counts;
    private readonly IPermissionService _permissions;

    public NavigationBadgeService(IMyWorkService myWork, INotificationService notifications, ITaskService tasks,
        INavigationCountService counts, IPermissionService permissions)
    {
        _myWork = myWork;
        _notifications = notifications;
        _tasks = tasks;
        _counts = counts;
        _permissions = permissions;
    }

    public async Task<Dictionary<string, int>> GetCountsAsync(IEnumerable<string> keys, int? userId, int? employeeId, IReadOnlyList<string> roles)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var wanted = keys.Where(k => NavigationBadges.Find(k) != null).Distinct().ToList();
        if (wanted.Count == 0) return result;

        // کارهای من (یک‌بار برای همه‌ی کلیدهای mywork.*)
        if (wanted.Any(k => k.StartsWith("mywork.", StringComparison.Ordinal)) && employeeId is > 0)
        {
            var summary = await _myWork.GetSummaryAsync(employeeId.Value, roles);
            foreach (var k in wanted)
            {
                if (k == NavigationBadges.MyWorkPending) result[k] = summary.Total;
                else if (k == NavigationBadges.MyWorkOverdue) result[k] = summary.Overdue;
                else if (k.StartsWith(MyWorkModulePrefix, StringComparison.Ordinal))
                    result[k] = summary.ByModule.TryGetValue(k[MyWorkModulePrefix.Length..], out var n) ? n : 0;
            }
        }

        if (wanted.Contains(NavigationBadges.NotificationsUnread) && userId.HasValue)
            result[NavigationBadges.NotificationsUnread] = await _notifications.GetUnreadCountAsync(userId.Value);

        if (wanted.Contains(NavigationBadges.TasksOpen) && userId.HasValue)
            result[NavigationBadges.TasksOpen] = await _tasks.GetOpenActionItemCountAsync(userId.Value);

        if (wanted.Contains(NavigationBadges.WorkOrdersOpen) && await AllowedAsync(NavigationBadges.WorkOrdersOpen, roles))
            result[NavigationBadges.WorkOrdersOpen] = await _counts.CountOpenWorkOrdersAsync();

        if (wanted.Contains(NavigationBadges.StoreRequisitionsAwaitingApproval) && await AllowedAsync(NavigationBadges.StoreRequisitionsAwaitingApproval, roles))
            result[NavigationBadges.StoreRequisitionsAwaitingApproval] = await _counts.CountStoreRequisitionsAwaitingApprovalAsync();

        return result;
    }

    private async Task<bool> AllowedAsync(string key, IReadOnlyList<string> roles)
    {
        var perm = NavigationBadges.Find(key)?.RequiredPermission;
        if (perm == null) return true;
        var parts = perm.Split('.');
        return await _permissions.HasPermissionAsync(roles, parts[0], parts[1]);
    }
}
