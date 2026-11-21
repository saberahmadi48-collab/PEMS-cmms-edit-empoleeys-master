namespace PEMS.Application.Interfaces;

/// <summary>One card of the import center as a row: the title and texts are in the language of the user, <c>Url</c> is the page or form that starts the import.</summary>
public sealed record ImportCenterRow(string Key, string Category, string Title, string Description, string FileTypes, string Flow, string Url);

/// <summary>The answer of a direct import: on success <c>Error</c> carries the summary text shown to the user.</summary>
public sealed record ImportCenterResult(bool Success, string? Error);

/// <summary>The import center and its two direct imports (SCRUM-706): the visible cards, and the employee and instrument spreadsheet imports (the same rules as before: the role gate of the card, exact matches only, nothing guessed).</summary>
public interface IImportCenterOperations
{
    [PEMS.Application.Common.PemsOperation("ImportCenter", "Entries")]
    Task<List<ImportCenterRow>> EntriesAsync(List<string> userRoles);

    [PEMS.Application.Common.PemsOperation("ImportCenter", "ImportEmployees")]
    Task<ImportCenterResult> ImportEmployeesAsync(byte[] file, List<string> userRoles);

    [PEMS.Application.Common.PemsOperation("ImportCenter", "ImportInstruments")]
    Task<ImportCenterResult> ImportInstrumentsAsync(byte[] file, List<string> userRoles);
}
