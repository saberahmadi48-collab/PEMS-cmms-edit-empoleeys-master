using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// رندر یک View واقعی کامپایل‌شده‌ی PEMS.Web (بدون هاست وب، بدون HTTP، بدون دیتابیس) تا تست‌ها بتوانند HTML نهایی را بررسی کنند. فقط برای تست «چه کنترل‌هایی
/// رندر می‌شوند»: Layout/ViewStart اجرا نمی‌شود و آدرس‌سازی مسیر (asp-action) خروجی ندارد، پس تشخیص فرم‌ها با نام فیلدها انجام می‌شود.
/// </summary>
public static class RazorViewRenderer
{
    public static async Task<string> RenderAsync(Controller controller, ViewResult result, string appRelativeViewPath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new TestEnv());
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());
        var listener = new DiagnosticListener("PEMS.Tests.Razor");
        services.AddSingleton(listener);
        services.AddSingleton<DiagnosticSource>(listener);
        services.AddLogging();
        services.AddLocalization();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddControllersWithViews().AddApplicationPart(controller.GetType().Assembly);

        await using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        // با Endpoint، IUrlHelperFactory مسیر LinkGenerator را می‌گیرد (بدون IRouter قدیمی)؛ مسیری ثبت نشده، پس آدرس‌ها null می‌شوند
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));
        var routeData = new RouteData();
        routeData.Values["controller"] = controller.GetType().Name.Replace("Controller", "");
        routeData.Values["action"] = "Details";
        // Area-Viewها: partialهای هم‌پوشه (Areas/{area}/Views/{controller}/_X) فقط با مقدار area پیدا می‌شوند
        var area = controller.GetType().GetCustomAttributes(typeof(AreaAttribute), inherit: true).OfType<AreaAttribute>().FirstOrDefault();
        if (area != null) routeData.Values["area"] = area.RouteValue;
        var actionContext = new ActionContext(http, routeData, new ActionDescriptor());

        var engine = provider.GetRequiredService<IRazorViewEngine>();
        var found = engine.GetView(null, appRelativeViewPath, isMainPage: false);
        if (!found.Success) throw new InvalidOperationException("View not found: " + appRelativeViewPath + " searched: " + string.Join(", ", found.SearchedLocations ?? Array.Empty<string>()));

        var viewData = new ViewDataDictionary(controller.ViewData) { Model = result.Model };
        var tempData = new TempDataDictionary(http, new NullTempDataProvider());
        await using var writer = new StringWriter();
        var viewContext = new ViewContext(actionContext, found.View, viewData, tempData, writer, new HtmlHelperOptions());
        await found.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PEMS.Web";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
