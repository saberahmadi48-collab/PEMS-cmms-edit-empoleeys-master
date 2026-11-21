namespace PEMS.Application.Common;

/// <summary>
/// قاعده‌ی واحد دسترسی به یک نامه (مرجع واحد برای صفحه‌ی جزئیات، PDF، خروجی انطباق و فیلتر فهرست).
///   - «همه‌چیز را ببیند» (canSeeAll = SystemAdmin یا Correspondence/Approve): همیشه.
///   - نویسنده و اعضای زنجیره‌ی تایید: همیشه (پیش‌نویس و مراحل تایید را هم می‌بینند).
///   - هر کس دیگر: فقط وقتی نامه از مرحله‌ی «پیش از ارسال» گذشته باشد؛ و آن‌گاه اگر سطح Public/Internal است یا او گیرنده
///     (To/Cc/Bcc) یا ارجاع‌گیرنده‌ی نامه است.
/// پیش‌نویس، در انتظار تایید، برگشت‌خورده، تاییدشده‌ی ارسال‌نشده و ردشده برای دیگران نامرئی‌اند (قبلاً هر کاربر ماژول،
/// پیش‌نویس Public/Internal دیگران را می‌دید و نامه‌ی محرمانه را با آدرس مستقیم باز می‌کرد).
/// </summary>
public static class LetterAccessRules
{
    public static readonly string[] PreSendStatuses = { "Draft", "PendingApproval", "ReturnedForRevision", "Approved", "Rejected" };

    public static bool IsPreSend(string status) => PreSendStatuses.Contains(status);

    public static bool IsRestrictedLevel(string level) => level is not ("Public" or "Internal");

    public static bool CanView(LetterAccessFacts f)
    {
        if (f.ViewerCanSeeAll || f.IsAuthor || f.IsChainApprover) return true;
        if (IsPreSend(f.Status)) return false;
        return !IsRestrictedLevel(f.ConfidentialityLevel) || f.IsRecipient || f.IsReferralTarget;
    }
}

public sealed record LetterAccessFacts(string Status, string ConfidentialityLevel, bool IsAuthor, bool IsRecipient,
    bool IsReferralTarget, bool IsChainApprover, bool ViewerCanSeeAll);
