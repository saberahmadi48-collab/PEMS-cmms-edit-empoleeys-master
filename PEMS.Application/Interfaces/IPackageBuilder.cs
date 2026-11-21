namespace PEMS.Application.Interfaces;

/// <summary>A refusal (<c>Success</c> false, <c>Error</c> the reason) or the signed package to download.</summary>
public sealed class PackageFileResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public byte[]? Bytes { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/zip";
}

/// <summary>
/// The vendor's package panel (SCRUM-584) as operations: build and sign an install, update or feed package and download it. Signing needs the vendor key, which is configuration of the vendor's own deployment only
/// (<c>Migrator:SigningKey</c>, a PKCS#8 private key in base64, and <c>Migrator:SignerKeyId</c>, set in the secrets env file of that machine, never in the repository and never on a customer's installation); without it every
/// operation refuses. A built package is verified against the matching public key before it is handed out, so the migrator center accepts it and refuses a changed copy (SCRUM-581).
/// </summary>
public interface IPackageBuilder
{
    /// <summary>A feed package: <paramref name="rowsJson"/> is a JSON array of objects (field name to text) for <paramref name="entity"/>, applied in <paramref name="mode"/> (append, overwrite or merge).</summary>
    /// <param name="rowsJson">A JSON array of objects, or CSV text (the first line holds the field names; a comma, semicolon or tab separates, quotes protect them) (SCRUM-815).</param>
    /// <param name="keyFields">The fields that identify a row, comma separated (default Code, else Name, else the first field); a row without them or a key twice in the file is refused before the package is signed.</param>
    [PEMS.Application.Common.PemsOperation("Packager", "BuildFeed", Roles = "SystemAdmin")]
    Task<PackageFileResult> BuildFeedAsync(string toVersion, string entity, string mode, string rowsJson, string? keyFields = null);

    /// <summary>What the feed package would contain (entity, mode, key fields, row count, the first rows, what is refused) without signing or handing out anything: the preview before download (SCRUM-815).</summary>
    [PEMS.Application.Common.PemsOperation("Packager", "PreviewFeed", Roles = "SystemAdmin")]
    Task<MigratorResult> PreviewFeedAsync(string toVersion, string entity, string mode, string rowsJson, string? keyFields = null);

    /// <summary>A first-installation package: the definitions listed in <paramref name="definitionRefs"/> (appKey:kind:key, comma separated) and the schema level.</summary>
    [PEMS.Application.Common.PemsOperation("Packager", "BuildInstall", Roles = "SystemAdmin")]
    Task<PackageFileResult> BuildInstallAsync(string toVersion, string schemaLevel, string? definitionRefs);

    /// <summary>An update package from <paramref name="fromVersion"/> to <paramref name="toVersion"/>: the schema level and the definitions that changed.</summary>
    [PEMS.Application.Common.PemsOperation("Packager", "BuildUpdate", Roles = "SystemAdmin")]
    Task<PackageFileResult> BuildUpdateAsync(string fromVersion, string toVersion, string schemaLevel, string? definitionRefs);
}
