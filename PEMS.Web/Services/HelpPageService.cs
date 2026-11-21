using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

public sealed class HelpPageService(IHelpService help, IHelpVisualService visuals, IHelpWorkflowDiagramService workflows, IHelpStandardReferenceService standards, PEMSDbContext context, ICurrentUserContext user) : IHelpPageService
{
    private static List<int> Ids(string? text) => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    private static async Task<HelpResultDto> RunAsync(Func<Task> work)
    {
        try { await work(); return new HelpResultDto(true, null); }
        catch (Exception ex) { return new HelpResultDto(false, ex.Message); }      // the services refuse with a message (as the controller showed it)
    }

    public async Task<List<HelpArticleSummaryDto>> ArticlesAsync() => (await help.GetAllForAdminAsync("en", null, null, null)).ToList();

    public async Task<List<HelpArticleFormRowDto>> ArticleOfAsync(int articleId)
    {
        var d = await help.GetForEditAsync(articleId);
        return d == null ? [] : [new HelpArticleFormRowDto { HelpArticleId = d.HelpArticleId, Code = d.Code, HelpCategoryId = d.HelpCategoryId.ToString(), ArticleType = d.ArticleType, HelpKey = d.HelpKey, RelevantRoles = d.RelevantRoles, DisplayOrder = d.DisplayOrder, IsPublished = d.IsPublished,
            TitleEn = d.TitleEn, SummaryEn = d.SummaryEn, ContentEn = d.ContentEn, SearchKeywordsEn = d.SearchKeywordsEn, TitleFa = d.TitleFa, SummaryFa = d.SummaryFa, ContentFa = d.ContentFa, SearchKeywordsFa = d.SearchKeywordsFa, RelatedArticleIds = string.Join(",", d.RelatedArticleIds) }];
    }

    public async Task<List<HelpVisualAdminEditDto>> VisualsAsync(int articleId) => (await visuals.GetAllForArticleAdminAsync(articleId)).ToList();

    public async Task<List<HelpVisualAdminEditDto>> VisualOfAsync(int visualId) { var v = await visuals.GetForEditAsync(visualId); return v == null ? [] : [v]; }

    public async Task<List<HelpStandardReferenceMatrixRowDto>> StandardMatrixAsync() => (await standards.GetMatrixAsync(includeUnpublished: true)).ToList();

    public async Task<List<HelpStandardReferenceAdminEditDto>> StandardOfAsync(int standardId) { var s = await standards.GetForEditAsync(standardId); return s == null ? [] : [s]; }

    public async Task<List<HelpHistoryRowDto>> HistoryAsync(int articleId)
    {
        var key = articleId.ToString();
        var entries = await context.AuditLogs.Where(a => (a.EntityName == "HelpArticle" || a.EntityName == "HelpArticleTranslation") && a.EntityKeyValue == key).OrderByDescending(a => a.Timestamp).Take(100).ToListAsync();
        return entries.Select(a => new HelpHistoryRowDto { ArticleId = articleId, Timestamp = a.Timestamp, EntityName = a.EntityName, Action = a.Action, ChangedFieldsSummary = a.ChangedFieldsSummary, UserName = a.UserName }).ToList();
    }

    public async Task<List<HelpOptionRowDto>> CategoryOptionsAsync() => (await context.HelpCategories.Where(c => c.IsActive).OrderBy(c => c.DisplayOrder).ToListAsync()).Select(c => new HelpOptionRowDto { Value = c.HelpCategoryId.ToString(), Label = c.NameEn }).ToList();

    public async Task<List<HelpOptionRowDto>> WorkflowSourceOptionsAsync() => (await workflows.GetAvailableWorkflowSourcesAsync()).Select(s => new HelpOptionRowDto { Value = s.Module + "|" + s.Category, Label = s.Module + " / " + s.Category + " (" + s.Name + ")" }).ToList();

    public Task<HelpResultDto> SaveArticleAsync(int articleId, HelpArticleAdminEditDto dto, string? relatedArticleIds)
    {
        dto.RelatedArticleIds = Ids(relatedArticleIds);
        return RunAsync(async () => { if (articleId == 0) await help.CreateArticleAsync(dto, user.EmployeeId); else { dto.HelpArticleId = articleId; await help.UpdateArticleAsync(articleId, dto); } });
    }

    public Task<HelpResultDto> ArticleSetPublishedAsync(int articleId, bool isPublished) => RunAsync(() => help.SetPublishedAsync(articleId, isPublished));

    public Task<HelpResultDto> DeleteArticleAsync(int articleId) => RunAsync(() => help.DeleteArticleAsync(articleId));

    public Task<HelpResultDto> SaveVisualAsync(int visualId, HelpVisualAdminEditDto dto)
    {
        if (!string.IsNullOrEmpty(dto.SourceWorkflowModule) && dto.SourceWorkflowModule.Contains('|'))
        {
            var parts = dto.SourceWorkflowModule.Split('|', 2);      // the select posts "module|category"
            dto.SourceWorkflowModule = parts[0];
            dto.SourceWorkflowCategory = parts[1];
        }
        return RunAsync(async () => { if (visualId == 0) await visuals.CreateAsync(dto); else { dto.HelpVisualId = visualId; await visuals.UpdateAsync(visualId, dto); } });
    }

    public Task<HelpResultDto> VisualSetPublishedAsync(int visualId, bool isPublished) => RunAsync(() => visuals.SetPublishedAsync(visualId, isPublished));

    public Task<HelpResultDto> DeleteVisualAsync(int visualId) => RunAsync(() => visuals.DeleteAsync(visualId));

    public Task<HelpResultDto> SaveStandardAsync(int standardId, HelpStandardReferenceAdminEditDto dto, string? relatedArticleIds)
    {
        dto.RelatedArticleIds = Ids(relatedArticleIds);
        return RunAsync(async () => { if (standardId == 0) await standards.CreateAsync(dto, user.EmployeeId); else { dto.HelpStandardReferenceId = standardId; await standards.UpdateAsync(standardId, dto); } });
    }

    public Task<HelpResultDto> StandardSetPublishedAsync(int standardId, bool isPublished) => RunAsync(() => standards.SetPublishedAsync(standardId, isPublished));

    public Task<HelpResultDto> DeleteStandardAsync(int standardId) => RunAsync(() => standards.DeleteAsync(standardId));
}
