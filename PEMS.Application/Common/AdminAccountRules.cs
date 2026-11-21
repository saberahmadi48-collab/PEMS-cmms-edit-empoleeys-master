using System.Security.Cryptography;

namespace PEMS.Application.Common;

/// <summary>قواعد خالص بازیابی حساب مدیر توسط مدیر دیگر (SCRUM-354): انقضای رمز موقت و تولید رمز موقتِ تصادفی.</summary>
public static class AdminAccountRules
{
    /// <summary>عمر رمز موقت بعد از ریست.</summary>
    public const int TemporaryPasswordLifetimeHours = 24;

    public const int TemporaryPasswordLength = 16;

    // حروف مبهم (I, l, O, 0, 1) عمداً حذف شده‌اند تا رمز موقت هنگام چاپ/خواندن اشتباه نشود
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "@#$%&*?!";

    /// <summary>رمز موقت منقضی‌شده: کاربر هنوز باید رمز را عوض کند و مهلت ثبت‌شده گذشته است.</summary>
    public static bool IsTemporaryPasswordExpired(bool mustChangePassword, DateTime? expiresAtUtc, DateTime nowUtc)
        => mustChangePassword && expiresAtUtc.HasValue && expiresAtUtc.Value <= nowUtc;

    public static DateTime ExpiryFrom(DateTime nowUtc) => nowUtc.AddHours(TemporaryPasswordLifetimeHours);

    /// <summary>
    /// رمز موقت تصادفی با منبع رمزنگاری (RandomNumberGenerator). هیچ مقدار ثابت یا مشتق‌شده از کاربر ندارد و حداقل دو حرف از هر دسته
    /// (بزرگ، کوچک، رقم، نماد) دارد تا با هر سیاست معمول رمز سازگار باشد.
    /// </summary>
    public static string GenerateTemporaryPassword()
    {
        var chars = new List<char>(TemporaryPasswordLength);
        foreach (var set in new[] { Upper, Lower, Digits, Symbols })
        {
            chars.Add(Pick(set));
            chars.Add(Pick(set));
        }

        var all = Upper + Lower + Digits + Symbols;
        while (chars.Count < TemporaryPasswordLength) chars.Add(Pick(all));

        // Fisher-Yates تا جای حروف دسته‌ها قابل پیش‌بینی نباشد
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
