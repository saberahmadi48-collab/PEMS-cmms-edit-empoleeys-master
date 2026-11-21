namespace PEMS.Application.DTOs;

/// <summary>Where the database stands relative to the migrations shipped with this application build.</summary>
public enum DatabaseSetupState
{
    /// <summary>The server or database cannot be reached.</summary>
    Unreachable = 0,

    /// <summary>Empty database: the next run is an install (creates the whole schema).</summary>
    FirstRun = 1,

    /// <summary>Schema exists but has no migration history (not created by this migrator): never touched automatically.</summary>
    UnmanagedSchema = 2,

    /// <summary>An existing database with migrations still to apply: the next run is an update.</summary>
    UpdateAvailable = 3,

    /// <summary>Every shipped migration is applied.</summary>
    UpToDate = 4,

    /// <summary>The database has migrations this build does not know: the application is older than the database.</summary>
    DatabaseNewerThanApplication = 5
}

/// <summary>Pure rule that tells an install from an update. Kept free of EF so it is trivially testable.</summary>
public static class DatabaseSetupClassifier
{
    public static DatabaseSetupState Classify(
        bool hasUserTables,
        IReadOnlyCollection<string> appliedMigrations,
        IReadOnlyCollection<string> knownMigrations)
    {
        if (appliedMigrations.Count == 0)
            return hasUserTables ? DatabaseSetupState.UnmanagedSchema : DatabaseSetupState.FirstRun;

        var known = new HashSet<string>(knownMigrations, StringComparer.Ordinal);
        if (appliedMigrations.Any(a => !known.Contains(a)))
            return DatabaseSetupState.DatabaseNewerThanApplication;

        var applied = new HashSet<string>(appliedMigrations, StringComparer.Ordinal);
        return knownMigrations.Any(k => !applied.Contains(k))
            ? DatabaseSetupState.UpdateAvailable
            : DatabaseSetupState.UpToDate;
    }
}

public class DatabaseUpdateStatusDto
{
    public DatabaseSetupState State { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int KnownMigrationCount { get; set; }
    public int AppliedMigrationCount { get; set; }
    public string? LastAppliedMigration { get; set; }
    public string? LatestKnownMigration { get; set; }
    public List<string> PendingMigrations { get; set; } = new();
    public List<DatabaseUpdateRunDto> RecentRuns { get; set; } = new();

    /// <summary>SCRUM-280: setting Database:BackupBeforeUpdate - an update first takes a COPY_ONLY backup and stops if it fails.</summary>
    public bool BackupBeforeUpdate { get; set; }

    /// <summary>True when "Apply" would do something (install or update).</summary>
    public bool CanApply => State is DatabaseSetupState.FirstRun or DatabaseSetupState.UpdateAvailable;
}

public class DatabaseUpdateRunDto
{
    public DateTime StartedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string? FromMigration { get; set; }
    public string? ToMigration { get; set; }
    public int MigrationsApplied { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string? PerformedBy { get; set; }
    public string? BackupFile { get; set; }
}

public class DatabaseUpdateResultDto
{
    public bool Succeeded { get; set; }
    public bool NothingToDo { get; set; }
    public string Message { get; set; } = string.Empty;
    public int MigrationsApplied { get; set; }
    public DatabaseSetupState StateBefore { get; set; }
    public DatabaseSetupState StateAfter { get; set; }

    /// <summary>SCRUM-280: the pre-update backup file (path on the SQL Server machine), when one was taken.</summary>
    public string? BackupFile { get; set; }
}
