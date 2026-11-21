using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Web.Resources;

namespace PEMS.Web.Security;

/// <summary>
/// Password history (SCRUM-412 / #382). A new password must differ from the current one and from the last
/// <see cref="Count"/> replaced ones, on every path that sets a password: the forced first change, a voluntary change,
/// the administrator reset, the second-administrator reset, a recovery code and break-glass. One mechanism, no per-controller code:
/// <see cref="PasswordHistoryValidator"/> refuses the password, <see cref="PemsUserManager"/> records the replaced hash.
/// Setting: Authentication:Password:HistoryCount (0 turns the rule off).
/// </summary>
public sealed class PasswordHistoryOptions
{
    public int Count { get; set; } = 5;
}

/// <summary>
/// Switches the history rule off for the current async flow. Only the development password reset uses it: its job is to put
/// the documented seed password back, whatever the history says. Never used on a request path.
/// </summary>
public static class PasswordHistoryBypass
{
    private static readonly AsyncLocal<bool> Active = new();

    public static bool IsActive => Active.Value;

    public static IDisposable Begin()
    {
        var previous = Active.Value;
        Active.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}

public sealed class PasswordHistoryValidator(PEMSDbContext ctx, IOptions<PasswordHistoryOptions> options, IStringLocalizer<SharedResource> Localizer)
    : IPasswordValidator<ApplicationUser>
{
    public const string ErrorCode = "PasswordReused";

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var count = options.Value.Count;

        // off, bypassed, nothing to compare (a new user has no id and no history)
        if (count <= 0 || PasswordHistoryBypass.IsActive || string.IsNullOrEmpty(password) || user.Id == 0)
            return IdentityResult.Success;

        var hashes = new List<string>();
        if (!string.IsNullOrEmpty(user.PasswordHash))
            hashes.Add(user.PasswordHash);

        hashes.AddRange(await ctx.PasswordHistory.AsNoTracking()
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.ReplacedAtUtc).ThenByDescending(h => h.PasswordHistoryEntryId)
            .Take(count)
            .Select(h => h.PasswordHash)
            .ToListAsync());

        foreach (var hash in hashes)
        {
            if (manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed)
                return IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = Localizer["Password_Reused", count] });
        }

        return IdentityResult.Success;
    }
}

/// <summary>Records the hash a password change or reset replaces, so <see cref="PasswordHistoryValidator"/> can compare against it later.</summary>
public sealed class PemsUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger,
    PEMSDbContext ctx,
    IOptions<PasswordHistoryOptions> history)
    : UserManager<ApplicationUser>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
{
    public override async Task<IdentityResult> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword)
    {
        var replacedHash = user.PasswordHash;
        var result = await base.ChangePasswordAsync(user, currentPassword, newPassword);
        await RememberIfChangedAsync(result, user, replacedHash);
        return result;
    }

    public override async Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword)
    {
        var replacedHash = user.PasswordHash;
        var result = await base.ResetPasswordAsync(user, token, newPassword);
        await RememberIfChangedAsync(result, user, replacedHash);
        return result;
    }

    // Both methods above save the user (new hash) before they return; the replaced hash is stored right after a successful change.
    private async Task RememberIfChangedAsync(IdentityResult result, ApplicationUser user, string? replacedHash)
    {
        var count = history.Value.Count;
        if (!result.Succeeded || count <= 0 || user.Id == 0 || string.IsNullOrEmpty(replacedHash) || user.PasswordHash == replacedHash)
            return;

        var older = await ctx.PasswordHistory
            .Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.ReplacedAtUtc).ThenByDescending(h => h.PasswordHistoryEntryId)
            .Skip(count - 1)
            .ToListAsync();
        ctx.PasswordHistory.RemoveRange(older);
        ctx.PasswordHistory.Add(new PasswordHistoryEntry { UserId = user.Id, PasswordHash = replacedHash, ReplacedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
    }
}
