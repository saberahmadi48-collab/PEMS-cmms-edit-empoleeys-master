namespace PEMS.Application.Interfaces;

public interface ITechnicalQueryService
{
    [PEMS.Application.Common.PemsOperation("TechnicalQuery", "Raise")]
    Task<int> RaiseAsync(
        string queryType, string subject, string? description, int projectId, int disciplineId,
        int? relatedDocumentId, int? relatedEquipmentId, DateTime? requiredResponseDate, string priority,
        int? raisedByEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("TechnicalQuery", "Respond")]
    Task RespondAsync(
        int technicalQueryId, string responseText,
        int actorEmployeeId, int actorDisciplineId,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("TechnicalQuery", "Confirm")]
    Task ConfirmAsync(
        int technicalQueryId, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("TechnicalQuery", "Reopen")]
    Task ReopenAsync(
        int technicalQueryId, string reason, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);
}
