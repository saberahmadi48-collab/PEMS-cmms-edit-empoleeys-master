using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PEMS.Web.Security;

/// <summary>
/// دروازه‌ی موقت دسترسی P5 (توصیه‌های تأمین): تا زمانی که Migration تأییدشده‌ی جدول‌های P5 روی دیتابیس مقصد اعمال نشده،
/// نباید هیچ مسیر HTTP یا لینک منویی به آن برسد. پیش‌فرض «غیرفعال» است: نبودن کلید یا مقدار نامعتبر = غیرفعال (Fail-Closed).
/// این دروازه مجوزها را تغییر نمی‌دهد و جایگزین آن‌ها نیست؛ فعال‌سازی فقط با تنظیم صریح
/// Features:ReplenishmentRecommendations:Enabled=true پس از استقرار تأییدشده‌ی Migration انجام می‌شود (نه با دادن مجوز).
/// </summary>
public interface IReplenishmentAvailability
{
    bool IsEnabled { get; }
}

public sealed class ReplenishmentAvailability : IReplenishmentAvailability
{
    public const string ConfigurationKey = "Features:ReplenishmentRecommendations:Enabled";

    private readonly IConfiguration _configuration;

    public ReplenishmentAvailability(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // فقط عبارت دقیقاً «true» فعال می‌کند؛ نبودن کلید، مقدار خالی یا هر مقدار نامعتبر ⇒ غیرفعال
    public bool IsEnabled => bool.TryParse(_configuration[ConfigurationKey], out var enabled) && enabled;
}

/// <summary>
/// فیلتر مجوزدهی در سطح Controller: وقتی P5 غیرفعال است با 404 کوتاه‌می‌شود، قبل از هر اکشن، سرویس P5، کوئری دیتابیس یا بررسی/ثبت مجوز.
/// ترتیب اجرا: فیلترهای سراسری (AuthorizeFilter و PasswordChangeGateFilter با Order=0 و Scope=Global) پیش از این فیلتر
/// (Order=0 و Scope=Controller) اجرا می‌شوند، پس رفتار احراز هویت و تغییر رمز عبور دست‌نخورده می‌ماند؛ و این فیلتر پیش از
/// ValidateAntiForgeryToken (Order=1000) اجرا می‌شود. SystemAdmin هم از آن عبور نمی‌کند (دروازه مستقل از مجوز است).
/// اگر سرویس دسترس‌پذیری ثبت نشده باشد نیز Fail-Closed است.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireReplenishmentAvailableAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var availability = context.HttpContext.RequestServices.GetService(typeof(IReplenishmentAvailability)) as IReplenishmentAvailability;

        if (availability?.IsEnabled != true)
            context.Result = new NotFoundResult();

        return Task.CompletedTask;
    }
}
