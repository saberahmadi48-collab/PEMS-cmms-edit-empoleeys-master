using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// Code-first database installer/updater. Decides whether the database needs an install (empty) or an
/// update (pending migrations on an existing schema), applies the migrations and records the run.
/// Used by the startup bootstrap and by the Admin "Database Update" page; one implementation for both.
/// </summary>
public interface IDatabaseUpdateService
{
    [PEMS.Application.Common.PemsOperation("DatabaseUpdate", "Status", Roles = "SystemAdmin")]
    Task<DatabaseUpdateStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies the install or the pending updates. Safe to call repeatedly: when nothing is pending it
    /// returns NothingToDo. A schema without migration history is never modified.
    /// With Database:BackupBeforeUpdate an update (not an install) first takes a COPY_ONLY backup and is not started when it fails.
    /// </summary>
    /// <param name="trigger">"Startup" or "Admin".</param>
    /// <param name="performedBy">User name for admin runs.</param>
    [PEMS.Application.Common.PemsOperation("DatabaseUpdate", "Apply", Roles = "SystemAdmin")]
    Task<DatabaseUpdateResultDto> ApplyAsync(string trigger, string? performedBy, CancellationToken ct = default);
}
