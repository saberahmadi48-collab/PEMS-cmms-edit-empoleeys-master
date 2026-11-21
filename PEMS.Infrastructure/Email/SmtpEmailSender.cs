using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PEMS.Application.Interfaces;

namespace PEMS.Infrastructure.Email;

/// <summary>
/// SCRUM-429: <see cref="IEmailSender"/> on MailKit (MIT licence) against the organisation's SMTP server. One connection per
/// message: the volume is small (one invitation per vendor) and the background job retries a failed one.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public bool IsEnabled => _options.IsUsable;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsEnabled) throw new InvalidOperationException("Email is switched off (Email__Enabled) or the mail server is not set.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName ?? _options.FromAddress, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.ToAddress, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient { Timeout = Math.Max(5, _options.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(_options.Host, _options.Port, ParseSecurity(_options.Security), ct);
        if (!string.IsNullOrEmpty(_options.UserName))
            await client.AuthenticateAsync(_options.UserName, _options.Password ?? string.Empty, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }

    public static SecureSocketOptions ParseSecurity(string? value) => (value ?? "Auto").Trim().ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" or "ssl" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };
}
