using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface ISimopsService
{
    Task<List<SimopsCandidateDto>> GetCandidatesForPermitAsync(int permitId);
    Task<List<SimopsCandidateWorkOrderDto>> GetCandidateWorkOrdersForPermitAsync(int permitId);
    Task<List<SimopsConflictListDto>> GetConflictsForPermitAsync(int permitId);

    [PEMS.Application.Common.PemsOperation("Simops", "Create", "Simops.Create")]
    Task<(bool Success, string? Error, int? Id)> CreateAsync(int permitAId, int permitBId, int? raisedByEmployeeId);
    Task<(bool Success, string? Error)> ConfirmAsync(int conflictId, int permitId, string? reason, int? actorEmployeeId);
    Task<(bool Success, string? Error)> ResolveAsync(int conflictId, int permitId, string resolutionNotes, int? actorEmployeeId);
    Task<(bool Success, string? Error)> AcceptRiskAsync(int conflictId, int permitId, string justification, int? actorEmployeeId);
}
