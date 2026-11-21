using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PEMS.Shared.Configuration;

namespace PEMS.Persistence.Context;

/// <summary>
/// Design-time DbContext factory (dotnet ef). It never guesses the target database: the connection string comes from
/// the environment variable ConnectionStrings__PEMSConnection, or from the environment files (env/&lt;name&gt;.env and
/// env/&lt;name&gt;.secrets.env, name = PEMS_ENV, default "local"). There is no hard-coded fallback; the selected
/// target is always printed so a migration is never run against an unintended database.
/// (Microsoft.Extensions.Configuration.* is deliberately not used here: an assembly conflict in this project's
/// dependency graph breaks the IConfigurationBuilder extension methods, so the dependency-free EnvFiles reader is used.)
/// </summary>
public class PEMSDbContextFactory : IDesignTimeDbContextFactory<PEMSDbContext>
{
    private const string ConnectionKey = "ConnectionStrings:PEMSConnection";

    public PEMSDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var target = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"[PEMSDbContextFactory] Design-time DbContext target database: '{target.InitialCatalog}' on server '{target.DataSource}'");

        var optionsBuilder = new DbContextOptionsBuilder<PEMSDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new PEMSDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString()
    {
        var envOverride = Environment.GetEnvironmentVariable("ConnectionStrings__PEMSConnection");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride;

        // dotnet ef runs outside a web host: treat it as the development environment unless PEMS_ENV says otherwise.
        var name = EnvFiles.ResolveName(Environment.GetEnvironmentVariable(EnvFiles.EnvNameVariable), "Development");
        var directory = EnvFiles.FindDirectory(
            Environment.GetEnvironmentVariable(EnvFiles.DirectoryVariable),
            Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

        if (name != null && directory != null)
        {
            var set = EnvFiles.Load(name, directory);
            if (set.Values.TryGetValue(ConnectionKey, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        throw new InvalidOperationException(
            "No database connection string for the design-time factory. Set ConnectionStrings__PEMSConnection or define " +
            "ConnectionStrings__PEMSConnection in env/<name>.secrets.env (PEMS_ENV, default 'local'; copy " +
            "env/local.secrets.env.example to env/local.secrets.env).");
    }
}
