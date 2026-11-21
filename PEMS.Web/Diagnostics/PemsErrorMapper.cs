using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Exceptions;
using PEMS.Web.Api;

namespace PEMS.Web.Diagnostics;

/// <summary>What the application knows about a failure: the kind, the HTTP status, a stable code and a retry hint. Never the exception message.</summary>
public sealed record PemsErrorInfo(PemsErrorKind Kind, int Status, string Code, int? RetryAfterSeconds = null)
{
    /// <summary>The generic, user-safe sentence of the kind (English, for problem+json; pages and toasts use localised keys by status).</summary>
    public string Title => Kind switch
    {
        PemsErrorKind.NotFound => "The resource was not found.",
        PemsErrorKind.Conflict => "The request conflicts with the current state.",
        PemsErrorKind.Validation => "The request is not valid.",
        PemsErrorKind.Forbidden => "You are not allowed to do this.",
        PemsErrorKind.Unavailable => "The service is temporarily unavailable; try again.",
        _ => "The server could not complete the request.",
    };
}

/// <summary>
/// The one mapping table of SCRUM-464 (documented in guideline chapter 08): any exception becomes a kind, a status and a code, so the API, the pages
/// and the logs agree. SQL Server errors are read here, not in controllers: unique key (2601, 2627) is a conflict, deadlock (1205), a timeout (-2) and a
/// database that cannot be reached are "unavailable".
/// </summary>
public static class PemsErrorMapper
{
    public static int Status(PemsErrorKind kind) => kind switch
    {
        PemsErrorKind.NotFound => 404,
        PemsErrorKind.Conflict => 409,
        PemsErrorKind.Validation => 422,
        PemsErrorKind.Forbidden => 403,
        PemsErrorKind.Unavailable => 503,
        _ => 500,
    };

    /// <summary>The kind, code and retry hint for a SQL Server error number, or null when the number is not one we map.</summary>
    public static PemsErrorInfo? FromSqlNumber(int number) => number switch
    {
        2601 or 2627 => new PemsErrorInfo(PemsErrorKind.Conflict, 409, "unique"),
        1205 => new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "deadlock", 2),
        -2 => new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "timeout", 5),
        53 or 233 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613 => new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "database-unavailable", 10),
        _ => null,
    };

    public static PemsErrorInfo Describe(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case PemsException pems:
                    return new PemsErrorInfo(pems.Kind, Status(pems.Kind), pems.Code, (pems as UnavailableException)?.RetryAfterSeconds);
                case DocumentFrozenException:
                    return new PemsErrorInfo(PemsErrorKind.Conflict, 409, "frozen");
                case DbUpdateConcurrencyException:
                    return new PemsErrorInfo(PemsErrorKind.Conflict, 409, "concurrency");
                case SqlException sql:
                    foreach (SqlError error in sql.Errors)
                        if (FromSqlNumber(error.Number) is { } mapped) return mapped;
                    if (FromSqlNumber(sql.Number) is { } byNumber) return byNumber;
                    break;
                case TimeoutException:
                    return new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "timeout", 5);
                case UnauthorizedAccessException:
                    return new PemsErrorInfo(PemsErrorKind.Forbidden, 403, "forbidden");
                case KeyNotFoundException:
                    return new PemsErrorInfo(PemsErrorKind.NotFound, 404, "not-found");
            }
        }
        return new PemsErrorInfo(PemsErrorKind.Unexpected, 500, "server-error");
    }

    /// <summary>The problem+json answer for a mapped failure; 503 carries Retry-After.</summary>
    public static Microsoft.AspNetCore.Mvc.ObjectResult Problem(PemsErrorInfo info, HttpResponse? response = null)
    {
        if (info.RetryAfterSeconds is { } seconds && response is not null) response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ApiProblem.Build(info.Status, info.Code, info.Title);
    }
}

/// <summary>
/// The one IExceptionHandler (SCRUM-464): for an /api request that threw outside a controller (middleware, authorization) it writes the same problem+json as
/// the controller filter; for a page it declines, so the exception page renders the localised status page with the mapped code.
/// </summary>
public sealed class PemsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<PemsExceptionHandler> _log;

    public PemsExceptionHandler(ILogger<PemsExceptionHandler> log) => _log = log;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!ApiServiceCollectionExtensions.IsApiRequest(httpContext.Request.Path)) return false;
        var info = PemsErrorMapper.Describe(exception);
        if (info.Status >= 500) _log.LogError(exception, "API error {Code} on {Path}", info.Code, httpContext.Request.Path);
        else _log.LogWarning(exception, "API error {Code} on {Path}", info.Code, httpContext.Request.Path);

        var result = PemsErrorMapper.Problem(info, httpContext.Response);
        if (result.Value is Microsoft.AspNetCore.Mvc.ProblemDetails problem) problem.Extensions["traceId"] = PemsTrace.Current(httpContext);
        var executor = httpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<Microsoft.AspNetCore.Mvc.ObjectResult>>();
        await executor.ExecuteAsync(new Microsoft.AspNetCore.Mvc.ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), result);
        return true;
    }
}
