using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using PEMS.Application.Uploads;
using PEMS.Web.Resources;

namespace PEMS.Web.Security.Uploads;

/// <summary>
/// Declares what an upload action accepts (SCRUM-202). Every action that receives a file must carry it (method or
/// controller); <see cref="UploadGuardFilter"/> refuses files sent to an action without it.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class UploadPolicyAttribute : Attribute
{
    public UploadPolicyAttribute(UploadProfile profile) => Profile = profile;
    public UploadProfile Profile { get; }
}

/// <summary>
/// SCRUM-596: on the generic hosts (<c>/f</c>, <c>/a</c>) the profile of an upload is not fixed in code: the action definition names it (<c>file.upload</c>, parameter <c>profile</c>) and the handler runs the same
/// <see cref="IUploadGuard"/> before anything is stored. A file that no <c>file.upload</c> step reads is never saved, so the filter lets the request through.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DefinitionUploadPolicyAttribute : Attribute;

/// <summary>
/// Global MVC filter (registered in Program.cs): every file of every multipart request goes through
/// <see cref="IUploadGuard"/> before model binding, so no controller can skip the allowlist, size limit, content check
/// and malware-scan hook. It runs after authentication and antiforgery (resource filters follow authorization filters).
/// A rejected request never reaches the action: a page post gets the message as TempData["Error"] and goes back to the
/// page it came from; a fetch/AJAX or /api call gets 400 with the message.
/// </summary>
public sealed class UploadGuardFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            await next();
            return;
        }

        var form = await request.ReadFormAsync(context.HttpContext.RequestAborted);
        if (form.Files.Count == 0)
        {
            await next();
            return;
        }

        if (context.ActionDescriptor.EndpointMetadata.OfType<DefinitionUploadPolicyAttribute>().Any())
        {
            await next();
            return;
        }

        var policy = context.ActionDescriptor.EndpointMetadata.OfType<UploadPolicyAttribute>().LastOrDefault();
        UploadCheckResult? rejected = null;
        if (policy == null)
        {
            rejected = UploadCheckResult.Reject(UploadErrors.NoPolicy, form.Files[0].FileName);
        }
        else
        {
            var guard = context.HttpContext.RequestServices.GetRequiredService<IUploadGuard>();
            foreach (var file in form.Files)
            {
                var result = await guard.CheckAsync(policy.Profile, file.FileName, file.ContentType, file.Length,
                    file.OpenReadStream, context.HttpContext.RequestAborted);
                if (!result.Ok)
                {
                    rejected = result;
                    break;
                }
            }
        }

        if (rejected == null)
        {
            await next();
            return;
        }

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<UploadGuardFilter>>();
        logger.LogWarning("Upload refused on {Path}: {Error} ({FileName})", request.Path, rejected.Error, rejected.FileName);
        context.Result = RejectionResult(context.HttpContext, rejected);
    }

    /// <summary>Localized message for a rejection, e.g. "File a.exe: this file type is not allowed here".</summary>
    public static string Message(IStringLocalizer localizer, UploadCheckResult rejected, int maxFileSizeMb) =>
        localizer["Upload_Err_" + rejected.Error, rejected.FileName ?? "", maxFileSizeMb].Value;

    private static IActionResult RejectionResult(HttpContext http, UploadCheckResult rejected)
    {
        var localizer = http.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
        var message = Message(localizer, rejected, http.RequestServices.GetRequiredService<UploadOptions>().MaxFileSizeMb);

        if (PEMS.Web.Api.ApiServiceCollectionExtensions.IsApiRequest(http.Request.Path))
            return new ObjectResult(new ProblemDetails { Status = 400, Title = message, Type = "upload-rejected", Extensions = { ["code"] = rejected.Error } })
            { StatusCode = 400, ContentTypes = { "application/problem+json" } };

        if (!WantsPage(http.Request))
            return new JsonResult(new { success = false, error = message, code = rejected.Error }) { StatusCode = 400 };

        var tempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        tempData["Error"] = message;
        var referer = http.Request.Headers.Referer.ToString();
        return new LocalRedirectResult(IsLocal(http, referer, out var local) ? local : "/");
    }

    // A form post from a page asks for HTML; fetch/XMLHttpRequest calls do not (or say so with X-Requested-With)
    private static bool WantsPage(HttpRequest request) =>
        request.Headers.XRequestedWith != "XMLHttpRequest" &&
        request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocal(HttpContext http, string referer, out string local)
    {
        local = "/";
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)) return false;
        local = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        return local.StartsWith('/') && !local.StartsWith("//") && !local.StartsWith("/\\");
    }
}
