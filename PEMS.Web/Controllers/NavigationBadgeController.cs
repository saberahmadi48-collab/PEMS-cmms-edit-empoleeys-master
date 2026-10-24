using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;
using PEMS.Web.Navigation;

namespace PEMS.Web.Controllers;

/// <summary>
/// شمارنده‌های سایدبار داده‌محور (JSON). هویت فقط از کوکی؛ هیچ userId/employeeId از ورودی پذیرفته نمی‌شود. شمارش‌های ماژولی فقط برای
/// دارنده‌ی مجوز همان شمارنده محاسبه می‌شوند (NavigationBadgeService)؛ کلید ناشناخته نادیده گرفته می‌شود.
/// </summary>
[Authorize]
public class NavigationBadgeController : Controller
{
    private const int MaxKeys = 20;
    private readonly NavigationBadgeService _badges;
    private readonly ICurrentUserContext _user;

    public NavigationBadgeController(NavigationBadgeService badges, ICurrentUserContext user)
    {
        _badges = badges;
        _user = user;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Counts([FromQuery] string[]? keys)
    {
        var wanted = (keys ?? Array.Empty<string>()).Where(k => NavigationBadges.Find(k) != null).Distinct().Take(MaxKeys);
        var counts = await _badges.GetCountsAsync(wanted, _user.UserId, _user.EmployeeId, _user.Roles);
        return Json(counts);
    }
}
