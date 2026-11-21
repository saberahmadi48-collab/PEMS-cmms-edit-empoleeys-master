using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IShiftHandoverService
{
    Task<List<ShiftHandoverListDto>> GetAllAsync(string? status);
    Task<ShiftHandoverDetailDto?> GetDetailsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ShiftHandover", "Create", "ShiftHandover.Create")]
    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        int outgoingEmployeeId, int incomingEmployeeId, string? shiftLabel, DateTime handoverDate, string? notes);

    [PEMS.Application.Common.PemsOperation("ShiftHandover", "Prepare", "ShiftHandover.Prepare")]
    Task<(bool Success, string? Error)> PrepareAsync(int id, int? actorEmployeeId);
    [PEMS.Application.Common.PemsOperation("ShiftHandover", "Acknowledge", "ShiftHandover.Acknowledge")]
    Task<(bool Success, string? Error)> AcknowledgeAsync(int id, int? actorEmployeeId);
    [PEMS.Application.Common.PemsOperation("ShiftHandover", "Complete", "ShiftHandover.Complete")]
    Task<(bool Success, string? Error)> CompleteAsync(int id, int? actorEmployeeId);
}
