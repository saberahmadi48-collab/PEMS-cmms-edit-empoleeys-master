using Microsoft.AspNetCore.Mvc;
using PEMS.Web.Models;

namespace PEMS.Web.Controllers
{
    public class HomeController : Controller
    {
        // SCRUM-705: the dashboard is the definition page /p/home (IHomeOperations); the entry address only forwards to it
        public IActionResult Index() => Redirect("/p/home");


        public IActionResult Privacy()
        {
            return View();
        }


        // صفحه‌ی دوستانه برای پاسخ‌های 4xx/5xx بدون بدنه (مثلاً NotFound()): Program.cs آن را با
        // UseStatusCodePagesWithReExecute صدا می‌زند و کد HTTP اصلی حفظ می‌شود. بدون احراز هویت هم باید باز شود
        // (وگرنه 404 برای کاربر ناشناس به Login منحرف می‌شد).
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult StatusPage(int code = 500)
        {
            if (code < 400 || code > 599) code = 500;
            return View(new StatusPageViewModel { Code = code, RequestId = PEMS.Web.Diagnostics.PemsTrace.Current(HttpContext) });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // SCRUM-464: the exception becomes a kind and a status through the one mapping table; the page shows the localised text of that status and the
            // trace id, never the exception message.
            var failure = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            var info = failure is null ? new PEMS.Web.Diagnostics.PemsErrorInfo(PEMS.Application.Exceptions.PemsErrorKind.Unexpected, 500, "server-error") : PEMS.Web.Diagnostics.PemsErrorMapper.Describe(failure);
            Response.StatusCode = info.Status;
            if (info.RetryAfterSeconds is { } seconds) Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return View("StatusPage", new StatusPageViewModel { Code = info.Status, RequestId = PEMS.Web.Diagnostics.PemsTrace.Current(HttpContext) });
        }
    }
}
