namespace PEMS.Infrastructure.Email;

/// <summary>
/// SCRUM-429: section <c>Email</c> of the env files. Settings in <c>env/&lt;name&gt;.env</c>; <c>UserName</c> and <c>Password</c> only in
/// <c>env/&lt;name&gt;.secrets.env</c> (empty = the server accepts mail without login, e.g. an internal relay).
/// </summary>
public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 25;

    /// <summary>Auto | None | StartTls | SslOnConnect (MailKit SecureSocketOptions).</summary>
    public string Security { get; set; } = "Auto";

    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Usable only when switched on and the server and sender are set.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}
