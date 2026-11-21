using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

/// <summary>
/// گام مشترک هر سه مسیر بازیابی حساب مدیر (SCRUM-354/355/356): ریست MFA برای ثبت‌نام مجدد و باز کردن قفل حساب.
/// کلید authenticator و کدهای بازیابی خودِ Identity حذف، passkeyها غیرفعال (نه حذف؛ سابقه می‌ماند) و پرچم MfaEnrollmentRequired روشن می‌شود.
/// کدهای بازیابی PEMS (SCRUM-355) دست نمی‌خورند: متعلق به صاحب حساب‌اند و مستقل از دستگاه MFA هستند.
/// </summary>
internal static class AdminMfaReset
{
    // نام ارائه‌دهنده/توکن‌های ذخیره‌ی خودِ Identity برای authenticator (TOTP) و کدهای بازیابی آن
    private const string UserStoreProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyToken = "AuthenticatorKey";
    private const string RecoveryCodesToken = "RecoveryCodes";

    /// <summary>تغییرات قبلی روی <paramref name="target"/> (مثلاً MustChangePassword) با همین UpdateAsync ذخیره می‌شوند.</summary>
    public static async Task ApplyAsync(PEMSDbContext ctx, UserManager<ApplicationUser> users, ApplicationUser target, CancellationToken ct)
    {
        target.MfaEnrollmentRequired = true;
        target.TwoFactorEnabled = false;
        Require(await users.UpdateAsync(target));

        // مدیری که قفل شده همان کسی است که بازیابی می‌خواهد
        Require(await users.SetLockoutEndDateAsync(target, null));
        Require(await users.ResetAccessFailedCountAsync(target));

        Require(await users.RemoveAuthenticationTokenAsync(target, UserStoreProvider, AuthenticatorKeyToken));
        Require(await users.RemoveAuthenticationTokenAsync(target, UserStoreProvider, RecoveryCodesToken));
        var passkeys = await ctx.WebAuthnCredentials.Where(c => c.UserId == target.Id && c.IsActive).ToListAsync(ct);
        foreach (var key in passkeys) key.IsActive = false;
        if (passkeys.Count > 0) await ctx.SaveChangesAsync(ct);
    }

    public static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Identity update failed during admin recovery.");
    }
}
