using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Infrastructure.Email;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-429: tender invitations by email. Queueing writes one log row per invited vendor (Skipped without an email), fills the
/// template in the sender's language and never queues a vendor twice; the job records Sent/Failed and lets the runner retry until
/// the last attempt; email off or a closed tender is refused. The MailKit sender delivers a real SMTP conversation.
/// </summary>
public class TenderInvitationTests
{
    private sealed class FakeSender : IEmailSender
    {
        public bool IsEnabled { get; set; } = true;
        public int FailNext { get; set; }
        public List<EmailMessage> Sent { get; } = new();
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (FailNext > 0) { FailNext--; throw new InvalidOperationException("mail server refused"); }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static readonly TenderInvitationTemplate Fa = new("دعوت {TenderNumber}", "{Vendor}|{Title}|{Package}|{Deadline}|{Requirements}|{Company}",
        new CultureInfo("fa-IR"), "شرکت نمونه");

    private static async Task<(PEMSDbContext Db, FakeSender Mail, TenderInvitationService Svc, int TenderId, int WithMail, int NoMail)> EnvAsync(string name, string status = "Open")
    {
        var db = PemsDbContextFactory.CreateInMemory("tinv_" + name);
        var project = new Project { ProjectName = "P", ProjectCode = "P1", IsActive = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var package = new ProcurementPackage { PackageNumber = "PP-1405-0001", ProjectId = project.ProjectId, Title = "Pumps", Status = "Open", Stage = "MR" };
        var withMail = new Vendor { Name = "Pumpco", ContactName = "Mr Rahimi", ContactEmail = "sales@pumpco.example", IsActive = true };
        var noMail = new Vendor { Name = "Valveco", ContactEmail = "  ", IsActive = true };
        db.AddRange(package, withMail, noMail);
        await db.SaveChangesAsync();
        var tender = new Tender
        {
            TenderNumber = "TND-1405-0001", ProcurementPackageId = package.ProcurementPackageId, Title = "Feed pumps", Status = status,
            ResponseDeadline = new DateTime(2026, 11, 21), TechnicalRequirements = "API 610", IsActive = true
        };
        tender.InvitedVendors.Add(new TenderInvitedVendor { VendorId = withMail.VendorId, IsActive = true });
        tender.InvitedVendors.Add(new TenderInvitedVendor { VendorId = noMail.VendorId, IsActive = true });
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        var mail = new FakeSender();
        return (db, mail, new TenderInvitationService(db, mail), tender.TenderId, withMail.VendorId, noMail.VendorId);
    }

    [Fact]
    public async Task Queue_writes_one_row_per_vendor_fills_the_template_and_skips_a_vendor_without_email()
    {
        var e = await EnvAsync(nameof(Queue_writes_one_row_per_vendor_fills_the_template_and_skips_a_vendor_without_email));

        var r = await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1");

        Assert.Null(r.Error);
        Assert.Single(r.QueuedDispatchIds);
        Assert.Equal(1, r.SkippedWithoutEmail);
        var rows = e.Db.TenderInvitationDispatches.OrderBy(d => d.VendorId).ToList();
        var sendRow = rows.Single(d => d.VendorId == e.WithMail);
        Assert.Equal(("sales@pumpco.example", TenderInvitationStatuses.Queued, "buyer1"), (sendRow.ToAddress, sendRow.Status, sendRow.QueuedByUserName));
        Assert.Equal("دعوت TND-1405-0001", sendRow.Subject);
        Assert.Equal("Mr Rahimi|Feed pumps|PP-1405-0001|1405/08/30|API 610|شرکت نمونه", sendRow.Body);   // Jalali deadline
        var skipped = rows.Single(d => d.VendorId == e.NoMail);
        Assert.Equal((TenderInvitationStatuses.Skipped, "Tinv_Err_NoVendorEmail", ""), (skipped.Status, skipped.LastError, skipped.ToAddress));

        // nobody is queued twice; a resend names the vendor
        Assert.Equal("Tinv_Err_NothingToSend", (await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1")).Error);
        var again = await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1", new[] { e.WithMail });
        Assert.Single(again.QueuedDispatchIds);

        // each send action leaves one readable audit entry on the tender
        var audit = e.Db.AuditLogs.Where(a => a.EntityName == nameof(Tender) && a.EntityKeyValue == e.TenderId.ToString()
                && a.Action.EndsWith("Invitations"))
            .OrderBy(a => a.AuditLogId).ToList();
        Assert.Equal(new[] { "SendInvitations", "ResendInvitations" }, audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal("buyer1", a.UserName));
        Assert.Contains("Skipped (no email): 1", audit[0].ChangedFieldsSummary);
    }

    [Fact]
    public void Every_environment_has_email_switched_off_and_keeps_the_account_in_the_secrets_file_only()
    {
        var env = Path.Combine(SupportedReleases.RepoRoot(), "env");
        var settings = Directory.GetFiles(env, "*.env").Where(f => !f.EndsWith(".secrets.env")).ToList();
        Assert.Equal(6, settings.Count);
        foreach (var file in settings)
        {
            var lines = File.ReadAllLines(file);
            foreach (var key in new[] { "Host", "Port", "Security", "FromAddress", "FromName", "TimeoutSeconds" })
                Assert.Contains(lines, l => l.StartsWith($"Email__{key}="));
            Assert.Contains("Email__Enabled=false", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("Email__Password") || l.StartsWith("Email__UserName"));
        }
        var examples = Directory.GetFiles(env, "*.secrets.env.example");
        Assert.Equal(6, examples.Length);
        foreach (var file in examples)
        {
            var lines = File.ReadAllLines(file);
            Assert.Contains("Email__UserName=", lines);
            Assert.Contains("Email__Password=", lines);
        }
    }

    [Fact]
    public async Task Only_skipped_vendors_remain_for_send_all_and_english_dates_are_iso()
    {
        var e = await EnvAsync(nameof(Only_skipped_vendors_remain_for_send_all_and_english_dates_are_iso));
        await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1");
        var vendor = e.Db.Vendors.Single(v => v.VendorId == e.NoMail);
        vendor.ContactEmail = "info@valveco.example";
        await e.Db.SaveChangesAsync();

        var en = new TenderInvitationTemplate("Invitation {TenderNumber}", "{Deadline}", CultureInfo.InvariantCulture, null);
        var r = await e.Svc.QueueAsync(e.TenderId, en, "buyer1");   // the skipped vendor now has an email; the queued one is not repeated

        var row = e.Db.TenderInvitationDispatches.Single(d => d.TenderInvitationDispatchId == r.QueuedDispatchIds.Single());
        Assert.Equal((e.NoMail, "2026-11-21"), (row.VendorId, row.Body));
    }

    [Fact]
    public async Task Send_records_sent_or_failed_and_rethrows_until_the_last_attempt()
    {
        var e = await EnvAsync(nameof(Send_records_sent_or_failed_and_rethrows_until_the_last_attempt));
        var id = (await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1")).QueuedDispatchIds.Single();

        e.Mail.FailNext = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Svc.SendAsync(id));   // attempt 1: runner retries
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Svc.SendAsync(id));   // attempt 2
        await e.Svc.SendAsync(id);                                                          // attempt 3: stays Failed, no rethrow
        var row = e.Db.TenderInvitationDispatches.Single(d => d.TenderInvitationDispatchId == id);
        Assert.Equal((TenderInvitationStatuses.Failed, 3, "mail server refused"), (row.Status, row.Attempts, row.LastError));

        var resend = (await e.Svc.QueueAsync(e.TenderId, Fa, "buyer2", new[] { e.WithMail })).QueuedDispatchIds.Single();
        await e.Svc.SendAsync(resend);
        await e.Svc.SendAsync(resend);   // idempotent: already sent
        var sent = e.Db.TenderInvitationDispatches.Single(d => d.TenderInvitationDispatchId == resend);
        Assert.Equal((TenderInvitationStatuses.Sent, 1), (sent.Status, sent.Attempts));
        Assert.NotNull(sent.SentAt);
        Assert.Single(e.Mail.Sent);
        Assert.Equal("sales@pumpco.example", e.Mail.Sent[0].ToAddress);

        var log = await e.Svc.GetLogAsync(e.TenderId);
        Assert.Equal(3, log.Count);   // nothing is deleted: failed, sent, skipped
    }

    [Theory]
    [InlineData("Closed", true, "Tinv_Err_Status")]
    [InlineData("Awarded", true, "Tinv_Err_Status")]
    [InlineData("Open", false, "Tinv_Err_EmailOff")]
    public async Task Refused_when_email_is_off_or_the_tender_is_no_longer_open(string status, bool emailOn, string error)
    {
        var e = await EnvAsync($"refused_{status}_{emailOn}", status);
        e.Mail.IsEnabled = emailOn;
        Assert.Equal(error, (await e.Svc.QueueAsync(e.TenderId, Fa, "buyer1")).Error);
        Assert.Empty(e.Db.TenderInvitationDispatches);
    }

    [Fact]
    public void Options_need_a_server_and_a_sender_and_parse_the_security_mode()
    {
        Assert.False(new EmailOptions { Enabled = true, Host = "mail.local" }.IsUsable);
        Assert.False(new EmailOptions { Enabled = false, Host = "mail.local", FromAddress = "pems@x" }.IsUsable);
        Assert.True(new EmailOptions { Enabled = true, Host = "mail.local", FromAddress = "pems@x" }.IsUsable);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, SmtpEmailSender.ParseSecurity("starttls"));
        Assert.Equal(MailKit.Security.SecureSocketOptions.Auto, SmtpEmailSender.ParseSecurity("anything"));
    }

    // ------------------------------------------------------------------ real SMTP conversation (local listener, no network)

    [Fact]
    public async Task Smtp_sender_delivers_the_message_to_the_mail_server()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RunSmtpSinkAsync(listener);

        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Enabled = true, Host = "127.0.0.1", Port = port, Security = "None", FromAddress = "pems@company.local", FromName = "PEMS", TimeoutSeconds = 10
        }));
        await sender.SendAsync(new EmailMessage("sales@pumpco.example", "Pumpco", "Invitation TND-1405-0001", "سلام\nAPI 610"));

        var (rcpt, data) = await server;
        listener.Stop();
        Assert.Equal("<sales@pumpco.example>", rcpt);
        var mime = await MimeMessage.LoadAsync(new MemoryStream(Encoding.UTF8.GetBytes(data)));
        Assert.Equal("Invitation TND-1405-0001", mime.Subject);
        Assert.Equal("pems@company.local", mime.From.Mailboxes.Single().Address);
        Assert.Contains("سلام", mime.TextBody);
    }

    /// <summary>A minimal SMTP server for one message: enough of RFC 5321 for MailKit without TLS or login.</summary>
    private static async Task<(string Rcpt, string Data)> RunSmtpSinkAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        string rcpt = "", data = "";
        await writer.WriteLineAsync("220 test ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            var cmd = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            if (cmd is "EHLO" or "HELO") { await writer.WriteLineAsync("250-test"); await writer.WriteLineAsync("250 8BITMIME"); }
            else if (cmd == "MAIL") await writer.WriteLineAsync("250 OK");
            else if (cmd == "RCPT") { rcpt = line[(line.IndexOf(':') + 1)..].Trim(); await writer.WriteLineAsync("250 OK"); }
            else if (cmd == "DATA")
            {
                await writer.WriteLineAsync("354 go");
                var sb = new StringBuilder();
                while (await reader.ReadLineAsync() is { } d && d != ".") sb.Append(d.StartsWith("..") ? d[1..] : d).Append("\r\n");
                data = sb.ToString();
                await writer.WriteLineAsync("250 queued");
            }
            else if (cmd == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
            else await writer.WriteLineAsync("250 OK");
        }
        return (rcpt, data);
    }
}
