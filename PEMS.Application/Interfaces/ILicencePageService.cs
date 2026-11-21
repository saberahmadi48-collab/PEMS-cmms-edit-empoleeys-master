namespace PEMS.Application.Interfaces;

public class LicenceStatusRowDto
{
    public string Id { get; set; } = "licence";
    public string State { get; set; } = string.Empty;
    public string Reasons { get; set; } = string.Empty;
    public string? LicenceId { get; set; }
    public string? LicenceType { get; set; }
    public string? Edition { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public int? DaysLeft { get; set; }
    public DateTime? UpdatesUntil { get; set; }
    public string? InstallId { get; set; }
    public string? FingerprintShort { get; set; }
    public bool Enforce { get; set; }
}

/// <summary>A refusal (Success false, Error = a resource key) or the signed request file to download.</summary>
public class LicenceFileResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public byte[]? Bytes { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed record LicenceApplyResult(bool Success, string? Error);

/// <summary>SCRUM-635: the customer licence page as operations (status, request file, licence upload). All rules stay in LicenseManager.</summary>
public interface ILicencePageService
{
    [PEMS.Application.Common.PemsOperation("Licence", "Status", Roles = "SystemAdmin")]
    Task<List<LicenceStatusRowDto>> StatusAsync(CancellationToken ct = default);

    [PEMS.Application.Common.PemsOperation("Licence", "CreateRequest", Roles = "SystemAdmin")]
    Task<LicenceFileResult> CreateRequestAsync(string requestType, string customerRef, string? orderRef, string? reason, CancellationToken ct = default);

    /// <summary>Success carries the key Lic_Applied (shown as the message); a refusal carries Lic_Fail_ plus the failure.</summary>
    [PEMS.Application.Common.PemsOperation("Licence", "Apply", Roles = "SystemAdmin")]
    Task<LicenceApplyResult> ApplyAsync(byte[] file, CancellationToken ct = default);
}
