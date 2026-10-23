using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PEMS.Tests.Security;

/// <summary>
/// E23: بازبینی ساختاری (Reflection) نقاط ورود CMMS/Admin: هر POST/PUT/DELETE ضدجعل (ValidateAntiForgeryToken یا AutoValidate در سطح کلاس) دارد؛ هر کنترلر ناحیه‌ی Admin
/// فقط برای SystemAdmin است؛ و اکشن‌های جهشی Work Request / Work Order / Crew حتماً بررسی مجوز سمت‌سرور دارند (مجوز در بدنه‌ی اکشن یا Attribute). IDOR فرزندان
/// Work Order با تست‌های Section Guard (رکورد متعلق به Work Order دیگر) پوشش داده شده است. احراز هویت سراسری با AuthorizeFilter در Program.cs اعمال می‌شود.
/// </summary>
public class CmmsEndpointSecurityTests
{
    private static readonly Assembly Web = typeof(PEMS.Web.Controllers.WorkOrderController).Assembly;

    private static IEnumerable<Type> Controllers(Func<Type, bool> filter)
        => Web.GetTypes().Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract && filter(t));

    private static bool IsMutating(MethodInfo m)
        => m.IsPublic && !m.IsStatic && m.DeclaringType != typeof(Controller)
           && m.GetCustomAttributes().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute);

    private static bool HasAntiforgery(MethodInfo m)
        => m.GetCustomAttributes().Any(a => a is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute)
           || m.DeclaringType!.GetCustomAttributes().Any(a => a is AutoValidateAntiforgeryTokenAttribute);

    [Fact]
    public void Every_mutating_action_of_work_request_work_order_crew_and_admin_controllers_validates_the_antiforgery_token()
    {
        var scope = Controllers(t => t.Name is "WorkRequestController" or "WorkOrderController" or "CrewController" || t.Namespace!.Contains(".Areas.Admin."));
        var offenders = scope.SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(IsMutating).Where(m => !HasAntiforgery(m)).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").OrderBy(x => x).ToList();
        Assert.True(offenders.Count == 0, "POST actions without [ValidateAntiForgeryToken]: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Admin_area_controllers_are_restricted_to_system_administrators()
    {
        var offenders = Controllers(t => t.Namespace!.Contains(".Areas.Admin.Controllers"))
            .Where(t => t.Name != "SettingsController")      // فقط Redirect به صفحه‌ی Settings (که مجوز خودش را دارد) و هیچ داده‌ای ندارد
            .Where(t => !t.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a => !string.IsNullOrWhiteSpace(a.Roles) || !string.IsNullOrWhiteSpace(a.Policy)))
            .Select(t => t.Name).OrderBy(x => x).ToList();
        Assert.True(offenders.Count == 0, "Admin controllers without a role/policy restriction: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Controllers_in_scope_are_not_anonymous()
    {
        var anonymous = Controllers(t => t.Name is "WorkRequestController" or "WorkOrderController" or "CrewController" || t.Namespace!.Contains(".Areas.Admin."))
            .SelectMany(t => new[] { (MemberInfo)t }.Concat(t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)))
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any()).Select(m => m.Name).ToList();
        Assert.Empty(anonymous);
    }

    [Fact]
    public void Mutating_work_request_work_order_and_crew_actions_reference_a_server_side_permission_check()
    {
        // سطح مدل: نام اکشن‌هایی که عمداً مجوز را به سرویس واگذار می‌کنند (مجوز وظیفه‌ای گردش‌کار + اسکوپ سازمان/دیسیپلین/SoD در سرویس و Runtime اعمال می‌شود)
        var delegatedToWorkflow = new HashSet<string> { "WorkflowAction", "Submit", "SetPlanning", "AssignExecutor", "SetSchedule", "AdvanceStatus" };
        var scope = Controllers(t => t.Name is "WorkRequestController" or "WorkOrderController" or "CrewController");
        var unchecked_ = new List<string>();
        foreach (var t in scope)
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(IsMutating))
            {
                if (delegatedToWorkflow.Contains(m.Name)) continue;
                var hasAttr = m.GetCustomAttributes().Any(a => a.GetType().Name.Contains("RequirePermission") || a is AuthorizeAttribute);
                if (!hasAttr && !SourceChecksPermission(t, m.Name)) unchecked_.Add($"{t.Name}.{m.Name}");
            }
        Assert.True(unchecked_.Count == 0, "Mutating actions without an explicit permission check: " + string.Join(", ", unchecked_));
    }

    private static bool SourceChecksPermission(Type controller, string action)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "PEMS.Web", "Controllers", controller.Name + ".cs");
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            var idx = text.IndexOf(" " + action + "(", StringComparison.Ordinal);
            if (idx < 0) return false;
            var body = text.Substring(idx, Math.Min(900, text.Length - idx));
            return body.Contains("HasPermissionAsync") || body.Contains("AuthorizeAsync") || body.Contains("_sectionGuard") || body.Contains("GuardChildAsync") || body.Contains("GuardWorkOrderCrewAsync");
        }
        return false;
    }
}
