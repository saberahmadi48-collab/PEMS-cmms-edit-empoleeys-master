using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IGasTestService
{
    Task<List<GasTestListDto>> GetForPermitAsync(int permitId);

    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        int permitId, string location, DateTime testedAt, int testerEmployeeId,
        string? instrumentTag, DateTime? instrumentCalibrationDueDate,
        List<GasReadingInputDto> readings, int? equipmentId, int? functionalLocationId);

    [PEMS.Application.Common.PemsOperation("GasTest", "Invalidate", "GasTest.Invalidate")]
    Task<(bool Success, string? Error)> InvalidateAsync(
        int gasTestId, int permitId, string reason, int? actorEmployeeId);
}
