using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.DTOs;
using PEMS.Persistence.Context;
using PEMS.Persistence.Initialization;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using Xunit.Abstractions;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-280 (#262): a customer database of every supported release (deploy/db/supported-releases.json) upgrades to this
/// build through the real migrator, and the result is the same database a fresh install gives:
///   1. a throw-away database is brought to exactly the release's migration set (also when later merges inserted older
///      timestamps into the chain), 2. every table that can take one gets a representative row, 3. DatabaseUpdateService
///      updates it, 4. nothing is pending, no table lost rows, and the schema equals a fresh install's.
/// Also: the pre-update backup is the rollback point (a failed backup stops the update; restoring the backup returns
/// the pre-update state). Server: <see cref="ThrowawaySqlServer"/>. Run in CI by .github/workflows/migration-chain.yml.
/// </summary>
[Trait("Category", "DbUpgrade")]
public class DatabaseUpgradePathSqlTests : IClassFixture<DatabaseUpgradePathSqlTests.FreshInstall>
{
    /// <summary>Share of tables the filler must reach; below it the data part of the test would prove little.</summary>
    private const double MinimumFilledShare = 0.6;

    private readonly FreshInstall _fresh;
    private readonly ITestOutputHelper _output;

    public DatabaseUpgradePathSqlTests(FreshInstall fresh, ITestOutputHelper output)
    {
        _fresh = fresh;
        _output = output;
    }

    /// <summary>One fresh install per test run: the reference schema every upgraded database must match.</summary>
    public sealed class FreshInstall : IAsyncLifetime
    {
        public string Master { get; private set; } = "";
        public string Name { get; } = ThrowawaySqlServer.NewName("PEMS_UpgradeRef_");
        public SortedSet<string> Schema { get; private set; } = new();

        public async Task InitializeAsync()
        {
            Master = ThrowawaySqlServer.ServerConnection();
            var cs = ThrowawaySqlServer.DatabaseConnection(Master, Name);
            await DatabaseInitializer.EnsureDatabaseExistsAsync(cs, new DatabaseInitializationOptions(), NullLogger.Instance);
            await using var ctx = ThrowawaySqlServer.NewContext(cs);
            var install = await NewService(ctx).ApplyAsync("Test", null);
            if (!install.Succeeded) throw new InvalidOperationException("Fresh install failed: " + install.Message);
            Schema = await SqlSchemaSnapshot.ReadAsync(cs);
        }

        public Task DisposeAsync() => ThrowawaySqlServer.DropAsync(Master, Name);
    }

    public static IEnumerable<object[]> Releases() => SupportedReleases.TheoryData();

    [Theory]
    [MemberData(nameof(Releases))]
    public async Task Database_of_a_supported_release_upgrades_to_the_fresh_install_schema_and_keeps_its_data(string releaseName)
    {
        var release = SupportedReleases.Load().Single(r => r.Name == releaseName);
        var master = _fresh.Master;
        var dbName = ThrowawaySqlServer.NewName("PEMS_UpgradeTest_");
        var cs = ThrowawaySqlServer.DatabaseConnection(master, dbName);

        try
        {
            await DatabaseInitializer.EnsureDatabaseExistsAsync(cs, new DatabaseInitializationOptions(), NullLogger.Instance);
            var chain = await BringToReleaseAsync(cs, release);

            var fill = await RepresentativeDataFiller.FillAsync(cs);
            _output.WriteLine($"{release.Name}: {fill.TablesWithRows}/{fill.TablesTotal} tables hold data before the upgrade.");
            foreach (var (table, why) in fill.Skipped.OrderBy(s => s.Key))
                _output.WriteLine($"  not filled: {table} - {why}");
            Assert.True(fill.TablesWithRows >= fill.TablesTotal * MinimumFilledShare,
                $"only {fill.TablesWithRows} of {fill.TablesTotal} tables could be filled; the data check would prove too little");
            var before = await RepresentativeDataFiller.RowCountsAsync(cs);

            await using (var ctx = ThrowawaySqlServer.NewContext(cs))
            {
                var service = NewService(ctx);
                var status = await service.GetStatusAsync();
                Assert.Equal(DatabaseSetupState.UpdateAvailable, status.State);
                Assert.Equal(chain.Except(release.MigrationSet(chain)).OrderBy(m => m, StringComparer.Ordinal), status.PendingMigrations);

                var update = await service.ApplyAsync("Test", null);
                Assert.True(update.Succeeded, $"upgrade from {release.Name} failed: {update.Message}");
                Assert.Equal(DatabaseSetupState.UpToDate, update.StateAfter);
            }

            var after = await RepresentativeDataFiller.RowCountsAsync(cs);
            var lost = before.Where(b => after.TryGetValue(b.Key, out var a) && a < b.Value)
                .Select(b => $"{b.Key}: {b.Value} -> {after[b.Key]}").ToList();
            Assert.True(lost.Count == 0, "rows lost by the upgrade: " + string.Join("; ", lost));

            var diff = SqlSchemaSnapshot.Diff(_fresh.Schema, await SqlSchemaSnapshot.ReadAsync(cs));
            Assert.True(diff.Count == 0,
                $"schema after upgrading from {release.Name} differs from a fresh install (- fresh only, + upgraded only):\n" +
                string.Join("\n", diff.Take(60)));
        }
        finally
        {
            await ThrowawaySqlServer.DropAsync(master, dbName);
        }
    }

    [Fact]
    public async Task Update_backs_up_first_stops_when_the_backup_fails_and_the_backup_restores_the_pre_update_state()
    {
        var master = _fresh.Master;
        var dbName = ThrowawaySqlServer.NewName("PEMS_BackupTest_");
        var cs = ThrowawaySqlServer.DatabaseConnection(master, dbName);
        string? backupFile = null;

        try
        {
            await DatabaseInitializer.EnsureDatabaseExistsAsync(cs, new DatabaseInitializationOptions(), NullLogger.Instance);
            List<string> chain;
            await using (var ctx = ThrowawaySqlServer.NewContext(cs))
            {
                chain = ctx.Database.GetMigrations().ToList();
                // an existing customer database two migrations behind this build
                await ctx.GetService<IMigrator>().MigrateAsync(chain[^3]);
            }

            // 1. backup into a folder that does not exist: the update is not started and the database is unchanged
            var defaultFolder = await DefaultBackupFolderAsync(master);
            var missing = defaultFolder.Contains('/') ? "/pems-missing-" + Guid.NewGuid().ToString("N") : @"C:\pems-missing-" + Guid.NewGuid().ToString("N");
            await using (var ctx = ThrowawaySqlServer.NewContext(cs))
            {
                var refused = await NewService(ctx, backupBeforeUpdate: true, backupDirectory: missing).ApplyAsync("Test", "tester");
                Assert.False(refused.Succeeded);
                Assert.Contains("pre-update backup failed", refused.Message);
                Assert.Equal(0, refused.MigrationsApplied);
                var still = await NewService(ctx).GetStatusAsync();
                Assert.Equal(DatabaseSetupState.UpdateAvailable, still.State);
                Assert.Equal(chain[^3], still.LastAppliedMigration);
            }

            // 2. default folder: backup, then update; the run log names the backup
            await using (var ctx = ThrowawaySqlServer.NewContext(cs))
            {
                var service = NewService(ctx, backupBeforeUpdate: true);
                Assert.True((await service.GetStatusAsync()).BackupBeforeUpdate);
                var update = await service.ApplyAsync("Test", "tester");
                Assert.True(update.Succeeded, update.Message);
                Assert.Equal(2, update.MigrationsApplied);
                backupFile = update.BackupFile;
                Assert.False(string.IsNullOrWhiteSpace(backupFile));
                Assert.StartsWith(defaultFolder.TrimEnd('/', '\\'), backupFile, StringComparison.OrdinalIgnoreCase);
                Assert.EndsWith(".bak", backupFile);
                var status = await service.GetStatusAsync();
                Assert.Equal(DatabaseSetupState.UpToDate, status.State);
                Assert.Equal(backupFile, status.RecentRuns.First().BackupFile);
            }

            // 3. rollback = restore the backup: the database is back at the release it had before the update
            await RestoreAsync(master, dbName, backupFile!);
            await using (var ctx = ThrowawaySqlServer.NewContext(cs))
            {
                var restored = await NewService(ctx).GetStatusAsync();
                Assert.Equal(DatabaseSetupState.UpdateAvailable, restored.State);
                Assert.Equal(chain[^3], restored.LastAppliedMigration);
                Assert.Equal(2, restored.PendingMigrations.Count);
            }

        }
        finally
        {
            await ThrowawaySqlServer.DropAsync(master, dbName);
            if (backupFile is not null && File.Exists(backupFile)) File.Delete(backupFile);   // the server is local (LocalDB / SQLEXPRESS)
        }
    }

    /// <summary>
    /// Applies exactly the release's migration set. Migrations the release never had (merged later with an older timestamp)
    /// are marked as applied while the chain runs, so EF skips them, and unmarked afterwards so the upgrade applies them.
    /// </summary>
    private static async Task<IReadOnlyList<string>> BringToReleaseAsync(string cs, SupportedReleases.Release release)
    {
        await using var ctx = ThrowawaySqlServer.NewContext(cs);
        var chain = ctx.Database.GetMigrations().ToList();
        Assert.Contains(release.LastMigration, chain);
        var history = ctx.GetService<IHistoryRepository>();
        await ctx.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        foreach (var skip in release.NotIncluded)
            await ctx.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(skip, "release-placeholder")));

        await ctx.GetService<IMigrator>().MigrateAsync(release.LastMigration);

        foreach (var skip in release.NotIncluded)
            await ctx.Database.ExecuteSqlRawAsync(history.GetDeleteScript(skip));

        var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(release.MigrationSet(chain), applied);
        return chain;
    }

    private static DatabaseUpdateService NewService(PEMSDbContext ctx, bool backupBeforeUpdate = false, string? backupDirectory = null)
        => new(ctx, NullLogger<DatabaseUpdateService>.Instance, 30, backupBeforeUpdate, backupDirectory);

    private static async Task<string> DefaultBackupFolderAsync(string master)
    {
        await using var conn = new SqlConnection(master);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CAST(COALESCE(SERVERPROPERTY('InstanceDefaultBackupPath'), SERVERPROPERTY('InstanceDefaultDataPath')) AS nvarchar(4000))";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>The documented rollback (CODE_FIRST_DATABASE.md "Rollback"): single user, RESTORE ... WITH REPLACE, multi user.</summary>
    private static async Task RestoreAsync(string master, string dbName, string backupFile)
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(master);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 600;
        cmd.CommandText =
            $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"RESTORE DATABASE [{dbName}] FROM DISK = @file WITH REPLACE, CHECKSUM; " +
            $"ALTER DATABASE [{dbName}] SET MULTI_USER;";
        cmd.Parameters.AddWithValue("@file", backupFile);
        await cmd.ExecuteNonQueryAsync();
    }
}
