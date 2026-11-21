using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// Audit trail of every database install/update run (code-first migrator). One row per attempt that
/// actually tried to change the schema: the automatic run at startup or an admin-triggered run from
/// the Database Update page. Written after the attempt, so the very first run is recorded too (the
/// table is created by the same migration chain).
/// </summary>
public class DatabaseUpdateRun
{
    [Key]
    public int DatabaseUpdateRunId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    /// <summary>Startup | Admin.</summary>
    [MaxLength(20)]
    public string Trigger { get; set; } = string.Empty;

    /// <summary>FirstRun (install on an empty database) | Update (pending migrations on an existing one).</summary>
    [MaxLength(20)]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Last migration applied before the run; null on a first run.</summary>
    [MaxLength(150)]
    public string? FromMigration { get; set; }

    /// <summary>Last migration known to the application when the run finished successfully.</summary>
    [MaxLength(150)]
    public string? ToMigration { get; set; }

    public int MigrationsApplied { get; set; }

    public bool Succeeded { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    /// <summary>User name for Admin runs; null for the automatic startup run.</summary>
    [MaxLength(256)]
    public string? PerformedBy { get; set; }

    /// <summary>SCRUM-280: the COPY_ONLY backup taken just before an update (path on the SQL Server machine); null when no backup was taken.</summary>
    [MaxLength(500)]
    public string? BackupFile { get; set; }
}
