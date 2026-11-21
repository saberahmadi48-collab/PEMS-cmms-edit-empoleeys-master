using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق CMMS-10c: LOTO/Isolation - چرخه‌ی وضعیت خطی و کنترل‌شده‌ی خودش را دارد (بدون نیاز به
/// IWorkflowRuntime - یک تاییدِ تک‌بازیگر است، نه زنجیره‌ی چندنقشی).
/// </summary>
public interface IIsolationService
{
    Task<List<IsolationPointListDto>> GetForPermitAsync(int permitId);

    [PEMS.Application.Common.PemsOperation("Isolation", "Create", "LOTO.Create")]
    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        int permitId, string energyType, string location, string? normalState, string? requiredIsolationState,
        string? isolationMethod, int? equipmentId, int? assetId, int? functionalLocationId);

    /// <summary>Floor "LOTO.View"; each action definition adds its step (LOTO.Isolate, LOTO.Deisolate, LOTO.Cancel). Verify stays in the controller: it needs the step-up check.</summary>
    [PEMS.Application.Common.PemsOperation("Isolation", "AdvanceStatus", "LOTO.View")]
    Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int isolationPointId, string nextStatus, string? reason, int? actorEmployeeId,
        string? lockNumber = null, string? tagNumber = null);
}
