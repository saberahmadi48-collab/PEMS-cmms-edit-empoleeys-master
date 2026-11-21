using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-731: the notification inbox and its actions for the signed-in user (found by user name), over the same INotificationService the controller used; an id of another user's notification is "not found".</summary>
public sealed class NotificationOperations(INotificationService service, PEMSDbContext context) : INotificationOperations
{
    private const int PageSize = 100;
    private const int MaxPages = 10;

    public async Task<List<NotificationRow>> InboxAsync(string userName, string? filter = null)
    {
        var userId = await UserIdAsync(userName);
        if (userId is null) return [];
        var rows = new List<NotificationRow>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await service.GetInboxAsync(userId.Value, string.IsNullOrWhiteSpace(filter) ? "all" : filter, page, PageSize);
            rows.AddRange(result.Items.Select(n => new NotificationRow(n.NotificationId, n.Title, n.Message, n.CreatedDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                n.DismissedDate.HasValue ? "Dismissed" : n.IsRead ? "Read" : "Unread")));
            if (result.Items.Count == 0 || rows.Count >= result.TotalCount) break;
        }
        return rows;
    }

    public async Task<NotificationActionResult> MarkReadAsync(string userName, int id)
        => await UserIdAsync(userName) is { } userId && await service.MarkAsReadAsync(userId, id) ? new(true, null) : new(false, "Notif_Err_NotFound");

    public async Task<NotificationActionResult> DismissAsync(string userName, int id)
        => await UserIdAsync(userName) is { } userId && await service.DismissAsync(userId, id) ? new(true, null) : new(false, "Notif_Err_NotFound");

    public async Task<NotificationActionResult> MarkAllReadAsync(string userName)
    {
        if (await UserIdAsync(userName) is not { } userId) return new(false, "Notif_Err_NotFound");
        await service.MarkAllAsReadAsync(userId);
        return new(true, null);
    }

    public async Task<NotificationActionResult> DismissAllAsync(string userName)
    {
        if (await UserIdAsync(userName) is not { } userId) return new(false, "Notif_Err_NotFound");
        await service.DismissAllAsync(userId);
        return new(true, null);
    }

    private async Task<int?> UserIdAsync(string userName)
        => string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => (int?)u.Id).FirstOrDefaultAsync();
}
