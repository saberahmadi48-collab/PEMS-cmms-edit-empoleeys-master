using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Security;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-412 / #382: a new password must differ from the current one and from the last N, on every path that sets a password.
/// The paths all go through UserManager (change, reset with a token), so these tests drive the real <see cref="PemsUserManager"/>
/// and <see cref="PasswordHistoryValidator"/> over an in-memory database.
/// </summary>
public sealed class PasswordHistoryTests
{
    private const string A = "Alpha@pass1234";
    private const string B = "Bravo@pass1234";
    private const string C = "Charlie@pass12";
    private const string D = "Delta@pass1234";
    private const string E = "Echo@pass12345";

    private sealed class Keys : Microsoft.Extensions.Localization.IStringLocalizer<PEMS.Web.Resources.SharedResource>
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => new(name, name);
        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => new(name, name + ":" + string.Join(",", arguments));
        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class World(ServiceProvider sp, PEMSDbContext ctx)
    {
        public UserManager<ApplicationUser> Users => sp.GetRequiredService<UserManager<ApplicationUser>>();
        public PEMSDbContext Ctx => ctx;
    }

    private static World Create(int historyCount)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("pwd_history_" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizer<PEMS.Web.Resources.SharedResource>>(new Keys());
        services.Configure<PasswordHistoryOptions>(o => o.Count = historyCount);
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 8)
            .AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>().AddDefaultTokenProviders()
            .AddPasswordValidator<PasswordHistoryValidator>()
            .AddUserManager<PemsUserManager>();
        return new World(services.BuildServiceProvider(), ctx);
    }

    private static async Task<ApplicationUser> NewUser(World w, string password = A)
    {
        var user = new ApplicationUser { UserName = "u" + Guid.NewGuid().ToString("N")[..6], Email = "u@t.local", FullName = "U", IsActive = true };
        var created = await w.Users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Description)));
        return user;
    }

    private static void AssertReused(IdentityResult result) =>
        Assert.Contains(result.Errors, e => e.Code == PasswordHistoryValidator.ErrorCode);

    [Fact]
    public async Task Forced_first_change_cannot_keep_the_one_time_password()
    {
        var w = Create(5);
        var user = await NewUser(w);                       // the one-time / temporary password

        AssertReused(await w.Users.ChangePasswordAsync(user, A, A));

        Assert.True(await w.Users.CheckPasswordAsync(user, A));        // nothing changed
        Assert.Empty(w.Ctx.PasswordHistory);
        Assert.True((await w.Users.ChangePasswordAsync(user, A, B)).Succeeded);
        Assert.True(await w.Users.CheckPasswordAsync(user, B));
    }

    [Fact]
    public async Task A_previous_password_inside_the_history_is_refused()
    {
        var w = Create(5);
        var user = await NewUser(w);
        Assert.True((await w.Users.ChangePasswordAsync(user, A, B)).Succeeded);
        Assert.True((await w.Users.ChangePasswordAsync(user, B, C)).Succeeded);

        AssertReused(await w.Users.ChangePasswordAsync(user, C, A));   // two changes ago
        AssertReused(await w.Users.ChangePasswordAsync(user, C, B));   // one change ago
        AssertReused(await w.Users.ChangePasswordAsync(user, C, C));   // current
        Assert.True(await w.Users.CheckPasswordAsync(user, C));
    }

    [Fact]
    public async Task A_password_that_fell_out_of_the_history_is_accepted_and_the_history_is_trimmed()
    {
        var w = Create(2);
        var user = await NewUser(w);
        Assert.True((await w.Users.ChangePasswordAsync(user, A, B)).Succeeded);
        Assert.True((await w.Users.ChangePasswordAsync(user, B, C)).Succeeded);
        Assert.True((await w.Users.ChangePasswordAsync(user, C, D)).Succeeded);

        Assert.Equal(2, await w.Ctx.PasswordHistory.CountAsync(h => h.UserId == user.Id));   // B and C; A was trimmed
        AssertReused(await w.Users.ChangePasswordAsync(user, D, C));
        AssertReused(await w.Users.ChangePasswordAsync(user, D, B));
        Assert.True((await w.Users.ChangePasswordAsync(user, D, A)).Succeeded);               // A is outside the last 2
        Assert.Equal(2, await w.Ctx.PasswordHistory.CountAsync(h => h.UserId == user.Id));
    }

    [Fact]
    public async Task Reset_with_a_token_follows_the_same_rule_as_the_administrator_reset_recovery_code_and_break_glass()
    {
        var w = Create(5);
        var user = await NewUser(w);

        var token = await w.Users.GeneratePasswordResetTokenAsync(user);
        AssertReused(await w.Users.ResetPasswordAsync(user, token, A));                       // same as current

        Assert.True((await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), B)).Succeeded);
        AssertReused(await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), A));   // previous
        Assert.True((await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), E)).Succeeded);
        Assert.Equal(2, await w.Ctx.PasswordHistory.CountAsync(h => h.UserId == user.Id));
    }

    [Fact]
    public async Task Only_hashes_are_stored_and_a_new_user_has_no_history()
    {
        var w = Create(5);
        var user = await NewUser(w);
        Assert.Empty(w.Ctx.PasswordHistory);

        Assert.True((await w.Users.ChangePasswordAsync(user, A, B)).Succeeded);

        var entry = await w.Ctx.PasswordHistory.SingleAsync();
        Assert.NotEqual(A, entry.PasswordHash);
        Assert.DoesNotContain("Alpha", entry.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, w.Users.PasswordHasher.VerifyHashedPassword(user, entry.PasswordHash, A));
    }

    [Fact]
    public async Task History_count_zero_turns_the_rule_off()
    {
        var w = Create(0);
        var user = await NewUser(w);

        Assert.True((await w.Users.ChangePasswordAsync(user, A, A)).Succeeded);
        Assert.Empty(w.Ctx.PasswordHistory);
    }

    [Fact]
    public async Task The_development_reset_can_restore_a_previous_password_through_the_bypass_only()
    {
        var w = Create(5);
        var user = await NewUser(w);
        Assert.True((await w.Users.ChangePasswordAsync(user, A, B)).Succeeded);

        AssertReused(await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), A));
        using (PasswordHistoryBypass.Begin())
            Assert.True((await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), A)).Succeeded);

        Assert.False(PasswordHistoryBypass.IsActive);
        AssertReused(await w.Users.ResetPasswordAsync(user, await w.Users.GeneratePasswordResetTokenAsync(user), B));   // enforced again
    }

    [Fact]
    public async Task The_message_carries_the_localization_key_and_the_count()
    {
        var w = Create(3);
        var user = await NewUser(w);

        var result = await w.Users.ChangePasswordAsync(user, A, A);

        Assert.Contains(result.Errors, e => e.Description == "Password_Reused:3");
    }

    [Fact]
    public void Product_code_never_sets_a_password_hash_directly()
    {
        // The rule lives in UserManager; code that assigns PasswordHash or hashes by hand would skip it.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException("repository root not found");

        var direct = new Regex(@"\.PasswordHash\s*=[^=]|\bHashPassword\(|SetPasswordHashAsync", RegexOptions.CultureInvariant);
        var offenders = new[] { "PEMS.Web", "PEMS.Persistence", "PEMS.Application", "PEMS.Shared", "PEMS.Domain", "PEMS.Licensing" }
            .Select(p => Path.Combine(root, p)).Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.EndsWith("PasswordHistory.cs", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => direct.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f)).ToList();

        Assert.True(offenders.Count == 0, "Set passwords only through UserManager so the history rule applies: " + string.Join(", ", offenders));
    }
}
