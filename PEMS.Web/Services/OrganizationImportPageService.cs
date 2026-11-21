using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.Organization;

namespace PEMS.Web.Services;

/// <summary>SCRUM-640: the former OrganizationImportController without any request type. The validated file waits in the server cache under the user's name (one pending file per user, 30 minutes); the browser keeps nothing.</summary>
public sealed class OrganizationImportPageService(IOrganizationImportService import, IMemoryCache cache) : IOrganizationImportPageService
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private sealed record Pending(byte[] Bytes, OrganizationImportResult Result);

    private static string Key(string? userName) => $"orgimport:{userName}";

    public async Task<OrgImportTemplateResult> TemplateAsync() =>
        new(true, null, await import.BuildTemplateAsync(), "OrganizationStructureTemplate.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

    public async Task<OrgImportStepResult> ValidateAsync(byte[] file, string? userName)
    {
        if (file is null || file.Length == 0) return new(false, "Org_Imp_NoFile");
        if (file.Length > MaxBytes) return new(false, "Org_Imp_BadFile");
        cache.Remove(Key(userName));
        OrganizationImportResult result;
        using (var s = new MemoryStream(file)) result = await import.ImportAsync(s, commit: false);
        if (result.Errors.Count > 0)
            return new(false, "Org_Imp_RejectedDetail", [string.Join(" | ", result.Errors.Take(20)) + (result.Errors.Count > 20 ? $" (+{result.Errors.Count - 20})" : string.Empty)]);
        cache.Set(Key(userName), new Pending(file, result), TimeSpan.FromMinutes(30));
        return new(true, "Org_Imp_ValidOk");
    }

    public Task<List<OrgImportPendingRowDto>> PendingAsync(string? userName)
    {
        if (!cache.TryGetValue(Key(userName), out Pending? p) || p == null) return Task.FromResult(new List<OrgImportPendingRowDto>());
        return Task.FromResult(new List<OrgImportPendingRowDto>
        {
            new()
            {
                PositionsCreated = p.Result.PositionsCreated, PositionsUpdated = p.Result.PositionsUpdated, AssignmentsCreated = p.Result.AssignmentsCreated,
                AssignmentsUpdated = p.Result.AssignmentsUpdated, Unchanged = p.Result.Unchanged, Warnings = string.Join(Environment.NewLine, p.Result.Warnings)
            }
        });
    }

    public async Task<OrgImportStepResult> ConfirmAsync(string? userName)
    {
        if (!cache.TryGetValue(Key(userName), out Pending? p) || p == null) return new(false, "Org_Imp_Expired");
        OrganizationImportResult result;
        using (var s = new MemoryStream(p.Bytes)) result = await import.ImportAsync(s, commit: true);
        if (!result.Committed) return new(false, "Org_Imp_RejectedDetail", [string.Join(" | ", result.Errors.Take(20))]);
        cache.Remove(Key(userName));
        return new(true, "Org_Imp_Committed");
    }
}
