using PEMS.Application.Interfaces;
using PEMS.Licensing;
using PEMS.Licensing.Formats;

namespace PEMS.Web.Services;

public sealed class LicencePageService(LicenseManager manager) : ILicencePageService
{
    public async Task<List<LicenceStatusRowDto>> StatusAsync(CancellationToken ct = default)
    {
        var s = await manager.InspectAsync(ct);
        return [new LicenceStatusRowDto
        {
            State = s.State.ToString(), Reasons = string.Join(", ", s.Reasons), LicenceId = s.LicenceId, LicenceType = s.LicenceType, Edition = s.Edition, ExpiresUtc = s.ExpiresUtc?.UtcDateTime, DaysLeft = s.DaysLeft,
            UpdatesUntil = s.UpdatesUntil?.UtcDateTime, InstallId = s.InstallId, FingerprintShort = s.FingerprintShort, Enforce = manager.Settings.Enforce
        }];
    }

    public async Task<LicenceFileResult> CreateRequestAsync(string requestType, string customerRef, string? orderRef, string? reason, CancellationToken ct = default)
    {
        if (!RequestTypes.All.Contains(requestType) || string.IsNullOrWhiteSpace(customerRef)) return new LicenceFileResult { Success = false, Error = "Lic_Err_RequestFields" };
        var built = await manager.CreateRequestAsync(requestType, customerRef.Trim(), (orderRef ?? "").Trim(), reason, ct);
        return new LicenceFileResult { Success = true, Bytes = System.Text.Encoding.UTF8.GetBytes(built.FileText), FileName = $"pems-{requestType}-{built.Pending.Nonce[..8]}.pemsreq" };
    }

    public async Task<LicenceApplyResult> ApplyAsync(byte[] file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return new LicenceApplyResult(false, "Lic_Err_NoFile");
        var outcome = await manager.ApplyLicenceAsync(file, ct);
        return outcome.Applied ? new LicenceApplyResult(true, "Lic_Applied") : new LicenceApplyResult(false, "Lic_Fail_" + outcome.Failure);
    }
}
