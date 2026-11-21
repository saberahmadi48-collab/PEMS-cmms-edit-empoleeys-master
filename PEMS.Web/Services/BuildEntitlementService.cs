using PEMS.Application.Interfaces;
using PEMS.Licensing;

namespace PEMS.Web.Services;

public sealed class BuildEntitlementService(LicenseManager licence) : IBuildEntitlementService
{
    public async Task<(bool Success, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        var entitled = await licence.IsBuildEntitledAsync(DateTimeOffset.Parse(licence.Settings.BuildDate, System.Globalization.CultureInfo.InvariantCulture), ct);
        return entitled ? (true, null) : (false, "This build is not covered by the licence maintenance period or the licence is not active. Open the Licence page.");
    }
}
