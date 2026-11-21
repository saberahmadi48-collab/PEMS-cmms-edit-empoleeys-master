using PEMS.Licensing.Evaluation;

namespace PEMS.Web.Licensing;

/// <summary>
/// Verification point V2 (design 7.1): decides from the cached state, with no I/O. When enforcement is off it is not even
/// added to the pipeline. In Restricted and ActivationRequired states writes are refused and, for browsers, reads are sent
/// to the licence page; the allowlist surfaces always pass.
/// </summary>
public sealed class LicenseGateMiddleware
{
    private readonly RequestDelegate _next;

    public LicenseGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, LicenseStateHolder state)
    {
        var status = state.Current;
        if (status.State is LicenseState.NotEnforced or LicenseState.Active or LicenseState.ActiveWarning
            || LicenceFreeSurfaces.IsFree(context.Request.Path.Value))
        {
            await _next(context);
            return;
        }

        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        if (status.State == LicenseState.Restricted && isRead)
        {
            await _next(context);   // read-only restricted mode: data stays readable
            return;
        }

        if (isRead && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Admin/License");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers["X-Licence-State"] = status.State.ToString();
        await context.Response.WriteAsJsonAsync(new { error = "licence", state = status.State.ToString(), reasons = status.Reasons, page = "/Admin/License" });
    }
}
