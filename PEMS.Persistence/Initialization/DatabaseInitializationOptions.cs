namespace PEMS.Persistence.Initialization;

/// <summary>
/// Code-first database bootstrap settings (configuration section "Database").
/// Defaults make a brand-new, empty SQL Server instance start with a complete schema and no manual SQL.
/// </summary>
public sealed class DatabaseInitializationOptions
{
    public const string SectionName = "Database";

    /// <summary>Create the database named in the connection string when it does not exist (needs CREATE DATABASE rights; skipped with a warning otherwise).</summary>
    public bool CreateIfMissing { get; set; } = true;

    /// <summary>
    /// Install (empty database) or update (pending migrations) automatically at startup. When off, the schema is
    /// changed only from the Admin "Database Update" page or by an external process.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;
}
