using PEMS.Application.DTOs;

namespace PEMS.Application.Common;

/// <summary>
/// قواعد خالص (بدون پایگاه داده) ثبت مکاتبه‌ی وارده در DCC. خطاها کد پایدار + نام فیلد دارند؛ متن نمایشی ترجمه‌شده در لایه‌ی وب با کلید
/// <c>Dcc_Err_{Code}</c> ساخته می‌شود (قبلاً پیام‌ها مستقیم در Controller به فارسی نوشته می‌شد و هیچ فیلدی علامت نمی‌خورد).
/// </summary>
public static class IncomingCorrespondenceRules
{
    // موضوع نامه‌ی بیرونی بلندتر از موضوع نامه‌ی داخلی (۸۰) است
    public const int MaxSubjectLength = 300;
    public const int MaxFiles = 10;
    public const int MaxLinks = 20;
    public const int MaxCc = 20;
    public const int MaxSummaryLength = 4000;
    public const int MaxInstructionLength = 1000;
    public const int MaxSenderReferenceLength = 100;
    public const int MaxPartyNameLength = 200;
    public const long MaxTotalBytes = 100L * 1024 * 1024;

    public static readonly string[] Channels = { "OfficialLetter", "Email", "Portal", "Hand" };
    public static readonly string[] PartyTypes = { "Client", "Consultant", "Contractor", "Vendor" };
    public static readonly string[] FileRoles = { "Original", "Attachment", "EmailCopy" };
    public static readonly string[] LinkTypes = { "EngineeringDocument", "VdrItem", "MaterialRequisition", "Transmittal" };

    public static readonly string[] AllowedExtensions =
        LetterComposeRules.AllowedAttachmentExtensions.Concat(new[] { ".tif", ".tiff", ".eml", ".msg" }).ToArray();

    /// <summary>First bytes fit the extension; the signature table is the shared upload allowlist (SCRUM-202, <see cref="Uploads.UploadPolicy"/>).</summary>
    public static bool SignatureMatches(string fileName, ReadOnlySpan<byte> head) =>
        Uploads.UploadPolicy.SignatureMatches(Uploads.UploadPolicy.ExtensionOf(fileName), head);

    public static string? ValidateFile(string fileName, long length)
    {
        if (length <= 0) return "FileEmpty";
        if (length > LetterComposeRules.MaxAttachmentBytes) return "FileTooLarge";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return AllowedExtensions.Contains(ext) ? null : "FileTypeNotAllowed";
    }

    public static List<LetterComposeIssue> ValidateStructure(IncomingRegistrationInput i, DateTime today)
    {
        var e = new List<LetterComposeIssue>();

        if (!i.ProjectId.HasValue) e.Add(new("ProjectRequired", "projectId"));

        if (!i.CorrespondentPartyId.HasValue)
        {
            var name = (i.NewPartyName ?? "").Trim();
            if (name.Length == 0) e.Add(new("PartyRequired", "party"));
            else if (name.Length > MaxPartyNameLength) e.Add(new("PartyNameTooLong", "party"));
            else if (!PartyTypes.Contains(i.NewPartyType ?? "")) e.Add(new("PartyTypeInvalid", "party"));
        }

        var subject = (i.Subject ?? "").Trim();
        if (subject.Length == 0) e.Add(new("SubjectRequired", "subject"));
        else if (subject.Length > MaxSubjectLength) e.Add(new("SubjectTooLong", "subject", MaxSubjectLength.ToString()));

        if (!Channels.Contains(i.ReceiptChannel)) e.Add(new("ChannelInvalid", "receiptChannel"));
        var reference = (i.SenderReference ?? "").Trim();
        if (reference.Length > MaxSenderReferenceLength) e.Add(new("SenderReferenceTooLong", "senderReference"));
        else if (reference.Length == 0 && i.ReceiptChannel == "OfficialLetter") e.Add(new("SenderReferenceRequired", "senderReference"));

        var received = (i.ReceivedDate ?? today).Date;
        if (!i.DocumentDate.HasValue) e.Add(new("DocumentDateRequired", "documentDate"));
        else if (i.DocumentDate.Value.Date > today.Date) e.Add(new("DocumentDateFuture", "documentDate"));
        if (received > today.Date) e.Add(new("ReceivedDateFuture", "receivedDate"));
        else if (i.DocumentDate.HasValue && i.DocumentDate.Value.Date <= today.Date && received < i.DocumentDate.Value.Date)
            e.Add(new("ReceivedBeforeDocument", "receivedDate"));

        if (!LetterComposeRules.Priorities.Contains(i.Priority)) e.Add(new("PriorityInvalid", "priority"));
        if (i.Category != null && !LetterComposeRules.Categories.Contains(i.Category)) e.Add(new("CategoryInvalid", "category"));
        if (!LetterComposeRules.Confidentialities.Contains(i.ConfidentialityLevel)) e.Add(new("ConfidentialityInvalid", "confidentialityLevel"));
        if ((i.Summary ?? "").Length > MaxSummaryLength) e.Add(new("SummaryTooLong", "summary", MaxSummaryLength.ToString()));

        if (i.ResponseRequired)
        {
            if (!i.ResponseDueDate.HasValue) e.Add(new("ResponseDueRequired", "responseDueDate"));
            else if (LetterResponseRules.Validate(true, i.ResponseDueDate, received) != null) e.Add(new("ResponseDueBeforeReceived", "responseDueDate"));
            if (!i.OwnerEmployeeId.HasValue) e.Add(new("OwnerRequired", "ownerEmployeeId"));
        }
        if ((i.Instruction ?? "").Length > MaxInstructionLength) e.Add(new("InstructionTooLong", "instruction", MaxInstructionLength.ToString()));
        if (i.CcEmployeeIds.Distinct().Count() > MaxCc) e.Add(new("TooManyCc", "ccEmployeeIds", MaxCc.ToString()));

        if (i.Links.Count > MaxLinks) e.Add(new("TooManyLinks", "links", MaxLinks.ToString()));
        if (i.Links.Any(l => !LinkTypes.Contains(l.LinkType) || l.TargetId <= 0)) e.Add(new("LinkInvalid", "links"));

        if (i.Files.Count > MaxFiles) e.Add(new("TooManyFiles", "files", MaxFiles.ToString()));
        if (i.Files.Sum(f => f.Length) > MaxTotalBytes) e.Add(new("FilesTooLarge", "files"));
        foreach (var f in i.Files)
        {
            if (!FileRoles.Contains(f.Role)) { e.Add(new("FileRoleInvalid", "files", f.FileName)); continue; }
            var error = ValidateFile(f.FileName, f.Length);
            if (error != null) e.Add(new(error, "files", f.FileName));
        }
        var originals = i.Files.Count(f => f.Role == "Original");
        if (originals == 0) e.Add(new("OriginalRequired", "files"));
        else if (originals > 1) e.Add(new("OneOriginalOnly", "files"));

        return e;
    }
}
