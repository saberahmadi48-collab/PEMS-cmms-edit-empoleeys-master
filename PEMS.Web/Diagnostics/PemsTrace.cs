using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PEMS.Web.Diagnostics;

/// <summary>
/// The one reference a user, a response and a log line share (SCRUM-464): the W3C trace id of the request, or the server's request id when no trace
/// is active. It is on every response as <see cref="HeaderName"/>, on every log line as the <c>TraceId</c> property, on the error pages and in
/// the <c>traceId</c> member of a problem+json body, so a support request that quotes it finds the matching log entry.
/// </summary>
public static class PemsTrace
{
    public const string HeaderName = "X-Trace-Id";
    public const string LogProperty = "TraceId";

    public static string Current(HttpContext http)
    {
        var trace = Activity.Current?.TraceId.ToString();
        return string.IsNullOrEmpty(trace) || trace == "00000000000000000000000000000000" ? http.TraceIdentifier : trace;
    }

    /// <summary>Puts the trace id on the response and on every log line of the request. Register before the exception handler so the error pages carry it too.</summary>
    public static IApplicationBuilder UsePemsTraceId(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var id = Current(context);
        context.Response.Headers[HeaderName] = id;
        using (Serilog.Context.LogContext.PushProperty(LogProperty, id))
            await next();
    });
}

/// <summary>Adds <c>traceId</c> to every problem+json body the API writes (guideline chapter 08), so the body and the header carry the same reference.</summary>
public sealed class ProblemTraceIdFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem })
            problem.Extensions["traceId"] = PemsTrace.Current(context.HttpContext);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
