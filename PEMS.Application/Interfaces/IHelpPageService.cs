using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public sealed record HelpResultDto(bool Success, string? Error);

public class HelpOptionRowDto { public string Value { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

/// <summary>The edit form of an article: the DTO of the service with the related articles as one comma separated text.</summary>
public class HelpArticleFormRowDto
{
    public int HelpArticleId { get; set; } public string Code { get; set; } = ""; public string HelpCategoryId { get; set; } = ""; public string ArticleType { get; set; } = "Module"; public string? HelpKey { get; set; } public string? RelevantRoles { get; set; }
    public int DisplayOrder { get; set; } public bool IsPublished { get; set; } public string TitleEn { get; set; } = ""; public string SummaryEn { get; set; } = ""; public string ContentEn { get; set; } = ""; public string? SearchKeywordsEn { get; set; }
    public string TitleFa { get; set; } = ""; public string SummaryFa { get; set; } = ""; public string ContentFa { get; set; } = ""; public string? SearchKeywordsFa { get; set; } public string RelatedArticleIds { get; set; } = "";
}

public class HelpHistoryRowDto { public int ArticleId { get; set; } public DateTime Timestamp { get; set; } public string EntityName { get; set; } = ""; public string Action { get; set; } = ""; public string? ChangedFieldsSummary { get; set; } public string? UserName { get; set; } }

/// <summary>SCRUM-634: the Admin Help (articles, visuals, standard references) as operations of a page service; the rules stay in IHelpService, IHelpVisualService and IHelpStandardReferenceService. SystemAdmin and EngineeringManager only, as the controller.</summary>
public interface IHelpPageService
{
    [PEMS.Application.Common.PemsOperation("Help", "AdminArticles", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpArticleSummaryDto>> ArticlesAsync();

    [PEMS.Application.Common.PemsOperation("Help", "ArticleOf", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpArticleFormRowDto>> ArticleOfAsync(int articleId);

    [PEMS.Application.Common.PemsOperation("Help", "Visuals", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpVisualAdminEditDto>> VisualsAsync(int articleId);

    [PEMS.Application.Common.PemsOperation("Help", "VisualOf", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpVisualAdminEditDto>> VisualOfAsync(int visualId);

    [PEMS.Application.Common.PemsOperation("Help", "StandardMatrix", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpStandardReferenceMatrixRowDto>> StandardMatrixAsync();

    [PEMS.Application.Common.PemsOperation("Help", "StandardOf", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpStandardReferenceAdminEditDto>> StandardOfAsync(int standardId);

    [PEMS.Application.Common.PemsOperation("Help", "History", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpHistoryRowDto>> HistoryAsync(int articleId);

    [PEMS.Application.Common.PemsOperation("Help", "CategoryOptions", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpOptionRowDto>> CategoryOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Help", "WorkflowSourceOptions", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<HelpOptionRowDto>> WorkflowSourceOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Help", "SaveArticle", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> SaveArticleAsync(int articleId, HelpArticleAdminEditDto dto, string? relatedArticleIds);

    [PEMS.Application.Common.PemsOperation("Help", "ArticleSetPublished", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> ArticleSetPublishedAsync(int articleId, bool isPublished);

    [PEMS.Application.Common.PemsOperation("Help", "DeleteArticle", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> DeleteArticleAsync(int articleId);

    [PEMS.Application.Common.PemsOperation("Help", "SaveVisual", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> SaveVisualAsync(int visualId, HelpVisualAdminEditDto dto);

    [PEMS.Application.Common.PemsOperation("Help", "VisualSetPublished", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> VisualSetPublishedAsync(int visualId, bool isPublished);

    [PEMS.Application.Common.PemsOperation("Help", "DeleteVisual", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> DeleteVisualAsync(int visualId);

    [PEMS.Application.Common.PemsOperation("Help", "SaveStandard", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> SaveStandardAsync(int standardId, HelpStandardReferenceAdminEditDto dto, string? relatedArticleIds);

    [PEMS.Application.Common.PemsOperation("Help", "StandardSetPublished", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> StandardSetPublishedAsync(int standardId, bool isPublished);

    [PEMS.Application.Common.PemsOperation("Help", "DeleteStandard", Roles = "SystemAdmin,EngineeringManager")]
    Task<HelpResultDto> DeleteStandardAsync(int standardId);
}
