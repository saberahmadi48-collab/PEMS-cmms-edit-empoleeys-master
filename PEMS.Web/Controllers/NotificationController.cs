using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// همه‌ی اکشن‌ها فقط روی اعلان‌های کاربر احرازهویت‌شده کار می‌کنند؛ شناسه‌ی کاربر هرگز از ورودی
/// گرفته نمی‌شود. اعلانِ متعلق به دیگری مثل «وجود ندارد» رد می‌شود (404).
/// </summary>
public class NotificationController : Controller
{
    private readonly INotificationService _service;
    private readonly ICurrentUserService _currentUser;

    public NotificationController(INotificationService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private bool IsAjax => Request.Headers.XRequestedWith == "XMLHttpRequest";

    [HttpGet]
    public async Task<IActionResult> Dropdown()
    {
        if (_currentUser.UserId == null) return PartialView("_NotificationDropdown", new List<NotificationDto>());

        var items = await _service.GetForUserAsync(_currentUser.UserId.Value);
        return PartialView("_NotificationDropdown", items.Take(15).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        if (_currentUser.UserId == null) return Unauthorized();

        return Json(new { unread = await _service.GetUnreadCountAsync(_currentUser.UserId.Value) });
    }

    // کلیک روی اعلان: Read → ناوبری. فقط مسیر داخلی برنامه (جلوگیری از Open Redirect)
    [HttpGet]
    public async Task<IActionResult> Open(int id)
    {
        if (_currentUser.UserId == null) return Unauthorized();

        var linkUrl = await _service.OpenAsync(_currentUser.UserId.Value, id);

        if (!string.IsNullOrWhiteSpace(linkUrl) && Url.IsLocalUrl(linkUrl))
            return LocalRedirect(linkUrl);

        return Redirect("/p/notifications");   // SCRUM-731: the inbox is a definition page
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (_currentUser.UserId == null) return Unauthorized();

        if (!await _service.MarkAsReadAsync(_currentUser.UserId.Value, id)) return NotFound();

        return await Done();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        if (_currentUser.UserId == null) return Unauthorized();

        if (!await _service.DismissAsync(_currentUser.UserId.Value, id)) return NotFound();

        return await Done();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        if (_currentUser.UserId == null) return Unauthorized();

        await _service.MarkAllAsReadAsync(_currentUser.UserId.Value);
        return await Done();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissAll()
    {
        if (_currentUser.UserId == null) return Unauthorized();

        await _service.DismissAllAsync(_currentUser.UserId.Value);
        return await Done();
    }

    private async Task<IActionResult> Done()
    {
        if (!IsAjax) return Redirect("/p/notifications");

        return Json(new { unread = await _service.GetUnreadCountAsync(_currentUser.UserId!.Value) });
    }
}
