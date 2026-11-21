using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// Throw-away databases for the migrator tests (never PEMSDB): server = PEMS_TEST_CONNECTION (only the server part is used)
/// or LocalDB. Fails loudly when no SQL Server is reachable instead of silently passing.
/// Shared by <c>DatabaseUpdateServiceSqlTests</c> and <c>DatabaseUpgradePathSqlTests</c> (SCRUM-280).
/// </summary>
public static class ThrowawaySqlServer
{
    public static string ServerConnection()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_TEST_CONNECTION");
        var csb = env is null
            ? new SqlConnectionStringBuilder { DataSource = "(localdb)\\MSSQLLocalDB", IntegratedSecurity = true }
            : new SqlConnectionStringBuilder(env);
        csb.TrustServerCertificate = true;
        csb.ConnectTimeout = 30;
        csb.InitialCatalog = "master";
        try
        {
            using var conn = new SqlConnection(csb.ConnectionString);
            conn.Open();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "This test needs a reachable SQL Server (LocalDB by default, or PEMS_TEST_CONNECTION).", ex);
        }
        return csb.ConnectionString;
    }

    /// <summary>A unique database name with the given prefix (letters, digits and '_' only).</summary>
    public static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12];

    public static string DatabaseConnection(string master, string db)
        => new SqlConnectionStringBuilder(master) { InitialCatalog = db }.ConnectionString;

    public static PEMSDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(connectionString).Options);

    public static async Task DropAsync(string master, string dbName)
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(master);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"IF DB_ID(N'{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END";
        await cmd.ExecuteNonQueryAsync();
    }
}
