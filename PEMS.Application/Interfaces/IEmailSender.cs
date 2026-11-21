namespace PEMS.Application.Interfaces;

/// <summary>One email: plain text body (Persian and English read correctly in any client), to one recipient.</summary>
public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string TextBody);

/// <summary>
/// SCRUM-429: sends email through the organisation's own mail server (SMTP; env <c>Email__*</c>, the account in the secrets file).
/// Offline product: no other host is contacted. When <see cref="IsEnabled"/> is false nothing is sent (demo, or no mail server yet).
/// </summary>
public interface IEmailSender
{
    bool IsEnabled { get; }

    /// <summary>Sends or throws (connection, authentication or rejection); the caller records the result.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
