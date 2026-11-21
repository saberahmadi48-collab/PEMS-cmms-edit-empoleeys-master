namespace PEMS.Application.Interfaces;

/// <summary>SCRUM-633: licence verification point V4 as an operation: a build is applied to the database only while the maintenance entitlement covers its build date (always true when Licensing:Enforce is false).</summary>
public interface IBuildEntitlementService
{
    [PEMS.Application.Common.PemsOperation("Licence", "BuildEntitled", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> CheckAsync(CancellationToken ct = default);
}
