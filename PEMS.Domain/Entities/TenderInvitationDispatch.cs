using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-429: one tender invitation email to one invited vendor (the RFQ send log): recipient, the text exactly as sent, result,
/// attempts and the last error. Rows are never deleted; a resend adds a new row.
/// </summary>
public class TenderInvitationDispatch : BaseEntity
{
    public int TenderInvitationDispatchId { get; set; }

    public int TenderId { get; set; }
    public Tender? Tender { get; set; }

    public int VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    /// <summary>Recipient address at queue time (vendor contact email); empty when the vendor has none (Skipped).</summary>
    public string ToAddress { get; set; } = string.Empty;
    public string? ToName { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary><see cref="TenderInvitationStatuses"/></summary>
    public string Status { get; set; } = TenderInvitationStatuses.Queued;

    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public string? QueuedByUserName { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.Now;
    public DateTime? SentAt { get; set; }
}

public static class TenderInvitationStatuses
{
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    /// <summary>Not sent: the vendor has no contact email.</summary>
    public const string Skipped = "Skipped";
}
