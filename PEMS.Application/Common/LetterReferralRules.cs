using System.Text.RegularExpressions;

namespace PEMS.Application.Common;

/// <summary>قواعد خالص ارجاع در قالب نامه: جداکردن دستورالعمل ارجاع از نقل نامه‌ی اصلی، و تشخیص ارجاع‌هایی که با پاسخ بسته می‌شوند.</summary>
public static class LetterReferralRules
{
    public const int MaxInstructionLength = 1000;

    /// <summary>
    /// متن ساده‌ی دستورالعمل ارجاع: بدنه‌ی نامه بدون بخش نقل‌شده‌ی (blockquote) نامه‌ی اصلی. بدنه‌ی خالی/فقط نقل = بدون دستورالعمل (null).
    /// </summary>
    public static string? InstructionText(string? bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml)) return null;
        var withoutQuote = Regex.Replace(bodyHtml, @"<blockquote\b.*?</blockquote>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var text = LetterComposeRules.PlainText(withoutQuote);
        if (text.Length == 0) return null;
        return text.Length > MaxInstructionLength ? text[..MaxInstructionLength].TrimEnd() : text;
    }

    /// <summary>
    /// ارجاعی که با پاسخ بسته می‌شود: ارجاعِ باز و متعلق به خودِ پاسخ‌دهنده که یا روی همان نامه‌ای است که به آن پاسخ داده می‌شود
    /// (referralOnLetterId) یا با همان نامه صادر شده است (referralLetterId، یعنی پاسخ به نامه‌ی ارجاعی).
    /// </summary>
    public static bool ClosesOnReply(int? referredToEmployeeId, string status, int referralOnLetterId, int? referralLetterId, int? replierEmployeeId, int repliedToLetterId)
        => replierEmployeeId.HasValue && referredToEmployeeId == replierEmployeeId && status != "Completed"
           && (referralOnLetterId == repliedToLetterId || referralLetterId == repliedToLetterId);
}
