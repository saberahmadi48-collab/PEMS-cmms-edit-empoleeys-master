using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق «PEMS — Enterprise File Storage Management»: مدیریت ادمین روی StorageProvider -
/// CRUD + Test Connection + Enable/Disable + Set Default + حذف با بررسی ارجاع فعال (بند ۱۳).
/// </summary>
public interface IStorageProviderService
{
    Task<List<StorageProviderDto>> GetAllAsync();

    Task<StorageProviderDto?> GetByIdAsync(int id);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "Create", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error, int? Id)> CreateAsync(StorageProviderUpsertRequest request, string? createdBy);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "Update", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> UpdateAsync(int id, StorageProviderUpsertRequest request, string? modifiedBy);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "SetEnabled", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> SetEnabledAsync(int id, bool enabled, string? modifiedBy);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "SetDefault", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> SetDefaultAsync(int id, string? modifiedBy);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "Delete", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> DeleteAsync(int id);

    [PEMS.Application.Common.PemsOperation("StorageProvider", "TestConnection", Roles = "SystemAdmin")]
    Task<StorageHealthResultDto> TestConnectionAsync(int id);
}

public interface IStorageRouteService
{
    Task<List<StorageRouteDto>> GetAllAsync();

    [PEMS.Application.Common.PemsOperation("StorageRoute", "Create", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error, int? Id)> CreateAsync(StorageRouteUpsertRequest request, string? createdBy);

    [PEMS.Application.Common.PemsOperation("StorageRoute", "Update", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> UpdateAsync(int id, StorageRouteUpsertRequest request, string? modifiedBy);

    [PEMS.Application.Common.PemsOperation("StorageRoute", "Delete", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> DeleteAsync(int id);
}
