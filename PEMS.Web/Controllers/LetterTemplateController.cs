using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// مدیریت قالب‌های آماده‌ی نامه. قالب‌ها محتوای رسمی را پیش‌پر می‌کنند، پس مدیریت آن‌ها در اختیار بازبین‌ها
/// (Correspondence/Approve) است نه هر کاربری که می‌تواند نامه ثبت کند. منطق در ILetterTemplateService است.
/// </summary>
[RequirePermission("Correspondence", "Approve")]
public class LetterTemplateController : Controller
{
    private readonly ILetterTemplateService _templates;

    public LetterTemplateController(ILetterTemplateService templates)
    {
        _templates = templates;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Letter Templates";
        ViewData["Section"] = "Correspondence Management";

        return View(await _templates.ListAsync(null, includeInactive: true));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id = 0)
    {
        ViewData["Title"] = "Letter Template";
        ViewData["Section"] = "Correspondence Management";

        if (id == 0) return View(new LetterTemplateDto());

        var template = await _templates.GetAsync(id);
        if (template == null) return NotFound();
        return View(template);
    }

    // پیش‌نمایش با مقادیر نمونه (نه داده‌ی واقعی نامه): نشان می‌دهد نشانه‌ها هنگام استفاده چه می‌شوند
    [HttpGet]
    public async Task<IActionResult> Preview(int id)
    {
        ViewData["Title"] = "Letter Template Preview";
        ViewData["Section"] = "Correspondence Management";

        var template = await _templates.GetAsync(id);
        if (template == null) return NotFound();

        var sample = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["UserName"] = "Sample User",
            ["LetterType"] = template.LetterType ?? "Outgoing",
            ["ParentNumber"] = "LTR-0001",
            ["ParentSubject"] = "Sample subject",
            ["ParentDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["Counterparty"] = "Sample Company",
            ["ParentProject"] = "Sample Project"
        };

        ViewBag.Subject = LetterTemplatePlaceholders.Apply(template.Subject, sample, htmlEncode: false);
        ViewBag.Body = LetterTemplatePlaceholders.Apply(template.Body, sample, htmlEncode: true);
        return View(template);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(LetterTemplateDto model)
    {
        var (success, error, id) = await _templates.SaveAsync(model);
        if (!success)
        {
            ViewData["Title"] = "Letter Template";
            ViewData["Section"] = "Correspondence Management";
            ViewBag.Error = error;
            return View(nameof(Edit), model);
        }

        TempData["Message"] = "Template saved";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        if (!await _templates.SetActiveAsync(id, active)) return NotFound();

        TempData["Message"] = active ? "Template activated" : "Template deactivated";
        return RedirectToAction(nameof(Index));
    }
}
