namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق PEMS_CMMS_MAINTENANCE_ORGANIZATION_BLUEPRINT.md بخش ۱۴: همون منطق سه‌حالته‌ی
/// ITeamService (Reject/Reactivate/New) برای عضویت در Crew.
/// </summary>
public interface ICrewService
{
    [PEMS.Application.Common.PemsOperation("Crew", "Create", "WorkOrder.Edit")]
    Task<(bool Success, string? Error, int? CrewId)> CreateAsync(string code, string name, int? workOrderId);

    Task AddOrReactivateMemberAsync(int crewId, int employeeId, int roleId);

    Task RemoveMemberAsync(int crewMemberId);
}
