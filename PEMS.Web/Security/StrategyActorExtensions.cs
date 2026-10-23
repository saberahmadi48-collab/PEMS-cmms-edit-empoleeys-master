using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Persistence.Context;

namespace PEMS.Web.Security;

/// <summary>
/// بازیگر عملیات RCM همیشه با EmployeeId (از ApplicationUser.EmployeeId) ساخته می‌شود - هرگز UserId با
/// EmployeeId مخلوط نمی‌شود (قرارداد کل کدبیس: GetCurrentEmployeeIdAsync).
/// </summary>
public static class StrategyActorExtensions
{
    public static async Task<StrategyActor> ResolveStrategyActorAsync(this PEMSDbContext context, ICurrentUserService currentUser)
    {
        int? employeeId = null;
        if (currentUser.UserId.HasValue)
            employeeId = await context.Users.Where(u => u.Id == currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();

        return new StrategyActor(employeeId, currentUser.UserName ?? currentUser.FullName, currentUser.Roles);
    }
}

/// <summary>ترجمه‌ی خطاهای کد-محور سرویس به پیام محلی‌شده (کلید Resource = RuleError.Code).</summary>
public static class RuleErrorLocalization
{
    public static string Translate(this IStringLocalizer? localizer, RuleError error)
    {
        if (localizer == null) return error.Code;
        var args = error.Args.Cast<object>().ToArray();
        var localized = localizer[error.Code, args];
        return localized.ResourceNotFound ? error.Code : localized.Value;
    }

    public static List<string> TranslateAll(this IStringLocalizer? localizer, IEnumerable<RuleError> errors)
        => errors.Select(e => localizer.Translate(e)).Distinct().ToList();
}
