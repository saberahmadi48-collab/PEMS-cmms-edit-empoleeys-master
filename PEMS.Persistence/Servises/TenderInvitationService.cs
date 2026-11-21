using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-429: دعوت‌نامه‌ی مناقصه با ایمیل سازمانی. برای هر فروشنده‌ی دعوت‌شده یک ردیف سابقه (متن دقیق ارسالی، نتیجه، تعداد تلاش،
/// آخرین خطا) ثبت و ارسال در پس‌زمینه انجام می‌شود؛ فروشنده‌ی بدون ایمیل «ردشده» ثبت می‌شود. ردیف‌ها پاک نمی‌شوند؛ ارسال دوباره ردیف تازه است.
/// </summary>
public class TenderInvitationService : ITenderInvitationService
{
    /// <summary>Attempts before a row stays Failed (the job runner retries while fewer).</summary>
    public const int MaxAttempts = 3;

    private static readonly string[] SendableStatuses = { "Draft", "Open" };

    private readonly PEMSDbContext _context;
    private readonly IEmailSender _email;

    public TenderInvitationService(PEMSDbContext context, IEmailSender email)
    {
        _context = context;
        _email = email;
    }

    public bool EmailEnabled => _email.IsEnabled;

    public async Task<TenderInvitationQueueResult> QueueAsync(int tenderId, TenderInvitationTemplate template, string? userName, IReadOnlyCollection<int>? vendorIds = null)
    {
        if (!_email.IsEnabled) return TenderInvitationQueueResult.Fail("Tinv_Err_EmailOff");

        var tender = await _context.Tenders.AsNoTracking()
            .Include(t => t.ProcurementPackage)
            .Include(t => t.InvitedVendors.Where(v => v.IsActive)).ThenInclude(v => v.Vendor)
            .FirstOrDefaultAsync(t => t.TenderId == tenderId && t.IsActive);
        if (tender == null) return TenderInvitationQueueResult.Fail("Tinv_Err_NotFound");
        if (!SendableStatuses.Contains(tender.Status)) return TenderInvitationQueueResult.Fail("Tinv_Err_Status");

        var invited = tender.InvitedVendors.Where(v => v.Vendor != null).ToList();
        if (vendorIds != null)
            invited = invited.Where(v => vendorIds.Contains(v.VendorId)).ToList();
        else
        {
            var history = await _context.TenderInvitationDispatches.AsNoTracking()
                .Where(d => d.TenderId == tenderId)
                .Select(d => new { d.VendorId, d.Status }).ToListAsync();
            var alreadySent = history.Where(h => h.Status is TenderInvitationStatuses.Sent or TenderInvitationStatuses.Queued).Select(h => h.VendorId).ToHashSet();
            var alreadySkipped = history.Where(h => h.Status == TenderInvitationStatuses.Skipped).Select(h => h.VendorId).ToHashSet();
            // a vendor skipped before is sent now only if it has an email meanwhile; otherwise no new "skipped" row each time
            invited = invited.Where(v => !alreadySent.Contains(v.VendorId)
                && !(alreadySkipped.Contains(v.VendorId) && !IsAddress(v.Vendor!.ContactEmail?.Trim() ?? string.Empty))).ToList();
        }
        if (invited.Count == 0) return TenderInvitationQueueResult.Fail("Tinv_Err_NothingToSend");

        var rows = new List<TenderInvitationDispatch>();
        var skipped = 0;
        foreach (var inv in invited)
        {
            var vendor = inv.Vendor!;
            var address = vendor.ContactEmail?.Trim() ?? string.Empty;
            var hasAddress = IsAddress(address);
            if (!hasAddress) skipped++;
            rows.Add(new TenderInvitationDispatch
            {
                TenderId = tenderId,
                VendorId = vendor.VendorId,
                ToAddress = hasAddress ? address : string.Empty,
                ToName = vendor.ContactName ?? vendor.Name,
                Subject = Fill(template.Subject, tender, vendor, template),
                Body = Fill(template.Body, tender, vendor, template),
                Status = hasAddress ? TenderInvitationStatuses.Queued : TenderInvitationStatuses.Skipped,
                LastError = hasAddress ? null : "Tinv_Err_NoVendorEmail",
                QueuedByUserName = userName,
                QueuedAt = DateTime.Now,
            });
        }
        _context.TenderInvitationDispatches.AddRange(rows);
        // one readable audit entry on the tender for the send action (the rows themselves are audited automatically too)
        _context.AuditLogs.Add(new AuditLog
        {
            EntityName = nameof(Tender), EntityKeyValue = tenderId.ToString(CultureInfo.InvariantCulture),
            Action = vendorIds == null ? "SendInvitations" : "ResendInvitations",
            ChangedFieldsSummary = $"Queued: {rows.Count - skipped}\nSkipped (no email): {skipped}\nVendors: {string.Join(", ", rows.Select(r => r.VendorId))}",
            UserName = userName, Timestamp = DateTime.Now
        });
        await _context.SaveChangesAsync();

        return new TenderInvitationQueueResult(
            rows.Where(r => r.Status == TenderInvitationStatuses.Queued).Select(r => r.TenderInvitationDispatchId).ToList(), skipped, null);
    }

    public async Task SendAsync(int dispatchId)
    {
        var row = await _context.TenderInvitationDispatches.FirstOrDefaultAsync(d => d.TenderInvitationDispatchId == dispatchId);
        if (row == null || row.Status is TenderInvitationStatuses.Sent or TenderInvitationStatuses.Skipped) return;   // idempotent

        row.Attempts++;
        try
        {
            await _email.SendAsync(new EmailMessage(row.ToAddress, row.ToName, row.Subject, row.Body));
            row.Status = TenderInvitationStatuses.Sent;
            row.SentAt = DateTime.Now;
            row.LastError = null;
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            row.Status = TenderInvitationStatuses.Failed;
            row.LastError = Truncate(ex.Message, 1000);
            await _context.SaveChangesAsync();
            if (row.Attempts < MaxAttempts) throw;   // the job runner retries; after the last attempt the row stays Failed (resend by hand)
        }
    }

    public async Task<List<TenderInvitationDispatchDto>> GetLogAsync(int tenderId) =>
        await _context.TenderInvitationDispatches.AsNoTracking()
            .Where(d => d.TenderId == tenderId)
            .OrderByDescending(d => d.QueuedAt).ThenByDescending(d => d.TenderInvitationDispatchId)
            .Select(d => new TenderInvitationDispatchDto(d.TenderInvitationDispatchId, d.VendorId, d.Vendor != null ? d.Vendor.Name : "",
                d.ToAddress, d.Subject, d.Status, d.Attempts, d.LastError, d.QueuedByUserName, d.QueuedAt, d.SentAt))
            .ToListAsync();

    // ------------------------------------------------------------------ text

    private static string Fill(string text, Tender t, Vendor v, TenderInvitationTemplate template)
    {
        var requirements = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { t.TechnicalRequirements, t.CommercialRequirements, t.InspectionRequirements, t.ContractualRequirements }
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()));
        return text
            .Replace("{Vendor}", v.ContactName ?? v.Name)
            .Replace("{TenderNumber}", t.TenderNumber)
            .Replace("{Title}", t.Title)
            .Replace("{Package}", t.ProcurementPackage?.PackageNumber ?? string.Empty)
            .Replace("{IssueDate}", FormatDate(t.IssueDate, template.Culture))
            .Replace("{Deadline}", FormatDate(t.ResponseDeadline, template.Culture))
            .Replace("{Requirements}", requirements.Length == 0 ? "-" : requirements)
            .Replace("{Company}", template.Company ?? string.Empty);
    }

    /// <summary>Jalali date with Latin digits on a Persian page (owner decision 2026-10-27 for dates), ISO otherwise.</summary>
    public static string FormatDate(DateTime? date, CultureInfo culture)
    {
        if (date is not DateTime d) return "-";
        if (culture.TwoLetterISOLanguageName == "fa")
        {
            var pc = new PersianCalendar();
            return $"{pc.GetYear(d):0000}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00}";
        }
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool IsAddress(string s) =>
        s.Length is > 3 and <= 256 && s.IndexOf('@') > 0 && s.IndexOf('@') == s.LastIndexOf('@') && s.IndexOf('.', s.IndexOf('@')) > s.IndexOf('@') + 1 && !s.Any(char.IsWhiteSpace);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
