using System.Net;

namespace PEMS.Application.Common;

/// <summary>
/// تنظیمات break-glass روی سرور (SCRUM-356 / ۳۵۳.۳). همه از env/&lt;name&gt;.env می‌آیند (Security:BreakGlass:*).
/// </summary>
public sealed class BreakGlassOptions
{
    /// <summary>مسیر مطلق پوشه‌ای که اپراتور فایل درخواست را در آن می‌گذارد.</summary>
    public string RequestFolder { get; init; } = string.Empty;

    public int WindowMinutes { get; init; } = BreakGlassRules.DefaultWindowMinutes;

    /// <summary>شبکه‌های مدیریتی مجاز علاوه بر localhost.</summary>
    public IReadOnlyList<IPNetwork> ManagementNetworks { get; init; } = Array.Empty<IPNetwork>();

    /// <summary>توکن امضاشده‌ی فروشنده؛ پیش‌فرض خاموش، مشتری باید صریحاً روشن کند.</summary>
    public bool VendorRecoveryEnabled { get; init; }

    /// <summary>کلید عمومی ECDSA P-256 فروشنده (SubjectPublicKeyInfo به Base64).</summary>
    public string? VendorPublicKey { get; init; }

    /// <summary>
    /// اثر انگشت ماشین که توکن باید به آن گره خورده باشد. تا موتور مجوز (licensing) اثر انگشت را فراهم کند، از تنظیمات خوانده می‌شود؛
    /// خالی = مسیر توکن حتی روشن هم هیچ توکنی را نمی‌پذیرد.
    /// </summary>
    public string? MachineFingerprint { get; init; }
}

/// <summary>قواعد خالص break-glass: پنجره‌ی زمانی فایل درخواست و محدودیت شبکه.</summary>
public static class BreakGlassRules
{
    /// <summary>نام ثابت فایل درخواست؛ محتوای فایل مهم نیست.</summary>
    public const string RequestFileName = "pems-recovery.request";

    /// <summary>پیشوند فایل‌های نشانگر توکن‌های مصرف‌شده‌ی فروشنده (در همان پوشه).</summary>
    public const string UsedVendorTokenPrefix = "used-vendor-";

    public const int DefaultWindowMinutes = 15;

    // ساعت سرور و سیستم فایل ممکن است کمی اختلاف داشته باشند
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// زمان شروع پنجره = جدیدترین زمان ایجاد/نوشتن فایل (کپی فایل در ویندوز زمان نوشتن قدیمی را نگه می‌دارد ولی زمان ایجاد تازه است).
    /// </summary>
    public static DateTime WindowStart(DateTime creationUtc, DateTime lastWriteUtc) => creationUtc > lastWriteUtc ? creationUtc : lastWriteUtc;

    public static bool IsWithinWindow(DateTime windowStartUtc, DateTime nowUtc, int windowMinutes) =>
        nowUtc >= windowStartUtc - ClockSkew && nowUtc < windowStartUtc.AddMinutes(windowMinutes);

    /// <summary>
    /// فهرست CIDR جدا شده با کاما (مثلاً "10.20.0.0/16, 192.168.5.0/24"). خالی = فقط localhost. مقدار نامعتبر در راه‌اندازی خطا می‌دهد.
    /// </summary>
    public static IReadOnlyList<IPNetwork> ParseNetworks(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<IPNetwork>();
        var list = new List<IPNetwork>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPNetwork.TryParse(part, out var network))
                throw new InvalidOperationException($"Security:BreakGlass:ManagementNetworks has an invalid network '{part}' (use CIDR, e.g. 10.0.0.0/24).");
            list.Add(network);
        }
        return list;
    }

    /// <summary>
    /// مجاز فقط وقتی که آدرس اتصال localhost یا داخل یک شبکه‌ی مدیریتی باشد و همه‌ی آدرس‌های X-Forwarded-For / Forwarded هم مجاز باشند.
    /// این‌طور یک reverse proxy روی همان سرور (که اتصالش از 127.0.0.1 است) صفحه را به اینترنت باز نمی‌کند؛ جعل این سرآیندها هم فقط سخت‌گیرتر می‌کند.
    /// مقدار ناخوانا در سرآیندها (مثلاً "unknown") => رد.
    /// </summary>
    public static bool IsAddressAllowed(IPAddress? remote, IEnumerable<string> forwardedHeaderValues, IReadOnlyList<IPNetwork> networks)
    {
        if (!IsAllowed(remote, networks)) return false;
        foreach (var value in forwardedHeaderValues)
        {
            foreach (var hop in ForwardedAddresses(value))
            {
                if (!IPAddress.TryParse(hop, out var ip) || !IsAllowed(ip, networks)) return false;
            }
        }
        return true;
    }

    private static bool IsAllowed(IPAddress? ip, IReadOnlyList<IPNetwork> networks)
    {
        if (ip == null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return IPAddress.IsLoopback(ip) || networks.Any(n => n.Contains(ip));
    }

    /// <summary>آدرس‌های یک مقدار X-Forwarded-For ("a, b") یا Forwarded ("for=a;proto=https, for=\"[b]:443\"").</summary>
    public static IEnumerable<string> ForwardedAddresses(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) yield break;
        foreach (var element in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = element;
            if (element.Contains('='))
            {
                var forPair = element.Split(';', StringSplitOptions.TrimEntries)
                    .FirstOrDefault(p => p.StartsWith("for=", StringComparison.OrdinalIgnoreCase));
                if (forPair == null) continue;
                value = forPair[4..];
            }
            yield return StripPort(value.Trim('"'));
        }
    }

    private static string StripPort(string value)
    {
        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end > 0 ? value[1..end] : value;
        }
        // IPv4 با پورت ("1.2.3.4:80"); IPv6 بدون کروشه چند ':' دارد و دست نمی‌خورد
        var colon = value.IndexOf(':');
        return colon > 0 && colon == value.LastIndexOf(':') ? value[..colon] : value;
    }
}
