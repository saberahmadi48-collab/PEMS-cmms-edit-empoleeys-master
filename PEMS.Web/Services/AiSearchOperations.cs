using Microsoft.EntityFrameworkCore;
using PEMS.Application.AI.Search.Interfaces;
using PEMS.Application.AI.Search.Models;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-669: the AI document search and the choices of its filters, exactly what the former controller read (the same search service, the same three lists).</summary>
public sealed class AiSearchOperations(IDocumentSearchAIService search, PEMSDbContext context) : IAiSearchOperations
{
    public async Task<List<AiSearchRow>> SearchAsync(string? direction = null, string? discipline = null, string? documentType = null, string? status = null)
    {
        if (string.IsNullOrWhiteSpace(direction) && string.IsNullOrWhiteSpace(discipline) && string.IsNullOrWhiteSpace(documentType) && string.IsNullOrWhiteSpace(status)) return [];
        var results = await search.SearchAsync(new DocumentSearchRequest
        {
            Direction = direction ?? string.Empty,
            Discipline = string.IsNullOrWhiteSpace(discipline) ? null : discipline,
            DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : documentType,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
        });
        return results.Select(r => new AiSearchRow(r.DocumentId, r.DocumentNumber, r.Title, r.Reason, (int)(r.SimilarityScore * 100))).ToList();
    }

    public async Task<List<AiSearchChoice>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Select(x => x.Name).ToListAsync()).Select(n => new AiSearchChoice(n)).ToList();

    public async Task<List<AiSearchChoice>> DocumentTypesAsync()
        => (await context.EngineeringDocuments.AsNoTracking().Select(x => x.DocumentType).Distinct().ToListAsync()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => new AiSearchChoice(n!)).ToList();

    public async Task<List<AiSearchChoice>> StatusesAsync()
        => (await context.EngineeringDocuments.AsNoTracking().Select(x => x.Status).Distinct().ToListAsync()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => new AiSearchChoice(n!)).ToList();
}
