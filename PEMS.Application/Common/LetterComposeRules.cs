using System.Text.RegularExpressions;
using PEMS.Application.DTOs;

namespace PEMS.Application.Common;

/// <summary>
/// قواعد خالص (بدون DB) فرم نوشتن نامه‌ی رسمی. دو سطح: ValidateStructure (برای ذخیره‌ی پیش‌نویس/ذخیره‌ی خودکار؛ فقط یکپارچگی داده) و
/// ValidateForSubmit (برای ارسال به تایید/ارسال؛ کامل‌بودن). همه‌ی پیام‌ها «کد» هستند و ترجمه در UI انجام می‌شود (Lc_Err_*، Lc_Warn_*).
/// </summary>
public static class LetterComposeRules
{
    public const int MaxSubjectLength = 80;
    public const int MaxReferenceLength = 100;
    public const int MaxRecipients = 500;
    public const int WarnRecipientThreshold = 50;
    public const int MaxAttachmentBytes = 25 * 1024 * 1024;

    public static readonly string[] ComposableTypes = { "Internal", "Outgoing" };
    public static readonly string[] Categories = { "Information", "Request", "Instruction", "Approval", "Recommendation", "Notice", "StatusReport" };
    public static readonly string[] Priorities = { "Low", "Normal", "High", "Urgent" };
    public static readonly string[] Confidentialities = { "Public", "Internal", "Confidential", "Restricted", "HighlyConfidential" };

    /// <summary>رتبه‌ی سطح محرمانگی (Public=0 ... HighlyConfidential=4)؛ مقدار ناشناخته مثل Internal حساب می‌شود.</summary>
    public static int ConfidentialityRank(string? level)
    {
        var i = Array.IndexOf(Confidentialities, level);
        return i < 0 ? 1 : i;
    }

    // پسوندهای مجاز پیوست (فایل اجرایی/اسکریپت هرگز پذیرفته نمی‌شود)
    public static readonly string[] AllowedAttachmentExtensions =
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip", ".dwg", ".dxf" };

    /// <summary>متن ساده‌ی یک HTML برای تشخیص «بدنه خالی است» (تگ و فاصله‌ی سخت حذف می‌شود).</summary>
    public static string PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var noTags = Regex.Replace(html, "<[^>]*>", " ");
        noTags = noTags.Replace("&nbsp;", " ").Replace("&#160;", " ");
        return Regex.Replace(noTags, @"\s+", " ").Trim();
    }

    public static IReadOnlyList<int> AllRecipientIds(LetterComposeInput i)
        => i.ToEmployeeIds.Concat(i.CcEmployeeIds).Concat(i.BccEmployeeIds).ToList();

    public static string? ValidateAttachment(string fileName, long length)
    {
        if (length <= 0) return "AttachmentEmpty";
        if (length > MaxAttachmentBytes) return "AttachmentTooLarge";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return AllowedAttachmentExtensions.Contains(ext) ? null : "AttachmentTypeNotAllowed";
    }

    public static List<LetterComposeIssue> ValidateStructure(LetterComposeInput i)
    {
        var e = new List<LetterComposeIssue>();

        if (!ComposableTypes.Contains(i.LetterType)) e.Add(new("TypeInvalid", "letterType"));
        if (i.Category != null && !Categories.Contains(i.Category)) e.Add(new("CategoryInvalid", "category"));
        if (!Priorities.Contains(i.Priority)) e.Add(new("PriorityInvalid", "priority"));
        if (!Confidentialities.Contains(i.ConfidentialityLevel)) e.Add(new("ConfidentialityInvalid", "confidentialityLevel"));
        if ((i.Subject ?? "").Trim().Length > MaxSubjectLength) e.Add(new("SubjectTooLong", "subject", MaxSubjectLength.ToString()));
        if ((i.ReferenceNumber ?? "").Length > MaxReferenceLength) e.Add(new("ReferenceTooLong", "referenceNumber"));

        var all = AllRecipientIds(i);
        if (all.Count > MaxRecipients) e.Add(new("TooManyRecipients", "recipients", MaxRecipients.ToString()));
        foreach (var dup in all.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))
            e.Add(new("RecipientDuplicate", "recipients", dup.ToString()));

        var responseError = LetterResponseRules.Validate(i.ResponseRequired, i.ResponseDueDate, i.LetterDate);
        if (responseError != null) e.Add(new(i.ResponseDueDate.HasValue ? "ResponseDueBeforeLetterDate" : "ResponseDueRequired", "responseDueDate"));

        if (i.ApproverUserIds.GroupBy(x => x).Any(g => g.Count() > 1)) e.Add(new("ApprovalChainDuplicate", "approvers"));

        return e;
    }

    /// <summary>کامل‌بودن برای ارسال به تایید / ارسال. (شامل همه‌ی خطاهای ساختاری.)</summary>
    public static List<LetterComposeIssue> ValidateForSubmit(LetterComposeInput i, int? authorUserId = null)
    {
        var e = ValidateStructure(i);

        if (string.IsNullOrWhiteSpace(i.Subject)) e.Add(new("SubjectRequired", "subject"));
        if (string.IsNullOrWhiteSpace(i.Category)) e.Add(new("CategoryRequired", "category"));
        if (PlainText(i.Body).Length == 0) e.Add(new("BodyRequired", "body"));

        if (i.LetterType == "Internal" && i.ToEmployeeIds.Count == 0) e.Add(new("RecipientRequired", "to"));
        if (i.LetterType == "Outgoing" && string.IsNullOrWhiteSpace(i.ExternalReceiver)) e.Add(new("ExternalReceiverRequired", "externalReceiver"));

        if (i.ApprovalRequired && i.ApproverUserIds.Count == 0) e.Add(new("ApprovalChainRequired", "approvers"));
        if (authorUserId.HasValue && i.ApproverUserIds.Contains(authorUserId.Value)) e.Add(new("ApproverIsAuthor", "approvers"));

        return e;
    }

    public static List<LetterComposeIssue> StaticWarnings(LetterComposeInput i, int? authorEmployeeId = null)
    {
        var w = new List<LetterComposeIssue>();
        var all = AllRecipientIds(i);
        if (all.Count >= WarnRecipientThreshold) w.Add(new("ManyRecipients", "recipients", all.Count.ToString()));
        if (authorEmployeeId.HasValue && all.Contains(authorEmployeeId.Value)) w.Add(new("SenderInRecipients", "recipients"));
        return w;
    }
}
