using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Areas.Admin.Models;

namespace PEMS.Tests.Authorization;

/// <summary>
/// رندر Viewهای واقعی صفحه‌ی Permission Matrix و تب Effective کاربر (بدون HTTP/دیتابیس): هر دو بدون خطا
/// رندر می‌شوند، ساختار موردنیاز JavaScript (data-attributeها) وجود دارد و حالت‌های قفل/فقط‌خواندنی درست است.
/// Layout اجرا نمی‌شود، پس بلوک @section Scripts در خروجی نیست.
/// </summary>
public class PermissionViewRenderTests
{
    private static Task<string> Render(Controller c, string path, object model)
    {
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        return RazorViewRenderer.RenderAsync(c, result, path);
    }

    // ------------------------------------------------------------------ Permission Matrix
    [Fact]
    public async Task PermissionMatrix_Renders_WithAutoSaveStructure()
    {
        var controller = new PermissionMatrixController(null!, null!, null!);
        controller.ViewBag.Roles = new[] { "SystemAdmin", "Engineer", "Viewer" };
        controller.ViewBag.Modules = new[] { "WorkOrder", "Task" };
        var model = new List<RolePermissionDto>
        {
            new() { RoleName = "Engineer", ModuleName = "WorkOrder", CanView = true }
        };

        var html = await Render(controller, "~/Areas/Admin/Views/PermissionMatrix/Index.cshtml", model);

        Assert.Contains("id=\"pmRoot\"", html);
        Assert.Contains("id=\"pmRoleSearch\"", html);
        Assert.Contains("id=\"pmModuleSearch\"", html);
        Assert.Contains("id=\"pmStatusFilter\"", html);
        // SystemAdmin is intentionally not a role card; the other two are, collapsed by default
        Assert.DoesNotContain("data-role=\"SystemAdmin\"", html);
        Assert.Equal(2, Regex.Matches(html, "<details class=\"permission-role\"").Count);
        Assert.DoesNotContain("permission-role\" id=\"role-Engineer\" open", html);
        // 10 action columns per role table, each with a column toggle and a risk class
        Assert.Equal(2 * 10, Regex.Matches(html, "class=\"form-check-input pm-col-all\"").Count);
        Assert.Contains("pm-risk-danger", html);
        // one box per (role x module x action) and exactly one is checked (Engineer / WorkOrder / View)
        Assert.Equal(2 * 2 * 10, Regex.Matches(html, "class=\"form-check-input pm-box\"").Count);
        Assert.Single(Regex.Matches(html, "data-action=\"View\" checked=\"checked\""));
        // the no-JS fallback form and Save button are still rendered
        Assert.Contains("permission-save", html);
        Assert.Contains("name=\"permissions[0].CanView\"", html);
        Assert.Contains("(1/2)", html); // Engineer: 1 of 2 modules configured
    }

    // ------------------------------------------------------------------ Users/Details Effective tab
    private static UsersController UsersControllerForRender() =>
        new(null!, null!, PemsDbContextFactory.CreateInMemory(nameof(PermissionViewRenderTests)), null!, null!, null!, null!);

    private static UserWorkspaceVm Workspace(params string[] roles) => new()
    {
        UserId = 7,
        UserName = "alice",
        FullName = "Alice A",
        IsActive = true,
        RoleNames = roles.ToList(),
        RoleUserCounts = roles.ToDictionary(r => r, _ => 3),
        EffectivePermissions = new List<EffectivePermissionDto>
        {
            new() { ResourceName = "WorkOrder", ActionName = "View", Allowed = true },
            new() { ResourceName = "WorkOrder", ActionName = "Delete", Allowed = false, IsDestructive = true },
            new() { ResourceName = "EngineeringDocuments", ActionName = "ViewAllDisciplines", Allowed = true }
        },
        NonEditablePermissions = new Dictionary<string, string>
        {
            ["EngineeringDocuments.ViewAllDisciplines"] = PermissionChangeReasons.OutsideMatrix
        }
    };

    private static Regex Row(string permission) => new($"<tr data-permission=\"{Regex.Escape(permission)}\"[^>]*>");

    [Fact]
    public async Task EffectiveTab_Renders_EditableRows_InTheRightTable_AndLocksNonEditableOnes()
    {
        var html = await Render(UsersControllerForRender(), "~/Areas/Admin/Views/Users/Details.cshtml", Workspace("Engineer"));

        Assert.Contains("id=\"permAdmin\"", html);
        Assert.Contains("data-editable=\"1\"", html);
        Assert.Contains("id=\"permToolbar\"", html);
        Assert.Contains("id=\"permEnableSelected\"", html);
        Assert.DoesNotContain("id=\"permTargetRole\"", html); // single role: no picker needed

        var allowed = html[html.IndexOf("id=\"permTableAllowed\"", StringComparison.Ordinal)..html.IndexOf("id=\"permTableDenied\"", StringComparison.Ordinal)];
        var denied = html[html.IndexOf("id=\"permTableDenied\"", StringComparison.Ordinal)..];

        var view = Row("WorkOrder.View").Match(allowed).Value;
        Assert.Contains("data-allowed=\"1\"", view);
        Assert.Contains("data-locked=\"0\"", view);

        var delete = Row("WorkOrder.Delete").Match(denied).Value;
        Assert.Contains("data-allowed=\"0\"", delete);
        Assert.Contains("data-locked=\"0\"", delete);

        var locked = Row("EngineeringDocuments.ViewAllDisciplines").Match(allowed).Value;
        Assert.Contains("data-locked=\"1\"", locked);
        // locked rows get disabled controls
        var lockedRow = allowed[allowed.IndexOf(locked, StringComparison.Ordinal)..];
        lockedRow = lockedRow[..lockedRow.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.Contains("perm-select\" disabled", lockedRow);
        Assert.Contains("perm-toggle\" checked disabled", lockedRow);
    }

    [Fact]
    public async Task EffectiveTab_WithSeveralRoles_OffersATargetRolePicker()
    {
        var html = await Render(UsersControllerForRender(), "~/Areas/Admin/Views/Users/Details.cshtml", Workspace("Engineer", "Viewer"));

        Assert.Contains("id=\"permTargetRole\"", html);
        Assert.Contains("<option value=\"Engineer\">", html);
        Assert.Contains("<option value=\"Viewer\">", html);
    }

    [Fact]
    public async Task EffectiveTab_ForSystemAdmin_IsReadOnly()
    {
        var html = await Render(UsersControllerForRender(), "~/Areas/Admin/Views/Users/Details.cshtml", Workspace(SystemRoles.SystemAdmin));

        Assert.Contains("data-editable=\"0\"", html);
        Assert.DoesNotContain("id=\"permToolbar\"", html);
        Assert.DoesNotContain("data-locked=\"0\"", html);
        Assert.Equal(3, Regex.Matches(html, "data-locked=\"1\"").Count);
    }
}
