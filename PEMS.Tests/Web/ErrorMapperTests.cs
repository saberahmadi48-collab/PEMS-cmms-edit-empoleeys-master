using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Exceptions;
using PEMS.Web.Controllers;
using PEMS.Web.Diagnostics;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>SCRUM-464: the exception taxonomy and its one mapping table (kind, HTTP status, stable code), used by the API filter, the exception handler and the error page.</summary>
public sealed class ErrorMapperTests
{
    [Theory]
    [InlineData(PemsErrorKind.NotFound, 404)]
    [InlineData(PemsErrorKind.Conflict, 409)]
    [InlineData(PemsErrorKind.Validation, 422)]
    [InlineData(PemsErrorKind.Forbidden, 403)]
    [InlineData(PemsErrorKind.Unavailable, 503)]
    [InlineData(PemsErrorKind.Unexpected, 500)]
    public void Every_kind_has_one_status(PemsErrorKind kind, int status) => Assert.Equal(status, PemsErrorMapper.Status(kind));

    [Fact]
    public void Every_kind_of_the_taxonomy_is_in_the_table()
        => Assert.All(Enum.GetValues<PemsErrorKind>(), k => Assert.NotEqual(0, PemsErrorMapper.Status(k)));

    [Fact]
    public void The_taxonomy_exceptions_map_to_their_kind_and_code()
    {
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.NotFound, 404, "not-found"), PemsErrorMapper.Describe(new NotFoundException("WorkOrder 17")));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Conflict, 409, "stage"), PemsErrorMapper.Describe(new ConflictException("stage", "wrong stage")));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Validation, 422, "validation"), PemsErrorMapper.Describe(new PemsValidationException("bad", new[] { ("/title", "required") })));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Forbidden, 403, "forbidden"), PemsErrorMapper.Describe(new ForbiddenException("no")));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "storage", 9), PemsErrorMapper.Describe(new UnavailableException("storage", "disk", 9)));
    }

    [Fact]
    public void Existing_exceptions_of_the_codebase_and_the_framework_are_mapped_without_touching_their_throw_sites()
    {
        Assert.Equal("frozen", PemsErrorMapper.Describe(new DocumentFrozenException("frozen")).Code);
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Conflict, 409, "concurrency"), PemsErrorMapper.Describe(new DbUpdateConcurrencyException("row changed")));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Unavailable, 503, "timeout", 5), PemsErrorMapper.Describe(new TimeoutException()));
        Assert.Equal(403, PemsErrorMapper.Describe(new UnauthorizedAccessException()).Status);
        Assert.Equal(404, PemsErrorMapper.Describe(new KeyNotFoundException()).Status);
    }

    [Fact]
    public void A_wrapped_taxonomy_exception_is_found_and_anything_else_is_a_generic_500()
    {
        Assert.Equal("unique", PemsErrorMapper.Describe(new InvalidOperationException("outer", new ConflictException("unique", "dup"))).Code);
        var unknown = PemsErrorMapper.Describe(new Exception("SELECT * FROM Users failed at C:\\app\\secret.cs"));
        Assert.Equal(new PemsErrorInfo(PemsErrorKind.Unexpected, 500, "server-error"), unknown);
        Assert.DoesNotContain("SELECT", unknown.Title);
    }

    [Theory]
    [InlineData(2601, 409, "unique")]
    [InlineData(2627, 409, "unique")]
    [InlineData(1205, 503, "deadlock")]
    [InlineData(-2, 503, "timeout")]
    [InlineData(4060, 503, "database-unavailable")]
    [InlineData(40613, 503, "database-unavailable")]
    public void Sql_server_error_numbers_are_mapped(int number, int status, string code)
    {
        var info = PemsErrorMapper.FromSqlNumber(number);
        Assert.NotNull(info);
        Assert.Equal(status, info!.Status);
        Assert.Equal(code, info.Code);
    }

    [Fact]
    public void An_unknown_sql_number_is_not_mapped_here() => Assert.Null(PemsErrorMapper.FromSqlNumber(547));

    [Fact]
    public void A_503_problem_tells_the_client_when_to_retry_and_carries_no_exception_text()
    {
        var http = new DefaultHttpContext();
        var result = PemsErrorMapper.Problem(PemsErrorMapper.Describe(new UnavailableException("storage", "disk /var/data is full", 9)), http.Response);

        Assert.Equal(503, result.StatusCode);
        Assert.Equal("9", http.Response.Headers.RetryAfter.ToString());
        var problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);
        Assert.DoesNotContain("disk", problem.Title + problem.Detail);
    }

    private static HomeController HomeWith(Exception? failure)
    {
        var home = (HomeController)RuntimeHelpers.GetUninitializedObject(typeof(HomeController));
        var http = new DefaultHttpContext { TraceIdentifier = "req-5" };
        if (failure is not null) http.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = failure, Path = "/x" });
        home.ControllerContext = new ControllerContext { HttpContext = http };
        return home;
    }

    [Theory]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(UnavailableException), 503)]
    [InlineData(typeof(InvalidOperationException), 500)]
    public void The_error_page_shows_the_mapped_status_and_the_trace_id_but_never_the_exception_message(Type failure, int status)
    {
        Exception ex = failure == typeof(ConflictException) ? new ConflictException("unique", "SECRET detail")
            : failure == typeof(NotFoundException) ? new NotFoundException("SECRET detail")
            : failure == typeof(UnavailableException) ? new UnavailableException("db", "SECRET detail")
            : new InvalidOperationException("SECRET detail");
        var home = HomeWith(ex);

        var view = Assert.IsType<ViewResult>(home.Error());

        Assert.Equal("StatusPage", view.ViewName);
        var model = Assert.IsType<StatusPageViewModel>(view.Model);
        Assert.Equal(status, model.Code);
        Assert.Equal(status, home.Response.StatusCode);
        Assert.Equal("req-5", model.RequestId);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(model));
    }

    [Fact]
    public void Without_an_exception_the_error_page_is_a_generic_500()
    {
        var home = HomeWith(null);
        var view = Assert.IsType<ViewResult>(home.Error());
        Assert.Equal(500, Assert.IsType<StatusPageViewModel>(view.Model).Code);
    }

    [Theory]
    [InlineData("SharedResource.resx")]
    [InlineData("SharedResource.fa.resx")]
    public void The_status_pages_have_a_text_for_every_mapped_status(string resource)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
        var xml = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Resources", resource));
        foreach (var key in new[] { "Status_NotFound", "Status_Forbidden", "Status_Conflict", "Status_TooMany", "Status_Unavailable", "Status_Generic" })
        {
            Assert.Contains($"name=\"{key}_Title\"", xml);
            Assert.Contains($"name=\"{key}_Text\"", xml);
        }
    }

    private static string Here([CallerFilePath] string p = "") => p;
}
