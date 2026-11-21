using System.Globalization;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-704: the lists HelpController.Index, Category and Search showed, over the same IHelpService (published articles only, in the language of the user).</summary>
public sealed class HelpOperations(IHelpService help) : IHelpOperations
{
    private static string Language(string culture) => culture.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en";

    public async Task<List<HelpArticleRow>> ArticlesAsync(string culture, string? categoryId = null, string? q = null)
    {
        var language = Language(culture);
        IEnumerable<PEMS.Application.DTOs.HelpArticleSummaryDto> articles;
        if (!string.IsNullOrWhiteSpace(q)) articles = await help.SearchAsync(q.Trim(), language);
        else if (int.TryParse(categoryId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) articles = await help.GetArticlesByCategoryAsync(id, language);
        else
        {
            var all = new List<PEMS.Application.DTOs.HelpArticleSummaryDto>();
            foreach (var category in await help.GetCategoriesAsync(language)) all.AddRange(await help.GetArticlesByCategoryAsync(category.HelpCategoryId, language));
            articles = all;
        }
        return articles.OrderBy(a => a.CategoryName).ThenBy(a => a.DisplayOrder).Select(a => new HelpArticleRow(a.Code, a.Title, a.CategoryName, a.ArticleType, a.Summary)).ToList();
    }

    public async Task<List<HelpCategoryRow>> CategoriesAsync(string culture)
        => (await help.GetCategoriesAsync(Language(culture))).Select(c => new HelpCategoryRow(c.HelpCategoryId.ToString(CultureInfo.InvariantCulture), $"{c.Name} ({c.ArticleCount})")).ToList();
}
