using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Security;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-410 / #380: a deploy, an update or a migration must never create, reset or change an existing administrator
/// password (staging, demo, client servers). Only a fresh install or a database reset (empty user table) may restart
/// the first-install administrator flow. Every application start runs <see cref="IdentitySeeder"/> and
/// <see cref="DevAdminPasswordReset"/>, so these tests are the guard: they fail if either starts touching an existing account.
/// </summary>
[Collection("AdminPasswordEnvironmentVariable")]
public sealed class AdminPasswordSurvivesUpdateTests
{
    private const string SeedPassword = "Seed@pass12345";
    private const string ChosenPassword = "Chosen@pass67890";

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "PEMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static ServiceProvider Build(string dbName, bool withSeedSettings = true)
    {
        var settings = new Dictionary<string, string?>();
        if (withSeedSettings)
        {
            settings["Seed:Admin:UserName"] = "admin";
            settings["Seed:Admin:Email"] = "admin@pems.local";
            settings["Seed:Admin:Password"] = SeedPassword;
        }

        var services = new ServiceCollection();
        services.AddSingleton(PemsDbContextFactory.CreateInMemory(dbName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>(o => o.Password.RequiredLength = 8)
            .AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static async Task<ApplicationUser> Admin(IServiceProvider sp) =>
        await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("admin")
        ?? throw new InvalidOperationException("admin missing");

    /// <summary>The administrator as it looks after the owner finished the first-install flow: own password, no forced change.</summary>
    private static async Task<(string Hash, int Id)> FinishFirstInstall(IServiceProvider sp)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await Admin(sp);
        var token = await users.GeneratePasswordResetTokenAsync(admin);
        Assert.True((await users.ResetPasswordAsync(admin, token, ChosenPassword)).Succeeded);
        admin.MustChangePassword = false;
        Assert.True((await users.UpdateAsync(admin)).Succeeded);
        var reloaded = await Admin(sp);
        return (reloaded.PasswordHash!, reloaded.Id);
    }

    [Fact]
    public async Task Seeder_on_an_empty_database_creates_the_administrator_who_must_change_the_password()
    {
        using var sp = Build("seed_empty_" + Guid.NewGuid().ToString("N"));

        await IdentitySeeder.SeedAsync(sp);

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await Admin(sp);
        Assert.True(admin.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(admin, SeedPassword));
        Assert.True(await users.IsInRoleAsync(admin, SystemRoles.SystemAdmin));
    }

    [Fact]
    public async Task Seeder_on_a_database_that_has_users_changes_nothing_about_the_administrator()
    {
        using var sp = Build("seed_update_" + Guid.NewGuid().ToString("N"));
        await IdentitySeeder.SeedAsync(sp);
        var (hash, id) = await FinishFirstInstall(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var userCount = users.Users.Count();

        // The same start-up code runs on every deploy/update; run it several times.
        for (var i = 0; i < 3; i++)
            await IdentitySeeder.SeedAsync(sp);

        var admin = await Admin(sp);
        Assert.Equal(id, admin.Id);
        Assert.Equal(hash, admin.PasswordHash);
        Assert.False(admin.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(admin, ChosenPassword));
        Assert.False(await users.CheckPasswordAsync(admin, SeedPassword));
        Assert.Equal(userCount, users.Users.Count());
        Assert.Single(await users.GetUsersInRoleAsync(SystemRoles.SystemAdmin));
    }

    [Fact]
    public async Task Seeder_does_not_create_a_second_administrator_when_another_user_exists()
    {
        using var sp = Build("seed_other_" + Guid.NewGuid().ToString("N"), withSeedSettings: true);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "someone", Email = "s@t.local", FullName = "Someone", IsActive = true }, ChosenPassword)).Succeeded);

        await IdentitySeeder.SeedAsync(sp);

        Assert.Null(await users.FindByNameAsync("admin"));
        Assert.Equal(1, users.Users.Count());
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "true")]
    [InlineData("Demo", "true")]
    [InlineData("Production", null)]
    [InlineData("Staging", null)]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    public async Task Development_reset_never_touches_the_password_unless_development_and_explicitly_enabled(string environment, string? variable)
    {
        using var sp = Build("reset_off_" + Guid.NewGuid().ToString("N"));
        await IdentitySeeder.SeedAsync(sp);
        var (hash, _) = await FinishFirstInstall(sp);

        var previous = Environment.GetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable);
        Environment.SetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable, variable);
        try
        {
            await DevAdminPasswordReset.ResetIfRequestedAsync(sp, new FakeEnvironment(environment), NullLogger.Instance);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable, previous);
        }

        var admin = await Admin(sp);
        Assert.Equal(hash, admin.PasswordHash);
        Assert.False(admin.MustChangePassword);
    }

    [Fact]
    public async Task Development_reset_is_the_only_path_that_resets_and_it_forces_a_password_change()
    {
        using var sp = Build("reset_on_" + Guid.NewGuid().ToString("N"));
        await IdentitySeeder.SeedAsync(sp);
        await FinishFirstInstall(sp);

        var previous = Environment.GetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable);
        Environment.SetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable, "true");
        try
        {
            await DevAdminPasswordReset.ResetIfRequestedAsync(sp, new FakeEnvironment("Development"), NullLogger.Instance);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevAdminPasswordReset.EnableEnvironmentVariable, previous);
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await Admin(sp);
        Assert.True(admin.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(admin, SeedPassword));
    }
}
