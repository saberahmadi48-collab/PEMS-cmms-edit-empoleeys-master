using PEMS.Application.Interfaces;

namespace PEMS.Web.BackgroundJobs;

/// <summary>
/// اعلان یادآورهای شخصی نامه (LetterReminder) در موعدشان. جدا از کار روزانه‌ی LetterReminderBackgroundService است (آن برای مهلت ارجاع/پاسخ‌خواهی
/// است و یک‌بار در روز اجرا می‌شود)؛ این‌جا هر چند دقیقه فقط یادآورهای سررسیده را اعلان می‌کند. هر یادآور فقط یک‌بار (NotifiedDate).
/// تنظیم: "LetterReminders:PersonalIntervalMinutes" (پیش‌فرض ۵؛ ۰ = خاموش).
/// </summary>
public class LetterPersonalReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LetterPersonalReminderBackgroundService> _logger;

    public LetterPersonalReminderBackgroundService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<LetterPersonalReminderBackgroundService> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = _configuration.GetValue("LetterReminders:PersonalIntervalMinutes", 5);
        if (minutes <= 0) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<ILetterPersonalReminderService>().DispatchDueAsync(DateTime.Now);
                if (sent > 0) _logger.LogInformation("Personal letter reminders notified: {Count}.", sent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Personal letter reminder dispatch failed.");
            }
        }
    }
}
