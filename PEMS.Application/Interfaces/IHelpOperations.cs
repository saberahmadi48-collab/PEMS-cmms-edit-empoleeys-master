namespace PEMS.Application.Interfaces;

/// <summary>One article of the help center list (Url opens the article page).</summary>
public sealed record HelpArticleRow(string Code, string Title, string Category, string ArticleType, string Summary);

/// <summary>One category of the help center (the choice of the category filter, the label carries the article count).</summary>
public sealed record HelpCategoryRow(string Value, string Label);

/// <summary>The help center list as read operations (SCRUM-704): categories and the articles (all, of a category, or the result of a search) in the language of the user; the article page itself stays code (rich content, visuals, AI explainer).</summary>
public interface IHelpOperations
{
    [PEMS.Application.Common.PemsOperation("Help", "Articles")]
    Task<List<HelpArticleRow>> ArticlesAsync(string culture, string? categoryId = null, string? q = null);

    [PEMS.Application.Common.PemsOperation("Help", "Categories")]
    Task<List<HelpCategoryRow>> CategoriesAsync(string culture);
}
