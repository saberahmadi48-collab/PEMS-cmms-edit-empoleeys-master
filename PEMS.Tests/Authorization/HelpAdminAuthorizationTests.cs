using Microsoft.AspNetCore.Authorization;
using PEMS.Persistence.Identity;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Bilingual Help & Knowledge System» بخش ۸ و تصمیم مجوزدهی مستندشده در
/// Areas/Admin/Controllers/HelpController: مدیریت محتوای راهنما فقط برای SystemAdmin/
/// EngineeringManager مجاز است (همان الگوی Areas/Admin/EmployeeController و
/// DepartmentController) - نه برای هر کاربر واردشده. [Authorize(Roles=...)] در سطح Action
/// توسط پایپ‌لاین واقعی MVC اجرا می‌شود، نه با فراخوانی مستقیم متد در تست؛ این تست پیکربندی
/// واقعیِ Attribute روی کنترلر را با Reflection تایید می‌کند - دقیقاً همان چیزی که در Runtime
/// توسط ASP.NET بررسی می‌شود.
/// </summary>
public class HelpAdminAuthorizationTests
{
    [Fact]
    public void AdminHelp_OperationsRequireSystemAdminOrEngineeringManager_AndCoverArticlesVisualsAndStandards()
    {
        // SCRUM-634: the Admin Help is operations of the page service, every one for the two roles the controller allowed
        var operations = PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly);
        var names = new[] { "AdminArticles", "ArticleOf", "SaveArticle", "ArticleSetPublished", "DeleteArticle", "Visuals", "SaveVisual", "VisualSetPublished", "DeleteVisual", "StandardMatrix", "SaveStandard", "StandardSetPublished", "DeleteStandard", "History" };
        foreach (var name in names)
        {
            var op = operations.Get("Help", name);
            Assert.NotNull(op);
            Assert.Contains(SystemRoles.SystemAdmin, op!.Roles);
            Assert.Contains(SystemRoles.EngineeringManager, op.Roles);
        }
    }

    [Fact]
    public void PublicHelpController_RequiresAuthenticatedUser_ButNoSpecificRole()
    {
        // طبق بخش ۹ سند: راهنما باید برای هر کاربر واردشده در دسترس باشد - نه محدودتر از
        // خودِ برنامه؛ فقط مدیریتِ محتوا (ساخت/ویرایش) محدود به نقش خاص است، نه مطالعه‌ی آن
        var controllerType = typeof(PEMS.Web.Controllers.HelpController);

        var authorizeAttr = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(authorizeAttr);
        Assert.True(string.IsNullOrEmpty(authorizeAttr!.Roles), "Public Help Center must not be role-restricted beyond authentication.");
    }
}
