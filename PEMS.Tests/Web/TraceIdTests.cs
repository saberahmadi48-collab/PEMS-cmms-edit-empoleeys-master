using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Web.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PEMS.Tests.Web;

/// <summary>SCRUM-464: one trace id on the response, on every log line of the request and in every problem+json body.</summary>
public sealed class TraceIdTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static async Task<DefaultHttpContext> Run(Func<HttpContext, Task>? inner = null)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UsePemsTraceId();
        app.Run(c => inner is null ? Task.CompletedTask : inner(c));
        var http = new DefaultHttpContext { TraceIdentifier = "request-7" };
        await app.Build()(http);
        return http;
    }

    [Fact]
    public async Task Without_a_trace_the_request_id_is_the_reference()
    {
        Activity.Current = null;
        var http = await Run();
        Assert.Equal("request-7", http.Response.Headers[PemsTrace.HeaderName].ToString());
    }

    [Fact]
    public async Task With_a_trace_the_w3c_trace_id_is_the_reference_and_not_the_full_activity_id()
    {
        using var activity = new Activity("request").Start();
        var http = await Run();
        var shown = http.Response.Headers[PemsTrace.HeaderName].ToString();
        Assert.Equal(activity.TraceId.ToString(), shown);
        Assert.Equal(32, shown.Length);
        Assert.DoesNotContain("-", shown);
    }

    [Fact]
    public async Task Every_log_line_of_the_request_carries_the_same_trace_id()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        Activity.Current = null;

        var http = await Run(_ => { logger.Information("inside the request"); return Task.CompletedTask; });

        var line = Assert.Single(sink.Events);
        Assert.Equal("\"request-7\"", line.Properties[PemsTrace.LogProperty].ToString());
        Assert.Equal("request-7", http.Response.Headers[PemsTrace.HeaderName].ToString());
    }

    [Fact]
    public void A_problem_body_carries_the_trace_id_of_the_request()
    {
        Activity.Current = null;
        var http = new DefaultHttpContext { TraceIdentifier = "request-9" };
        var problem = new ProblemDetails { Status = 409, Title = "conflict" };
        var context = new ResultExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
            new ObjectResult(problem) { StatusCode = 409 }, new object());

        new ProblemTraceIdFilter().OnResultExecuting(context);

        Assert.Equal("request-9", problem.Extensions["traceId"]);
    }

    [Fact]
    public void A_body_that_is_not_a_problem_is_left_alone()
    {
        var http = new DefaultHttpContext();
        var body = new { id = 1 };
        var context = new ResultExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(), new ObjectResult(body), new object());

        new ProblemTraceIdFilter().OnResultExecuting(context);

        Assert.Same(body, ((ObjectResult)context.Result).Value);
    }

    [Fact]
    public void The_pages_and_the_pipeline_use_the_one_helper()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
        var home = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Controllers", "HomeController.cs"));
        Assert.DoesNotContain("Activity.Current?.Id", home);
        Assert.Contains("PemsTrace.Current(HttpContext)", home);
        var program = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Program.cs"));
        Assert.True(program.IndexOf("UsePemsTraceId(app)", StringComparison.Ordinal) < program.IndexOf("app.UseExceptionHandler", StringComparison.Ordinal),
            "the trace id middleware must run before the exception handler");
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;
}
