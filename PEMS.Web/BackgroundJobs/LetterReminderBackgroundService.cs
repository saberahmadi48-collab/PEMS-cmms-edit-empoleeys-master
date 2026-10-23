using Microsoft.Extensions.Options;
using PEMS.Application.Interfaces;

namespace PEMS.Web.BackgroundJobs;

/// <summary>تنظیمات appsettings: "LetterReminders": { "Enabled": false, "RunAtLocalTime": "07:30" }. پیش‌فرض خاموش است.</summary>
public class LetterReminderOptions
{
    public bool Enabled { get; set; }

    // ساعت اجرای روزانه به وقت محلی سرور، قالب HH:mm
    public string RunAtLocalTime { get; set; } = "07:30";

    // آرشیو خودکار نامه‌های ارسال‌شده/بسته‌شده‌ای که این‌قدر سال هیچ فعالیتی نداشته‌اند (۰ = خاموش، پیش‌فرض). مقدار پیشنهادی طبق سند: ۲.
    // مستقل از Enabled (یادآوری) است، ولی هر دو در همین اجرای روزانه انجام می‌شوند.
    public int AutoArchiveAfterYears { get; set; }
}

public static class LetterReminderSchedule
{
    public static readonly TimeSpan DefaultTime = new(7, 30, 0);

    /// <summary>مقدار نامعتبر به ساعت پیش‌فرض برمی‌گردد (تنظیم اشتباه سرویس را از کار نمی‌اندازد).</summary>
    public static TimeSpan ParseTime(string? text)
        => TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var t)
           && t >= TimeSpan.Zero && t < TimeSpan.FromHours(24)
            ? t
            : DefaultTime;

    /// <summary>نزدیک‌ترین زمان اجرای آینده: امروز اگر ساعت هنوز نرسیده، وگرنه فردا.</summary>
    public static DateTime NextRun(DateTime now, TimeSpan timeOfDay)
    {
        var candidate = now.Date + timeOfDay;
        return candidate > now ? candidate : candidate.AddDays(1);
    }
}

/// <summary>
/// اجرای روزانه‌ی ILetterReminderService. در استارت چیزی نمی‌فرستد؛ فقط در ساعت تنظیم‌شده. وقتی Enabled=false است
/// فوراً تمام می‌شود (تست‌ها و محیط‌هایی که یادآوری نمی‌خواهند). منطق و ضد تکرار در سرویس است، نه این حلقه.
/// </summary>
public class LetterReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<LetterReminderOptions> _options;
    private readonly ILogger<LetterReminderBackgroundService> _logger;

    public LetterReminderBackgroundService(IServiceScopeFactory scopes, IOptions<LetterReminderOptions> options,
        ILogger<LetterReminderBackgroundService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (!options.Enabled && options.AutoArchiveAfterYears <= 0)
        {
            _logger.LogInformation("Letter daily job is disabled (LetterReminders:Enabled=false and AutoArchiveAfterYears=0).");
            return;
        }

        var time = LetterReminderSchedule.ParseTime(options.RunAtLocalTime);
        _logger.LogInformation("Letter reminders enabled; daily at {Time}.", time);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var wait = LetterReminderSchedule.NextRun(DateTime.Now, time) - DateTime.Now;
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            IServiceScope scope;
            try
            {
                scope = _scopes.CreateScope();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Letter daily job could not create a service scope.");
                continue;
            }

            using var _ = scope;

            // هر مرحله جدا خطا را می‌گیرد: شکست یادآوری، آرشیو همان روز را (و هیچ‌کدام حلقه‌ی روزهای بعد را) نمی‌کشد
            if (options.Enabled)
            {
                try
                {
                    var service = scope.ServiceProvider.GetRequiredService<ILetterReminderService>();
                    var result = await service.SendDueRemindersAsync();
                    _logger.LogInformation("Letter reminders sent: {Referral} referral, {Response} response, {Skipped} duplicate(s) skipped, {Failed} failed.",
                        result.ReferralReminders, result.ResponseReminders, result.SkippedDuplicates, result.Failed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Letter reminder run failed.");
                }
            }

            if (options.AutoArchiveAfterYears > 0)
            {
                try
                {
                    var archive = scope.ServiceProvider.GetRequiredService<ILetterComplianceService>();
                    var count = await archive.ArchiveInactiveAsync(DateTime.Now, options.AutoArchiveAfterYears);
                    _logger.LogInformation("Letters auto-archived after {Years} year(s) of inactivity: {Count}.", options.AutoArchiveAfterYears, count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Letter auto-archive run failed.");
                }
            }
        }
    }
}
