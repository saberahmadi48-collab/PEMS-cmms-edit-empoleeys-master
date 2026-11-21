using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IRoutingRuleService
{
    Task<List<RoutingRuleDto>> GetAllAsync();

    [PEMS.Application.Common.PemsOperation("RoutingRule", "Set", Roles = "SystemAdmin")]
    Task SetRuleAsync(string category, string targetModule, bool allowAutoRouting, string? description);

    Task<string?> GetTargetModuleAsync(string category);

    Task<bool> AllowsAutoRoutingAsync(string category);

    Task<AIConfidenceSettingsDto> GetConfidenceSettingsAsync();

    [PEMS.Application.Common.PemsOperation("RoutingRule", "SetConfidence", Roles = "SystemAdmin")]
    Task UpdateConfidenceSettingsAsync(double highThreshold, double mediumThreshold);

    Task SeedDefaultsAsync();
}
