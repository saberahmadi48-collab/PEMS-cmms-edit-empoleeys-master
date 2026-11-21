namespace PEMS.Application.Interfaces;

/// <summary>One result of the global search: the type it belongs to, its label, a short second line and the page of the record.</summary>
public sealed record GlobalSearchRow(string Type, string Label, string Sub, string Url);

/// <summary>The full results page of the top-bar search as rows (SCRUM-703): only what the command palette shows, with the same permission and row-scope checks.</summary>
public interface IGlobalSearchOperations
{
    [PEMS.Application.Common.PemsOperation("GlobalSearch", "Search")]
    Task<List<GlobalSearchRow>> SearchAsync(string query);
}
