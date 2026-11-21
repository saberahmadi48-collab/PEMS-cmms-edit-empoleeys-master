using System.Security.Cryptography;
using System.Text;

namespace PEMS.Application.Common;

/// <summary>
/// قواعد خالص کدهای بازیابی یکبارمصرف حساب مدیر (SCRUM-355): تولید، نرمال‌سازی و هش/بررسی.
/// هر کد ۱۶ نویسه از الفبای ۳۲تایی (۸۰ بیت تصادف رمزنگاری) است، با خط تیره در گروه‌های چهارتایی برای چاپ.
/// ذخیره فقط به‌صورت PBKDF2-SHA256 با نمک تصادفی جدا برای هر کد.
/// </summary>
public static class RecoveryCodeRules
{
    public const int CodesPerSet = 10;
    public const int CodeLength = 16;
    public const int GroupLength = 4;
    public const int SaltLength = 16;
    public const int HashLength = 32;

    // کد ۸۰ بیتی است، پس تکرار بالا لازم نیست؛ این مقدار فقط حمله‌ی آفلاین به نسخه‌ی پشتیبان دیتابیس را کندتر می‌کند
    // و بررسی ۱۰ کد در هر تلاش را سریع نگه می‌دارد.
    public const int Pbkdf2Iterations = 10_000;

    // بدون حروف مبهم (I, O, 0, 1) - دقیقاً ۳۲ نویسه
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>یک مجموعه‌ی کامل کد (بدون تکرار) به شکل نمایشی XXXX-XXXX-XXXX-XXXX.</summary>
    public static IReadOnlyList<string> GenerateSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (set.Count < CodesPerSet) set.Add(GenerateCode());
        return set.ToList();
    }

    public static string GenerateCode()
    {
        var sb = new StringBuilder(CodeLength + CodeLength / GroupLength);
        for (var i = 0; i < CodeLength; i++)
        {
            if (i > 0 && i % GroupLength == 0) sb.Append('-');
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>حروف بزرگ، بدون فاصله و خط تیره؛ نویسه‌های نامعتبر یا طول اشتباه => null.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var sb = new StringBuilder(CodeLength);
        foreach (var ch in code)
        {
            if (ch is '-' or ' ' or '\t') continue;
            var c = char.ToUpperInvariant(ch);
            if (Alphabet.IndexOf(c) < 0) return null;
            sb.Append(c);
        }
        return sb.Length == CodeLength ? sb.ToString() : null;
    }

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    public static byte[] Hash(string normalizedCode, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(normalizedCode), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashLength);

    public static bool Matches(string normalizedCode, byte[] salt, byte[] expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(normalizedCode, salt), expectedHash);
}
