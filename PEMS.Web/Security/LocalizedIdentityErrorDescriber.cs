using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Security;

/// <summary>
/// SCRUM-540: the ASP.NET Identity messages (password policy, duplicate user name, wrong password, ...) were always English, also on a Persian
/// page. Each message is looked up in SharedResource under an <c>Id_*</c> key; the English value is Identity's own text, so English pages read
/// as before. The <see cref="IdentityError.Code"/> stays the same, so code that checks codes is not affected.
/// </summary>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer) : IdentityErrorDescriber
{
    private IdentityError Error(IdentityError original, string key, params object?[] args)
        => new() { Code = original.Code, Description = (args.Length == 0 ? localizer[key] : localizer[key, args!]).Value };

    public override IdentityError DefaultError() => Error(base.DefaultError(), "Id_DefaultError");
    public override IdentityError ConcurrencyFailure() => Error(base.ConcurrencyFailure(), "Id_ConcurrencyFailure");
    public override IdentityError PasswordMismatch() => Error(base.PasswordMismatch(), "Id_PasswordMismatch");
    public override IdentityError InvalidToken() => Error(base.InvalidToken(), "Id_InvalidToken");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(base.RecoveryCodeRedemptionFailed(), "Id_RecoveryCodeRedemptionFailed");
    public override IdentityError LoginAlreadyAssociated() => Error(base.LoginAlreadyAssociated(), "Id_LoginAlreadyAssociated");
    public override IdentityError InvalidUserName(string? userName) => Error(base.InvalidUserName(userName), "Id_InvalidUserName", userName);
    public override IdentityError InvalidEmail(string? email) => Error(base.InvalidEmail(email), "Id_InvalidEmail", email);
    public override IdentityError DuplicateUserName(string userName) => Error(base.DuplicateUserName(userName), "Id_DuplicateUserName", userName);
    public override IdentityError DuplicateEmail(string email) => Error(base.DuplicateEmail(email), "Id_DuplicateEmail", email);
    public override IdentityError InvalidRoleName(string? role) => Error(base.InvalidRoleName(role), "Id_InvalidRoleName", role);
    public override IdentityError DuplicateRoleName(string role) => Error(base.DuplicateRoleName(role), "Id_DuplicateRoleName", role);
    public override IdentityError UserAlreadyHasPassword() => Error(base.UserAlreadyHasPassword(), "Id_UserAlreadyHasPassword");
    public override IdentityError UserLockoutNotEnabled() => Error(base.UserLockoutNotEnabled(), "Id_UserLockoutNotEnabled");
    public override IdentityError UserAlreadyInRole(string role) => Error(base.UserAlreadyInRole(role), "Id_UserAlreadyInRole", role);
    public override IdentityError UserNotInRole(string role) => Error(base.UserNotInRole(role), "Id_UserNotInRole", role);
    public override IdentityError PasswordTooShort(int length) => Error(base.PasswordTooShort(length), "Id_PasswordTooShort", length);
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(base.PasswordRequiresUniqueChars(uniqueChars), "Id_PasswordRequiresUniqueChars", uniqueChars);
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(base.PasswordRequiresNonAlphanumeric(), "Id_PasswordRequiresNonAlphanumeric");
    public override IdentityError PasswordRequiresDigit() => Error(base.PasswordRequiresDigit(), "Id_PasswordRequiresDigit");
    public override IdentityError PasswordRequiresLower() => Error(base.PasswordRequiresLower(), "Id_PasswordRequiresLower");
    public override IdentityError PasswordRequiresUpper() => Error(base.PasswordRequiresUpper(), "Id_PasswordRequiresUpper");
}
