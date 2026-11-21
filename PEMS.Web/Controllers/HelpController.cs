using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق «PEMS Bilingual Help & Knowledge System»: کنترلر عمومیِ Help Center - قابل‌دسترس برای
/// هر کاربر واردشده (راهنما نباید محدودتر از خودِ برنامه باشد). مدیریت محتوا (ساخت/ویرایش)
/// جداگانه و فقط برای ادمین در Areas/Admin/Controllers/HelpController است.
/// </summary>
[Authorize]
public class HelpController : Controller
{
    private readonly IHelpService _helpService;
    private readonly IHelpAiExplainerService _aiExplainer;
    private readonly IHelpVisualService _helpVisualService;
    private readonly IHelpStandardReferenceService _helpStandardReferenceService;

    public HelpController(
        IHelpService helpService, IHelpAiExplainerService aiExplainer, IHelpVisualService helpVisualService,
        IHelpStandardReferenceService helpStandardReferenceService)
    {
        _helpService = helpService;
        _aiExplainer = aiExplainer;
        _helpVisualService = helpVisualService;
        _helpStandardReferenceService = helpStandardReferenceService;
    }

    private string CurrentLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? "fa" : "en";

    private int? CurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    public async Task<IActionResult> Article(string code)
    {
        var article = await _helpService.GetArticleByCodeAsync(code, CurrentLanguage);
        if (article == null) return NotFound();

        ViewData["Title"] = article.Title;
        ViewData["Section"] = "Help";

        ViewBag.Visuals = await _helpVisualService.GetVisualsForArticleAsync(article.HelpArticleId, CurrentLanguage);

        if (article.ArticleType == "StandardReference")
            ViewBag.StandardReference = await _helpStandardReferenceService.GetByArticleIdAsync(article.HelpArticleId);

        return View(article);
    }

    /// <summary>
    /// طبق بخش ۵ سند (Context-Aware): نقطه‌ی JSON که پنل راهنمای زمینه‌ای روی خودِ صفحات با
    /// آن محتوا می‌گیرد - سبک، فقط با درخواست بارگذاری می‌شود (طبق بخش ۱۸: Performance).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Context(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Json(new { found = false });

        var article = await _helpService.GetArticleByContextAsync(key, CurrentLanguage);
        if (article == null) return Json(new { found = false });

        // طبق بخش ۱۰ سند: پنل زمینه‌ای عمداً فقط متن است (نه خودِ نمودار) - جای کافی برای
        // نمودار در یک Offcanvas باریک نیست؛ فقط لینک به مقاله‌ی کامل (که نمودار را نشان
        // می‌دهد) نمایش داده می‌شود، طبق بخش ۱۰ («Do not make every article visually
        // overloaded»)
        var hasVisual = (await _helpVisualService.GetVisualsForArticleAsync(article.HelpArticleId, CurrentLanguage)).Any();

        return Json(new
        {
            found = true,
            code = article.Code,
            title = article.Title,
            summary = article.Summary,
            content = article.Content,
            languageCode = article.LanguageCode,
            relatedArticles = article.RelatedArticles.Select(r => new { r.Code, r.Title }),
            articleUrl = Url.Action("Article", "Help", new { code = article.Code }),
            hasVisual
        });
    }

    /// <summary>
    /// طبق بخش ۱۱ سند: «Ask PEMS AI about this page» - پاسخ محدود به محتوای واقعیِ همان مقاله.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AskAi([FromForm] string? articleCode, [FromForm] string question)
    {
        var answer = await _aiExplainer.ExplainAsync(articleCode, question, CurrentLanguage, CurrentUserId());
        return Json(new { answer });
    }
}
