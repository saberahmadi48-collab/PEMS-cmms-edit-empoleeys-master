using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

public sealed class OpTestData { public string? Code { get; set; } public decimal? Hours { get; set; } public int? By { get; set; } }

public sealed record OpTestActor(int? EmployeeId, IReadOnlyList<string> Roles);

public interface IOpTestService
{
    [PemsOperation("Test", "Run", "Test.Run")]
    Task<(bool Success, string? Error)> RunAsync(int id, string kind, string? note, OpTestActor actor);

    [PemsOperation("Test", "Flag")]
    Task<bool> FlagAsync(int id, bool on);

    [PemsOperation("Test", "Void")]
    Task DoAsync(string text, CancellationToken ct);

    [PemsOperation("Test", "Trace")]
    Task<(bool Success, string? Error)> TraceAsync(int id, IReadOnlyList<string> roles, string? ipAddress, string? userAgent);

    [PemsOperation("Test", "Bag")]
    Task<bool> BagAsync(int id, OpTestData? data = null);

    [PemsOperation("Test", "Boom")]
    Task BoomAsync();
}

public sealed class OpTestService : IOpTestService
{
    public static string? Last;
    public Task<(bool Success, string? Error)> RunAsync(int id, string kind, string? note, OpTestActor actor)
    {
        Last = $"{id}|{kind}|{note}|{actor.EmployeeId}|{string.Join(",", actor.Roles)}";
        return Task.FromResult(kind == "refuse" ? (false, "The service said no.") : (true, (string?)null));
    }
    public Task<bool> FlagAsync(int id, bool on) { Last = $"flag {id} {on}"; return Task.FromResult(id != 0); }
    public Task DoAsync(string text, CancellationToken ct) { Last = "void " + text; return Task.CompletedTask; }
    public Task<(bool Success, string? Error)> TraceAsync(int id, IReadOnlyList<string> roles, string? ipAddress, string? userAgent)
    { Last = $"trace {id}|{string.Join(",", roles)}|{ipAddress}|{userAgent}"; return Task.FromResult((true, (string?)null)); }
    public Task<bool> BagAsync(int id, OpTestData? data = null) { Last = data is null ? "bag none" : $"bag {id}|{data.Code}|{data.Hours}|{data.By}"; return Task.FromResult(true); }
    public Task BoomAsync() => throw new InvalidOperationException("Segregation of duties.");
}

/// <summary>SCRUM-437: the existing service methods marked [PemsOperation] run from an action definition through one generic handler; the service keeps its rules, the code checks the operation's permission, the definition cannot widen it.</summary>
public sealed class OperationTests
{
    private static readonly OperationCatalog Catalog = OperationCatalog.Scan(typeof(OperationTests).Assembly);

    private sealed class Access(Func<string?, bool> allow) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allow(permission));
    }

    private sealed class FakeAccess(PEMS.Application.DTOs.AccessDecision decision) : PEMS.Application.Interfaces.IAccessControlService
    {
        public PEMS.Application.DTOs.AccessControlRequest? Asked;
        public Task<PEMS.Application.DTOs.AccessDecision> AuthorizeAsync(PEMS.Application.DTOs.AccessControlRequest request) { Asked = request; return Task.FromResult(decision); }
        public Task<PEMS.Application.DTOs.AccessDecision> AuthorizeForRolesAsync(IEnumerable<string> roles, string resourceName, string actionName) => Task.FromResult(decision);
    }

    private static OperationInvokeAction Handler(Func<string?, bool>? allow = null, FakeAccess? access = null)
    {
        var db = "op-" + Guid.NewGuid();
        using (var ctx = PemsDbContextFactory.CreateInMemory(db)) { ctx.Users.Add(new ApplicationUser { UserName = "hamid", NormalizedUserName = "HAMID", EmployeeId = 7 }); ctx.SaveChanges(); }
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access(allow ?? (_ => true))).AddScoped<IOpTestService, OpTestService>().AddSingleton<PEMS.Application.Interfaces.IAccessControlService>(access ?? new FakeAccess(PEMS.Application.DTOs.AccessDecision.Allow())).BuildServiceProvider();
        return new OperationInvokeAction(sp.GetRequiredService<IServiceScopeFactory>(), Catalog);
    }

    private static ActionInvocation Invoke(string parameters, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["Reviewer"], "hamid", JsonNode.Parse(parameters)!.AsObject());

    [Fact]
    public void Every_operation_of_the_product_is_found_by_its_attribute_and_a_key_cannot_be_declared_twice()
    {
        var product = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
        Assert.Contains("WorkRequest", product.Models);
        Assert.Equal(["Submit", "WorkflowAction"], product.Actions("WorkRequest"));
        Assert.Equal(["RolesTest", "Test"], Catalog.Models);   // RolesTest: the operations of OperationRolesAndValidationTests (SCRUM-773, 774)
        Assert.Equal(["Bag", "Boom", "Flag", "Run", "Trace", "Void"], Catalog.Actions("Test"));
        Assert.Equal("Test.Run", Catalog.Get("Test", "Run")!.Permission);
        Assert.Null(Catalog.Get("Test", "Flag")!.Permission);
        Assert.Null(Catalog.Get("Test", "Nope"));
        Assert.Null(Catalog.Get(null, null));
        var description = Catalog.Describe().Single(d => d.Action == "Run");
        Assert.Equal(["id", "kind", "note"], description.Arguments);            // the actor and the cancellation token are supplied by the engine
    }

    [Fact]
    public async Task Arguments_come_from_the_form_a_literal_or_the_context_and_an_actor_record_is_built_from_the_user()
    {
        var handler = Handler();
        var outcome = await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Run","args":{"id":"field:recordId","kind":"literal:approve"}}""", ("recordId", "5"), ("note", " hello ")), CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Equal("5|approve|hello|7|Reviewer", OpTestService.Last);     // note: the field of the same name; actor: employee id of the user (from the database) and the roles
    }

    [Fact]
    public async Task The_answer_of_the_service_becomes_the_outcome_and_a_refusal_shows_the_services_own_message()
    {
        var handler = Handler();
        var refused = await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Run","args":{"id":"literal:1","kind":"literal:refuse"}}"""), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Equal("The service said no.", refused.FormError!.Resolve(new System.Globalization.CultureInfo("en")));
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag","args":{"id":"field:id","on":"literal:true"}}""", ("id", "3")), CancellationToken.None)).Ok);
        Assert.Equal("flag 3 True", OpTestService.Last);
        Assert.False((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag","args":{"id":"literal:0","on":"literal:false"}}"""), CancellationToken.None)).Ok);    // a bool false answer
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Void","args":{"text":"field:t"}}""", ("t", "x")), CancellationToken.None)).Ok);                  // no answer = done
        var boom = await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Boom"}"""), CancellationToken.None);                                                            // a rule of the service that throws
        Assert.Equal("Segregation of duties.", boom.FormError!.Resolve(new System.Globalization.CultureInfo("en")));
    }

    [Fact]
    public async Task A_missing_required_value_or_one_that_does_not_fit_is_refused_before_the_service_is_called()
    {
        var handler = Handler();
        OpTestService.Last = "untouched";
        Assert.False((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag"}"""), CancellationToken.None)).Ok);                                   // id is a required number
        Assert.False((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag"}""", ("id", "abc"), ("on", "true")), CancellationToken.None)).Ok);      // not a number
        Assert.False((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag"}""", ("id", "1"), ("on", "perhaps")), CancellationToken.None)).Ok);    // not a bool
        Assert.False((await handler.ExecuteAsync(Invoke("""{"model":"Nope","action":"X"}"""), CancellationToken.None)).Ok);
        Assert.Equal("untouched", OpTestService.Last);
    }

    [Fact]
    public async Task The_permission_of_the_operation_is_checked_by_the_code_not_by_the_definition()
    {
        var handler = Handler(permission => permission != "Test.Run");
        OpTestService.Last = "untouched";
        var refused = await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Run","args":{"id":"literal:1","kind":"literal:x"},"permission":null}"""), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Equal("untouched", OpTestService.Last);
    }

    [Fact]
    public async Task The_roles_the_address_and_the_browser_of_the_request_are_context_arguments()
    {
        var handler = Handler();
        var invocation = new ActionInvocation(new Dictionary<string, string?>(), "en", ["Reviewer", "Admin"], "hamid",
            JsonNode.Parse("""{"model":"Test","action":"Trace","args":{"id":"literal:4","roles":"context:roles","ipAddress":"context:ip","userAgent":"context:userAgent"}}""")!.AsObject(), "10.0.0.5", "UA/1");
        Assert.True((await handler.ExecuteAsync(invocation, CancellationToken.None)).Ok);
        Assert.Equal("trace 4|Reviewer,Admin|10.0.0.5|UA/1", OpTestService.Last);
        Assert.Contains("wrong-source", handler.ValidateParameters(JsonNode.Parse("""{"model":"Test","action":"Trace","args":{"roles":"literal:Admin"}}""")!.AsObject()).Select(e => e.Code));
    }

    [Fact]
    public async Task An_options_object_is_filled_from_form_values_of_the_same_name_or_the_listed_sources_and_is_null_when_not_named()
    {
        var handler = Handler();
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Bag","args":{"id":"field:id"}}""", ("id", "2"), ("code", "x")), CancellationToken.None)).Ok);
        Assert.Equal("bag none", OpTestService.Last);
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Bag","args":{"id":"field:id","data":{"By":"context:employeeId","Hours":"literal:2.5"}}}""", ("id", "2"), ("code", "x")), CancellationToken.None)).Ok);
        Assert.Equal("bag 2|x|2.5|7", OpTestService.Last);
        Assert.Empty(handler.ValidateParameters(JsonNode.Parse("""{"model":"Test","action":"Bag","args":{"data":{}}}""")!.AsObject()));
        Assert.Contains("unknown-parameter", handler.ValidateParameters(JsonNode.Parse("""{"model":"Test","action":"Bag","args":{"data":{"Nope":"field:x"}}}""")!.AsObject()).Select(e => e.Code));
        Assert.Contains("wrong-source", handler.ValidateParameters(JsonNode.Parse("""{"model":"Test","action":"Bag","args":{"id":{"A":"field:x"}}}""")!.AsObject()).Select(e => e.Code));
    }

    private const string GuardedFlag = """{"model":"Test","action":"Flag","guard":{"resource":"Production","action":"Approve","resourceId":"field:id","mode":"MODE"},"args":{"id":"field:id","on":"literal:true"}}""";

    [Fact]
    public async Task A_separation_of_duties_refusal_stops_the_action_before_the_service_and_shows_the_policys_own_message()
    {
        var access = new FakeAccess(PEMS.Application.DTOs.AccessDecision.Deny(PEMS.Application.DTOs.AccessDecisionReason.DeniedBySoD, "You created this order."));
        var handler = Handler(access: access);
        OpTestService.Last = "untouched";
        var refused = await handler.ExecuteAsync(Invoke(GuardedFlag.Replace("MODE", "sod"), ("id", "5")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Equal("You created this order.", refused.FormError!.Resolve(new System.Globalization.CultureInfo("en")));
        Assert.Equal("untouched", OpTestService.Last);
        Assert.Equal(("Production", "Approve", "5"), (access.Asked!.ResourceName, access.Asked.ActionName, access.Asked.ResourceId));
    }

    [Fact]
    public async Task In_sod_mode_only_a_separation_of_duties_refusal_stops_the_action_but_strict_mode_stops_everything_but_an_explicit_allow()
    {
        var other = PEMS.Application.DTOs.AccessDecision.Deny(PEMS.Application.DTOs.AccessDecisionReason.DeniedByRole, "role");
        Assert.True((await Handler(access: new FakeAccess(other)).ExecuteAsync(Invoke(GuardedFlag.Replace("MODE", "sod"), ("id", "5")), CancellationToken.None)).Ok);
        OpTestService.Last = "untouched";
        Assert.False((await Handler(access: new FakeAccess(other)).ExecuteAsync(Invoke(GuardedFlag.Replace("MODE", "strict"), ("id", "5")), CancellationToken.None)).Ok);
        Assert.Equal("untouched", OpTestService.Last);
        Assert.True((await Handler(access: new FakeAccess(PEMS.Application.DTOs.AccessDecision.Allow())).ExecuteAsync(Invoke(GuardedFlag.Replace("MODE", "strict"), ("id", "5")), CancellationToken.None)).Ok);
    }

    private sealed class FakeSectionGuard(bool allowed) : PEMS.Application.Interfaces.IWorkOrderSectionGuard
    {
        public List<string> Recorded = [];
        public Task<PEMS.Application.Interfaces.WoGuardResult> AuthorizeAsync(int workOrderId, WoSection section, WoSectionAction action, PEMS.Application.Interfaces.WoCaller caller, string? reason = null, string? recordRef = null)
            => Task.FromResult(new PEMS.Application.Interfaces.WoGuardResult(allowed, allowed ? "OK" : "WO_PHASE", "فاز مجاز نیست", "Not allowed in this phase", default, allowed ? 200 : 400));
        public Task RecordChangeAsync(int workOrderId, WoSection section, WoSectionAction action, PEMS.Application.Interfaces.WoCaller caller, string? recordRef, string? reason, string? before, string? after)
        { Recorded.Add($"{workOrderId}|{section}|{action}|{caller.EmployeeId}|{after}"); return Task.CompletedTask; }
        public Task<PEMS.Application.Interfaces.WoSectionPage?> GetPageAsync(int workOrderId, PEMS.Application.Interfaces.WoCaller caller) => Task.FromResult<PEMS.Application.Interfaces.WoSectionPage?>(null);
    }

    private static OperationInvokeAction HandlerWithSections(FakeSectionGuard sections)
    {
        var db = "op-" + Guid.NewGuid();
        using (var ctx = PemsDbContextFactory.CreateInMemory(db)) { ctx.Users.Add(new ApplicationUser { UserName = "hamid", NormalizedUserName = "HAMID", EmployeeId = 7 }); ctx.SaveChanges(); }
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access(_ => true)).AddScoped<IOpTestService, OpTestService>()
            .AddSingleton<PEMS.Application.Interfaces.IWorkOrderSectionGuard>(sections).BuildServiceProvider();
        return new OperationInvokeAction(sp.GetRequiredService<IServiceScopeFactory>(), Catalog);
    }

    private const string SectionFlag = """{"model":"Test","action":"Flag","sectionGuard":{"section":"Schedule","action":"Edit","workOrderId":"field:id","audit":"On={on}; Id={id}"},"args":{"id":"field:id","on":"literal:true"}}""";

    [Fact]
    public async Task The_section_guard_stops_the_action_before_the_service_with_its_own_message_and_records_the_change_after_a_success()
    {
        var denied = new FakeSectionGuard(false);
        OpTestService.Last = "untouched";
        var refused = await HandlerWithSections(denied).ExecuteAsync(Invoke(SectionFlag, ("id", "9")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Equal("Not allowed in this phase", refused.FormError!.Resolve(new System.Globalization.CultureInfo("en")));
        Assert.Equal("untouched", OpTestService.Last);
        Assert.Empty(denied.Recorded);

        var allowed = new FakeSectionGuard(true);
        Assert.True((await HandlerWithSections(allowed).ExecuteAsync(Invoke(SectionFlag, ("id", "9")), CancellationToken.None)).Ok);
        Assert.Equal("flag 9 True", OpTestService.Last);
        Assert.Equal(["9|Schedule|Edit|7|On=True; Id=9"], allowed.Recorded);           // the audit text is filled from the service arguments

        var rule = new FakeSectionGuard(true);
        Assert.False((await HandlerWithSections(rule).ExecuteAsync(Invoke(SectionFlag.Replace("\"id\":\"field:id\",\"on\"", "\"id\":\"literal:0\",\"on\""), ("id", "9")), CancellationToken.None)).Ok);
        Assert.Empty(rule.Recorded);                                                    // a service refusal records nothing
    }

    [Fact]
    public void A_section_guard_is_checked_at_load()
    {
        var handler = Handler();
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(handler.ValidateParameters(P(SectionFlag)));
        Assert.Contains("wrong-guard", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","sectionGuard":{"section":"Nope","action":"Edit","workOrderId":"field:id"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-guard", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","sectionGuard":{"section":"Schedule","action":"Fly","workOrderId":"field:id"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-source", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","sectionGuard":{"section":"Schedule","action":"Edit","workOrderId":"7"}}""")).Select(e => e.Code));
        Assert.Contains("unknown-parameter", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","sectionGuard":{"section":"Schedule","action":"Edit","workOrderId":"field:id","skip":"yes"}}""")).Select(e => e.Code));
    }

    [Fact]
    public async Task A_posted_checkbox_value_in_either_case_is_a_bool_and_a_blank_optional_number_is_null()
    {
        var handler = Handler();
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag","args":{"id":"field:id","on":"field:attended"}}""", ("id", "3"), ("attended", "True")), CancellationToken.None)).Ok);
        Assert.Equal("flag 3 True", OpTestService.Last);
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Flag","args":{"id":"field:id","on":"field:attended"}}""", ("id", "3"), ("attended", "False")), CancellationToken.None)).Ok);
        Assert.Equal("flag 3 False", OpTestService.Last);
        Assert.True((await handler.ExecuteAsync(Invoke("""{"model":"Test","action":"Bag","args":{"id":"field:id"}}""", ("id", "3"), ("by", "")), CancellationToken.None)).Ok);
    }

    [Fact]
    public void A_guard_is_checked_at_load()
    {
        var handler = Handler();
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(handler.ValidateParameters(P(GuardedFlag.Replace("MODE", "sod"))));
        Assert.Contains("wrong-guard", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","guard":{"action":"Approve"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-guard", handler.ValidateParameters(P(GuardedFlag.Replace("MODE", "maybe"))).Select(e => e.Code));
        Assert.Contains("unknown-parameter", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","guard":{"resource":"A","action":"B","bypass":"x"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-source", handler.ValidateParameters(P("""{"model":"Test","action":"Flag","guard":{"resource":"A","action":"B","resourceId":"x"}}""")).Select(e => e.Code));
    }

    [Fact]
    public void The_parameters_are_checked_at_load()
    {
        var handler = Handler();
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(handler.ValidateParameters(P("""{"model":"Test","action":"Run","args":{"id":"field:id","kind":"literal:a","note":"context:userName"}}""")));
        Assert.Contains("unknown-model", handler.ValidateParameters(P("""{"model":"Drop","action":"Database"}""")).Select(e => e.Code));
        Assert.Contains("unknown-action", handler.ValidateParameters(P("""{"model":"Test","action":"Nope"}""")).Select(e => e.Code));
        Assert.Contains("unknown-model", handler.ValidateParameters(null).Select(e => e.Code));
        Assert.Contains("unknown-parameter", handler.ValidateParameters(P("""{"model":"Test","action":"Run","args":{"secret":"field:x"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-source", handler.ValidateParameters(P("""{"model":"Test","action":"Run","args":{"id":"x"}}""")).Select(e => e.Code));
        Assert.Contains("wrong-source", handler.ValidateParameters(P("""{"model":"Test","action":"Run","args":{"id":"context:password"}}""")).Select(e => e.Code));
    }

    [Fact]
    public async Task A_redirect_takes_its_placeholders_from_the_values_of_the_request()
    {
        var store = new InlineStore("action", "go", """{"kind":"handler","handler":"operation.invoke","parameters":{"model":"Test","action":"Void","args":{"text":"field:id"}},"redirect":"/Thing/Details/{id}","success":{"en":"Done","fa":"شد"}}""");
        var executor = new ActionExecutor(store, new Access(_ => true), [Handler()]);
        var result = await executor.RunAsync("go", Invoke("{}", ("id", "42")));
        Assert.Equal(ActionStatus.Ok, result.Status);
        Assert.Equal("/Thing/Details/42", result.Redirect);
        Assert.Equal("/Thing/Details/", (await executor.RunAsync("go", Invoke("{}"))).Redirect);       // an unknown placeholder never throws
    }

    private sealed class InlineStore(string kind, string key, string payload) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string k, string name, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult<PublishedDefinition?>(k == kind && name == key ? new PublishedDefinition(kind, key, appKey, 1, payload, "h") : null);
    }
}
