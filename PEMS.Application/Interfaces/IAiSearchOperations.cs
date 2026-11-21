namespace PEMS.Application.Interfaces;

/// <summary>One document found by the AI search; <c>Relevance</c> is the similarity in percent.</summary>
public sealed record AiSearchRow(int DocumentId, string DocumentNumber, string Title, string Reason, int Relevance);

/// <summary>A choice of a filter of the AI search page (the value is what the search takes).</summary>
public sealed record AiSearchChoice(string Value);

/// <summary>The AI document search page as read operations (SCRUM-669): the search itself (nothing typed and no filter: no rows) and the choices of its three filters.</summary>
public interface IAiSearchOperations
{
    [PEMS.Application.Common.PemsOperation("AiSearch", "Search")]
    Task<List<AiSearchRow>> SearchAsync(string? direction = null, string? discipline = null, string? documentType = null, string? status = null);

    [PEMS.Application.Common.PemsOperation("AiSearch", "Disciplines")]
    Task<List<AiSearchChoice>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("AiSearch", "DocumentTypes")]
    Task<List<AiSearchChoice>> DocumentTypesAsync();

    [PEMS.Application.Common.PemsOperation("AiSearch", "Statuses")]
    Task<List<AiSearchChoice>> StatusesAsync();
}
