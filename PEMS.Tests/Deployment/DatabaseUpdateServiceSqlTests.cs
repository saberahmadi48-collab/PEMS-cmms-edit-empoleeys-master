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

namespace PEMS.Tests.Deployment;

/// <summary>
/// Real SQL Server proof of the code-first migrator, on throw-away databases (never PEMSDB):
/// empty database -> first run (install) -> up to date -> rolled back two migrations -> update;
/// and a database that has tables but no migration history is left untouched.
/// Server: <see cref="ThrowawaySqlServer"/> (PEMS_TEST_CONNECTION or LocalDB).
/// </summary>
public class DatabaseUpdateServiceSqlTests
{
    private static string ServerConnection() => ThrowawaySqlServer.ServerConnection();

    private static string DatabaseConnection(string master, string db) => ThrowawaySqlServer.DatabaseConnection(master, db);

    private static PEMSDbContext NewContext(string connectionString) => ThrowawaySqlServer.NewContext(connectionString);

    private static DatabaseUpdateService NewService(PEMSDbContext ctx)
        => new(ctx, NullLogger<DatabaseUpdateService>.Instance);

    [Fact]
    public async Task Empty_database_installs_then_updates_after_new_migrations_appear()
    {
        var master = ServerConnection();
        var dbName = "PEMS_CodeFirstTest_" + Guid.NewGuid().ToString("N")[..12];
        var cs = DatabaseConnection(master, dbName);
        var options = new DatabaseInitializationOptions();

        try
        {
            // 1. database does not exist yet: the initializer creates it
            await DatabaseInitializer.EnsureDatabaseExistsAsync(cs, options, NullLogger.Instance);

            await using var ctx = NewContext(cs);
            var service = NewService(ctx);

            // 2. empty database = first run
            var first = await service.GetStatusAsync();
            Assert.Equal(DatabaseSetupState.FirstRun, first.State);
            Assert.True(first.CanApply);
            Assert.True(first.PendingMigrations.Count > 100);

            // 3. install
            var install = await service.ApplyAsync("Startup", null);
            Assert.True(install.Succeeded, install.Message);
            Assert.Equal(first.KnownMigrationCount, install.MigrationsApplied);
            Assert.Equal(DatabaseSetupState.UpToDate, install.StateAfter);

            var afterInstall = await service.GetStatusAsync();
            Assert.Equal(DatabaseSetupState.UpToDate, afterInstall.State);
            var run = Assert.Single(afterInstall.RecentRuns);
            Assert.Equal("FirstRun", run.Mode);
            Assert.True(run.Succeeded);

            // 4. nothing to do the second time
            var again = await service.ApplyAsync("Startup", null);
            Assert.True(again.Succeeded);
            Assert.True(again.NothingToDo);

            // 5. simulate "a new release adds migrations": roll back to just before DatabaseUpdateRuns (the table that keeps the run log,
            //    so the log starts empty again), whatever migrations were added after it, then update
            var migrations = ctx.Database.GetMigrations().ToList();
            var runsIndex = migrations.FindIndex(m => m.EndsWith("_DatabaseUpdateRuns", StringComparison.Ordinal));
            Assert.True(runsIndex > 0);
            var expectedPending = migrations.Count - runsIndex;
            await ctx.GetService<IMigrator>().MigrateAsync(migrations[runsIndex - 1]);
            ctx.ChangeTracker.Clear();

            var behind = await service.GetStatusAsync();
            Assert.Equal(DatabaseSetupState.UpdateAvailable, behind.State);
            Assert.Equal(expectedPending, behind.PendingMigrations.Count);

            var update = await service.ApplyAsync("Admin", "tester");
            Assert.True(update.Succeeded, update.Message);
            Assert.Equal(expectedPending, update.MigrationsApplied);

            var afterUpdate = await service.GetStatusAsync();
            Assert.Equal(DatabaseSetupState.UpToDate, afterUpdate.State);
            var updateRun = Assert.Single(afterUpdate.RecentRuns);
            Assert.Equal("Update", updateRun.Mode);
            Assert.Equal("tester", updateRun.PerformedBy);
        }
        finally
        {
            await DropAsync(master, dbName);
        }
    }

    [Fact]
    public async Task Schema_without_migration_history_is_reported_and_never_touched()
    {
        var master = ServerConnection();
        var dbName = "PEMS_CodeFirstTest_" + Guid.NewGuid().ToString("N")[..12];
        var cs = DatabaseConnection(master, dbName);

        try
        {
            await DatabaseInitializer.EnsureDatabaseExistsAsync(cs, new DatabaseInitializationOptions(), NullLogger.Instance);
            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE dbo.LegacyThing (Id int NOT NULL PRIMARY KEY);";
                await cmd.ExecuteNonQueryAsync();
            }

            await using var ctx = NewContext(cs);
            var service = NewService(ctx);

            Assert.Equal(DatabaseSetupState.UnmanagedSchema, (await service.GetStatusAsync()).State);

            var result = await service.ApplyAsync("Startup", null);
            Assert.False(result.Succeeded);
            Assert.Equal(0, result.MigrationsApplied);

            await using var verify = new SqlConnection(cs);
            await verify.OpenAsync();
            await using var count = verify.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM sys.tables";
            Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }
        finally
        {
            await DropAsync(master, dbName);
        }
    }

    private static Task DropAsync(string master, string dbName) => ThrowawaySqlServer.DropAsync(master, dbName);
}
