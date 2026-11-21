using System.Globalization;

namespace PEMS.Application.Interfaces;

/// <summary>
/// The invitation text with placeholders, in the language of the person who sends it (texts are inputs: the page supplies them from
/// its resources). Placeholders: {Vendor} {TenderNumber} {Title} {Package} {IssueDate} {Deadline} {Requirements} {Company}.
/// </summary>
public sealed record TenderInvitationTemplate(string Subject, string Body, CultureInfo Culture, string? Company);

/// <summary>Result of queueing: the rows to send in the background, and how many vendors had no email.</summary>
public sealed record TenderInvitationQueueResult(IReadOnlyList<int> QueuedDispatchIds, int SkippedWithoutEmail, string? Error)
{
    public static TenderInvitationQueueResult Fail(string error) => new(Array.Empty<int>(), 0, error);
}

public sealed record TenderInvitationDispatchDto(
    int Id, int VendorId, string VendorName, string ToAddress, string Subject, string Status, int Attempts, string? LastError,
    string? QueuedBy, DateTime QueuedAt, DateTime? SentAt);

/// <summary>SCRUM-429: tender invitations (RFQ) by the organisation's email, with a send log.</summary>
public interface ITenderInvitationService
{
    bool EmailEnabled { get; }

    /// <summary>
    /// Writes one log row per invited vendor (Queued, or Skipped when the vendor has no email). Without <paramref name="vendorIds"/>
    /// every invited vendor that has not been sent an invitation yet; with it exactly those vendors (resend). The caller starts
    /// <see cref="SendAsync"/> for each queued row. Errors are resource keys.
    /// </summary>
    Task<TenderInvitationQueueResult> QueueAsync(int tenderId, TenderInvitationTemplate template, string? userName, IReadOnlyCollection<int>? vendorIds = null);

    /// <summary>Background job: sends one queued row and records the result; throws while retries remain so the job runner retries.</summary>
    Task SendAsync(int dispatchId);

    Task<List<TenderInvitationDispatchDto>> GetLogAsync(int tenderId);
}
