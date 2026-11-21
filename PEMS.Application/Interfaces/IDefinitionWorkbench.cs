namespace PEMS.Application.Interfaces;

/// <summary>
/// The definition workbench as operations (SCRUM-449, 454, 439, 604, 606, 607): create a definition, save a draft, validate, publish, roll back and discard, addressed by the kind, key and app of the
/// definition (a <c>reference</c> is the text <c>appKey:kind:key</c>, what the workbench list uses as the id of a row). The stored pages and forms of the workbench run these through the one
/// operation handler, so the designers are ordinary definitions over the definition store (no compiled designer); the revision checks of the REST API (If-Match) stay there, here the latest revision is used.
/// Every method answers <c>(Success, Error)</c> with the text of the first problem.
/// </summary>
public interface IDefinitionWorkbench
{
    [PEMS.Application.Common.PemsOperation("Definition", "Create", "Definitions.Create")]
    Task<(bool Success, string? Error)> CreateAsync(string kind, string key, string? appKey, string titleEn, string titleFa, string payloadJson, string? changeSummary, string actor);

    [PEMS.Application.Common.PemsOperation("Definition", "SaveDraft", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SaveDraftAsync(string kind, string key, string? appKey, string payloadJson, string? changeSummary, string actor);

    [PEMS.Application.Common.PemsOperation("Definition", "Validate", "Definitions.Edit")]
    Task<(bool Success, string? Error)> ValidateAsync(string reference, string actor);

    [PEMS.Application.Common.PemsOperation("Definition", "Publish", "Definitions.Approve")]
    Task<(bool Success, string? Error)> PublishAsync(string reference, string actor);

    [PEMS.Application.Common.PemsOperation("Definition", "Rollback", "Definitions.Approve")]
    Task<(bool Success, string? Error)> RollbackAsync(string reference, int toVersion, string actor);

    [PEMS.Application.Common.PemsOperation("Definition", "DiscardDraft", "Definitions.Edit")]
    Task<(bool Success, string? Error)> DiscardDraftAsync(string reference, string actor);
}
