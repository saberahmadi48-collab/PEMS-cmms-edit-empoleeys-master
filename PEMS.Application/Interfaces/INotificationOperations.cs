namespace PEMS.Application.Interfaces;

/// <summary>One notification of the inbox page; <c>Status</c> is Unread, Read or Dismissed.</summary>
public sealed record NotificationRow(int NotificationId, string Title, string Message, string CreatedDate, string Status);

/// <summary>The answer of a notification action: the error is a resource key.</summary>
public sealed record NotificationActionResult(bool Success, string? ErrorKey);

/// <summary>The notification inbox page as operations (SCRUM-731): only the notifications of the signed-in user, never an id from the input for the user; the user is the context user name.</summary>
public interface INotificationOperations
{
    [PEMS.Application.Common.PemsOperation("Notification", "Inbox")]
    Task<List<NotificationRow>> InboxAsync(string userName, string? filter = null);

    [PEMS.Application.Common.PemsOperation("Notification", "MarkRead")]
    Task<NotificationActionResult> MarkReadAsync(string userName, int id);

    [PEMS.Application.Common.PemsOperation("Notification", "Dismiss")]
    Task<NotificationActionResult> DismissAsync(string userName, int id);

    [PEMS.Application.Common.PemsOperation("Notification", "MarkAllRead")]
    Task<NotificationActionResult> MarkAllReadAsync(string userName);

    [PEMS.Application.Common.PemsOperation("Notification", "DismissAll")]
    Task<NotificationActionResult> DismissAllAsync(string userName);
}
