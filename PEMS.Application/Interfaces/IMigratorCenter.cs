namespace PEMS.Application.Interfaces;

/// <summary>What the migrator center answers: <c>Succeeded</c> and a <c>Message</c> in plain words (shown by the action that ran the operation).</summary>
public sealed record MigratorResult(bool Succeeded, string Message);

/// <summary>
/// The migrator center (SCRUM-582, 814) as operations of stored pages: upload a signed package and see what the migrator would do with it (inspect, read only), then run it with a confirmation (an install on an empty
/// database, an update with the migrations and shipped definitions, or a feed through the entity write actions); every run is recorded with the package hash, the backup point and the result. The package format and the detector are <c>MigrationPackage</c> (SCRUM-581); the trusted vendor keys are the configuration <c>Migrator:TrustedKeys</c> (key id to public key, base64), never a file of the repository.
/// </summary>
public interface IMigratorCenter
{
    [PEMS.Application.Common.PemsOperation("Migrator", "Inspect", Roles = "SystemAdmin")]
    Task<MigratorResult> InspectAsync(byte[] package, string actor);

    /// <summary>
    /// Runs the package after the explicit <paramref name="confirm"/>: verifies it, decides install, update, feed or refeed, takes the backup the update service takes first, applies, and records the run (table AppliedPackages).
    /// A failed run leaves the data as it was (the feed is one transaction; an update stops before it changes anything when its backup fails) and is recorded as failed. SystemAdmin only.
    /// </summary>
    [PEMS.Application.Common.PemsOperation("Migrator", "Run", Roles = "SystemAdmin")]
    Task<MigratorResult> RunAsync(byte[] package, bool confirm, string actor);
}
