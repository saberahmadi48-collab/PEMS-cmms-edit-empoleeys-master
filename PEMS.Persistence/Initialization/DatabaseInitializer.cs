using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;

namespace PEMS.Persistence.Initialization;

/// <summary>
/// Startup half of the code-first database: creates the database when missing and then lets
/// <see cref="IDatabaseUpdateService"/> install (empty database) or update (pending migrations).
/// The same service backs the Admin "Database Update" page. Every step is idempotent.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Creates the target database when it is missing. Must run before anything else opens the connection
    /// (for example Hangfire's storage, which installs its own schema on configuration).
    /// A failure is logged and ignored: the database may already exist and the login may lack CREATE DATABASE.
    /// </summary>
    public static async Task EnsureDatabaseExistsAsync(
        string? connectionString, DatabaseInitializationOptions options, ILogger logger, CancellationToken ct = default)
    {
        if (!options.CreateIfMissing || string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            var target = new SqlConnectionStringBuilder(connectionString);
            var database = target.InitialCatalog;
            if (string.IsNullOrWhiteSpace(database))
                return;

            target.InitialCatalog = "master";
            target.ConnectTimeout = Math.Max(target.ConnectTimeout, 15);

            await using var connection = new SqlConnection(target.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "IF DB_ID(@name) IS NULL BEGIN DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC (@sql); SELECT 1; END ELSE SELECT 0;";
            command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = database });
            var created = Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
            if (created)
                logger.LogInformation("Database '{Database}' did not exist and was created.", database);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check/create the database before startup; continuing (it may already exist or the login lacks CREATE DATABASE).");
        }
    }

    /// <summary>
    /// Installs (first run) or updates (pending migrations) the schema before the seeders run.
    /// Throws when an install/update was attempted and failed; logs and continues when the database is
    /// in a state the migrator deliberately does not touch (unmanaged schema, database newer than the app).
    /// </summary>
    public static async Task MigrateAsync(
        IServiceProvider services, DatabaseInitializationOptions options, ILogger logger, CancellationToken ct = default)
    {
        if (!options.MigrateOnStartup)
        {
            logger.LogInformation("Database:MigrateOnStartup is off; use the Admin > Database Update page or an external process.");
            return;
        }

        using var scope = services.CreateScope();
        var updater = scope.ServiceProvider.GetRequiredService<IDatabaseUpdateService>();

        var result = await updater.ApplyAsync("Startup", null, ct);

        if (result.Succeeded)
        {
            logger.LogInformation("Database: {Message}", result.Message);
            return;
        }

        if (result.StateBefore is DatabaseSetupState.FirstRun or DatabaseSetupState.UpdateAvailable)
            throw new InvalidOperationException(result.Message);

        logger.LogWarning("Database was not changed ({State}): {Message}", result.StateBefore, result.Message);
    }
}
