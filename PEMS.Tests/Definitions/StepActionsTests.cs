using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-593 (ordered steps in one transaction with rollback), SCRUM-594 (a step's result reaches later steps and the redirect), SCRUM-595 (step-up as a setting of the action). The transaction is a fake that
/// records commit and rollback: the EF transaction itself needs SQL Server and is a QA line.
/// </summary>
public sealed class StepActionsTests
{
    private sealed class FakeTransaction : IActionTransaction
    {
        public List<string> Log { get; } = [];

        public async Task<ActionOutcome> RunAsync(Func<Task<ActionOutcome>> body, CancellationToken ct)
        {
            Log.Add("begin");
            try
            {
                var outcome = await body();
                Log.Add(outcome.Ok ? "commit" : "rollback");
                return outcome;
            }
            catch { Log.Add("rollback"); throw; }
        }
    }

    private sealed class Recorder(string key, Func<ActionInvocation, ActionOutcome>? answer = null, IReadOnlyCollection<string>? roles = null) : IActionHandler
    {
        public List<ActionInvocation> Calls { get; } = [];
        public string Key => key;
        public IReadOnlyCollection<string>? AllowedRoles => roles;
        public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
        {
            Calls.Add(invocation);
            return Task.FromResult(answer?.Invoke(invocation) ?? ActionOutcome.Done());
        }
    }

    private sealed class Throwing : IActionHandler
    {
        public string Key => "boom";
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct) => throw new InvalidOperationException("disk full");
    }

    private sealed class Services(params IActionHandler[] handlers) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IEnumerable<IActionHandler>) ? handlers : null;
    }

    private static ActionInvocation Invocation(JsonObject parameters, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.Ordinal), "en", ["Admin"], "hamid", parameters);

    private static JsonObject Params(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static (StepsAction Steps, FakeTransaction Tx, Recorder Create, Recorder Child) Build(Func<ActionInvocation, ActionOutcome>? child = null)
    {
        var create = new Recorder("make.parent", _ => ActionOutcome.Done(returned: new Dictionary<string, string?> { ["id"] = "42", ["code"] = "P-42" }));
        var kid = new Recorder("make.child", child);
        var tx = new FakeTransaction();
        return (new StepsAction(new Services(create, kid), tx), tx, create, kid);
    }

    private const string TwoSteps = """
        { "steps": [
            { "as": "parent", "handler": "make.parent", "parameters": { "entity": "Parent" } },
            { "as": "kid", "handler": "make.child", "parameters": { "entity": "Child" }, "values": { "parentId": "step:parent.id", "label": "literal:first", "name": "field:name" } }
        ] }
        """;

    // ---- 593: ordered steps, one transaction

    [Fact]
    public async Task Steps_run_in_order_inside_one_transaction_that_commits()
    {
        var (steps, tx, create, child) = Build();
        var outcome = await steps.ExecuteAsync(Invocation(Params(TwoSteps), ("name", "Ali")), default);
        Assert.True(outcome.Ok);
        Assert.Equal(["begin", "commit"], tx.Log);
        Assert.Single(create.Calls);
        Assert.Single(child.Calls);
        Assert.Equal("Parent", (string?)create.Calls[0].Parameters!["entity"]);        // each step runs with its own parameters
        Assert.Equal("Child", (string?)child.Calls[0].Parameters!["entity"]);
    }

    [Fact]
    public async Task The_first_refusal_stops_the_steps_and_rolls_everything_back()
    {
        var (_, tx, _, child) = Build();
        var refusing = new Recorder("make.parent", _ => ActionOutcome.Refused(TranslatableText.Literal("No parent allowed")));
        var stepsRefusing = new StepsAction(new Services(refusing, child), tx);
        var outcome = await stepsRefusing.ExecuteAsync(Invocation(Params(TwoSteps)), default);
        Assert.False(outcome.Ok);
        Assert.Equal("No parent allowed", outcome.FormError!.Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.Empty(child.Calls);                                                      // the later step never ran
        Assert.Equal(["begin", "rollback"], tx.Log);
    }

    [Fact]
    public async Task A_refusal_of_a_later_step_rolls_back_the_earlier_ones()
    {
        var (steps, tx, create, _) = Build(_ => ActionOutcome.Refused("name", TranslatableText.Literal("Name taken")));
        var outcome = await steps.ExecuteAsync(Invocation(Params(TwoSteps), ("name", "Ali")), default);
        Assert.False(outcome.Ok);
        Assert.Contains("name", outcome.FieldErrors!.Keys);                              // the field error of the failing step reaches the form
        Assert.Single(create.Calls);                                                    // it did run, and the transaction took it back
        Assert.Equal(["begin", "rollback"], tx.Log);
    }

    [Fact]
    public async Task An_exception_in_a_step_rolls_back_and_is_not_swallowed()
    {
        var tx = new FakeTransaction();
        var steps = new StepsAction(new Services(new Recorder("make.parent"), new Throwing()), tx);
        var json = """{ "steps": [ { "handler": "make.parent" }, { "handler": "boom" } ] }""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => steps.ExecuteAsync(Invocation(Params(json)), default));
        Assert.Equal(["begin", "rollback"], tx.Log);
    }

    [Fact]
    public async Task Transaction_false_runs_the_steps_without_a_transaction()
    {
        var (steps, tx, _, _) = Build();
        var json = TwoSteps.Replace("{ \"steps\"", "{ \"transaction\": false, \"steps\"");
        Assert.True((await steps.ExecuteAsync(Invocation(Params(json)), default)).Ok);
        Assert.Empty(tx.Log);
    }

    [Fact]
    public async Task A_step_handler_keeps_its_own_role_rule()
    {
        var tx = new FakeTransaction();
        var guarded = new Recorder("make.parent", roles: ["Engineer"]);
        var steps = new StepsAction(new Services(guarded), tx);
        var outcome = await steps.ExecuteAsync(Invocation(Params("""{ "steps": [ { "handler": "make.parent" } ] }""")), default);   // the user has only Admin
        Assert.False(outcome.Ok);
        Assert.Empty(guarded.Calls);
        Assert.Equal(["begin", "rollback"], tx.Log);
    }

    // ---- 594: results flow to later steps and to the redirect

    [Fact]
    public async Task A_later_step_receives_the_values_an_earlier_step_returned_next_to_the_posted_ones()
    {
        var (steps, _, _, child) = Build();
        await steps.ExecuteAsync(Invocation(Params(TwoSteps), ("name", "Ali"), ("other", "x")), default);
        var values = child.Calls[0].Values;
        Assert.Equal(("42", "first", "Ali", "x"), (values["parentId"], values["label"], values["name"], values["other"]));
    }

    [Fact]
    public async Task The_action_returns_every_step_result_by_step_name_and_the_redirect_can_use_them()
    {
        var (steps, _, _, _) = Build();
        var outcome = await steps.ExecuteAsync(Invocation(Params(TwoSteps)), default);
        Assert.Equal("42", outcome.Returned!["parent.id"]);
        Assert.Equal("P-42", outcome.Returned["parent.code"]);

        // the executor turns {parent.id} of the definition's redirect into the id the first step created
        var store = new OneAction("""{"kind":"handler","handler":"action.steps","redirect":"/Parent/Details/{parent.id}?from={name}","parameters":""" + TwoSteps + "}");
        var executor = new ActionExecutor(store, new AllowAll(), [steps]);
        var result = await executor.RunAsync("create-both", Invocation(Params("{}"), ("name", "Ali")));
        Assert.Equal(ActionStatus.Ok, result.Status);
        Assert.Equal("/Parent/Details/42?from=Ali", result.Redirect);
    }

    [Fact]
    public async Task A_single_handler_can_return_a_value_for_the_redirect_without_steps()
    {
        var create = new Recorder("make.parent", _ => ActionOutcome.Done(returned: new Dictionary<string, string?> { ["Id"] = "7" }));
        var executor = new ActionExecutor(new OneAction("""{"kind":"handler","handler":"make.parent","redirect":"/Item/{Id}"}"""), new AllowAll(), [create]);
        Assert.Equal("/Item/7", (await executor.RunAsync("create-both", Invocation(Params("{}")))).Redirect);
    }

    // ---- definition checks at load

    [Theory]
    [InlineData("""{ "steps": [] }""", "/steps", "required")]
    [InlineData("""{ "steps": [ { "handler": "nope" } ] }""", "/steps/0/handler", "unknown-handler")]
    [InlineData("""{ "steps": [ { "handler": "action.steps" } ] }""", "/steps/0/handler", "nested-steps")]
    [InlineData("""{ "steps": [ { "handler": "make.parent", "values": { "x": "step:later.id" } }, { "as": "later", "handler": "make.child" } ] }""", "/steps/0/values/x", "unknown-step")]
    [InlineData("""{ "steps": [ { "handler": "make.parent", "values": { "x": "bogus" } } ] }""", "/steps/0/values/x", "wrong-source")]
    [InlineData("""{ "steps": [ { "as": "a", "handler": "make.parent" }, { "as": "a", "handler": "make.child" } ] }""", "/steps/1/as", "duplicate")]
    [InlineData("""{ "steps": [ { "handler": "make.parent", "extra": 1 } ] }""", "/steps/0/extra", "unknown-property")]
    public void A_bad_steps_definition_is_refused_when_it_is_loaded(string parameters, string pointer, string code)
    {
        var (steps, _, _, _) = Build();
        Assert.Contains(steps.ValidateParameters(Params(parameters)), e => e.Pointer == pointer && e.Code == code);
    }

    [Fact]
    public void A_good_steps_definition_is_valid_and_too_many_steps_are_refused()
    {
        var (steps, _, _, _) = Build();
        Assert.Empty(steps.ValidateParameters(Params(TwoSteps)));
        var many = "{ \"steps\": [" + string.Join(",", Enumerable.Repeat("{ \"handler\": \"make.parent\" }", StepsAction.MaxSteps + 1)) + "] }";
        Assert.Contains(steps.ValidateParameters(Params(many)), e => e.Code == "too-many");
    }

    // ---- 595: step-up

    private sealed class Verifier(StepUpResult result) : IStepUpVerifier
    {
        public List<StepUpRequest> Requests { get; } = [];
        public Task<StepUpResult> VerifyAsync(StepUpRequest request, CancellationToken ct) { Requests.Add(request); return Task.FromResult(result); }
    }

    private const string Protected = """{"kind":"handler","handler":"permit.close","stepUp":{"resource":"Permit","action":"Close"}}""";

    [Fact]
    public async Task A_protected_action_gives_the_challenge_and_does_not_run_before_the_security_key_answered()
    {
        var handler = new Recorder("permit.close");
        var verifier = new Verifier(new StepUpResult(StepUpStatus.Challenge, "{\"challenge\":\"abc\"}"));
        var result = await new ActionExecutor(new OneAction(Protected), new AllowAll(), [handler], verifier).RunAsync("create-both", Invocation(Params("{}")));
        Assert.Equal(ActionStatus.StepUpRequired, result.Status);
        Assert.Equal("{\"challenge\":\"abc\"}", result.StepUpOptionsJson);
        Assert.Empty(handler.Calls);
        Assert.Equal(("Permit", "Close"), (verifier.Requests[0].Resource, verifier.Requests[0].Action));
    }

    [Fact]
    public async Task A_verified_answer_lets_the_operation_run_and_the_posted_answer_reaches_the_verifier()
    {
        var handler = new Recorder("permit.close");
        var verifier = new Verifier(new StepUpResult(StepUpStatus.Passed));
        var result = await new ActionExecutor(new OneAction(Protected), new AllowAll(), [handler], verifier)
            .RunAsync("create-both", Invocation(Params("{}"), ("webAuthnAssertionJson", "{a}"), ("webAuthnOptionsJson", "{o}")));
        Assert.Equal(ActionStatus.Ok, result.Status);
        Assert.Single(handler.Calls);
        Assert.Equal(("{a}", "{o}", "hamid"), (verifier.Requests[0].AssertionJson, verifier.Requests[0].OptionsJson, verifier.Requests[0].UserName));
    }

    [Fact]
    public async Task A_denied_confirmation_refuses_and_a_definition_that_needs_one_never_runs_without_a_verifier()
    {
        var handler = new Recorder("permit.close");
        var denied = await new ActionExecutor(new OneAction(Protected), new AllowAll(), [handler], new Verifier(new StepUpResult(StepUpStatus.Denied, Error: "wrong key"))).RunAsync("create-both", Invocation(Params("{}")));
        Assert.Equal(ActionStatus.Refused, denied.Status);
        var none = await new ActionExecutor(new OneAction(Protected), new AllowAll(), [handler]).RunAsync("create-both", Invocation(Params("{}")));
        Assert.Equal(ActionStatus.Refused, none.Status);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void The_stepUp_setting_of_an_action_definition_is_checked_when_it_is_loaded()
    {
        var kind = new ActionDefinitionKind([new Recorder("permit.close")]);
        Assert.Empty(kind.Validate(Protected, new DefinitionLimits()));
        Assert.Contains(kind.Validate("""{"kind":"handler","handler":"permit.close","stepUp":{"resource":"Permit"}}""", new DefinitionLimits()), e => e.Pointer == "/stepUp" && e.Code == "required");
        Assert.Contains(kind.Validate("""{"kind":"handler","handler":"permit.close","stepUp":{"resource":"P","action":"C","x":1}}""", new DefinitionLimits()), e => e.Pointer == "/stepUp/x");
        Assert.Contains(kind.Validate("""{"kind":"handler","handler":"permit.close","stepUp":true}""", new DefinitionLimits()), e => e.Code == "wrong-type");
    }

    // ---- the ambient scope

    private sealed class Factory : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public IServiceScope CreateScope() { Created++; return new Scope(); }
        private sealed class Scope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Services();
            public void Dispose() { }
        }
    }

    [Fact]
    public void Handlers_use_the_transactions_scope_while_one_is_running_and_their_own_otherwise()
    {
        var factory = new Factory();
        var ambient = new Services();
        using (var own = ActionScope.Open(factory)) Assert.NotSame(ambient, own.ServiceProvider);
        Assert.Equal(1, factory.Created);
        using (ActionScope.Enter(ambient))
        {
            using var inside = ActionScope.Open(factory);
            Assert.Same(ambient, inside.ServiceProvider);
            Assert.Equal(1, factory.Created);                                           // no new scope: the same DbContext, so the same transaction
        }
        using var after = ActionScope.Open(factory);
        Assert.NotSame(ambient, after.ServiceProvider);
        Assert.Equal(2, factory.Created);
    }

    // ---- SCRUM-799: the shipped multi-table action proves action.steps

    private static JsonObject ShippedWarehouseAction()
    {
        var file = Path.Combine(PEMS.Tests.TestSupport.SupportedReleases.RepoRoot(), "PEMS.Persistence", "Definitions", "System", "action", "core", "warehouses-create-with-location.json");
        return (JsonObject)((JsonObject)JsonNode.Parse(File.ReadAllText(file))!)["payload"]!["parameters"]!.DeepClone();
    }

    [Fact]
    public async Task The_shipped_warehouse_with_location_action_passes_the_second_step_the_id_of_the_first()
    {
        var tx = new FakeTransaction();
        var entity = new Recorder("entity.create", call => call.Parameters?["entity"]?.GetValue<string>() == "Warehouse"
            ? ActionOutcome.Done(returned: new Dictionary<string, string?> { ["WarehouseId"] = "7", ["Code"] = "W-7" })
            : ActionOutcome.Done());
        var steps = new StepsAction(new Services(entity), tx);
        var parameters = ShippedWarehouseAction();
        Assert.Empty(steps.ValidateParameters(parameters));
        var outcome = await steps.ExecuteAsync(Invocation(parameters, ("code", "W-7"), ("name", "Main"), ("locationCode", "A1"), ("locationName", "Aisle 1")), default);
        Assert.True(outcome.Ok);
        Assert.Equal(["begin", "commit"], tx.Log);
        Assert.Equal(2, entity.Calls.Count);
        Assert.Equal("7", entity.Calls[1].Values["warehouseId"]);       // the new warehouse id reaches the location through step:warehouse.WarehouseId
    }

    [Fact]
    public async Task A_refusal_in_the_second_step_of_the_shipped_action_rolls_the_first_step_back()
    {
        var tx = new FakeTransaction();
        var entity = new Recorder("entity.create", call => call.Parameters?["entity"]?.GetValue<string>() == "Warehouse"
            ? ActionOutcome.Done(returned: new Dictionary<string, string?> { ["WarehouseId"] = "7" })
            : ActionOutcome.Refused(TranslatableText.Of(("en", "Code is required."), ("fa", "کد الزامی است."))));
        var steps = new StepsAction(new Services(entity), tx);
        var outcome = await steps.ExecuteAsync(Invocation(ShippedWarehouseAction(), ("code", "W-7"), ("name", "Main")), default);
        Assert.False(outcome.Ok);
        Assert.Equal(["begin", "rollback"], tx.Log);                    // step 1 ran, step 2 refused: the transaction is rolled back, so the warehouse is not written
        Assert.Equal(2, entity.Calls.Count);
    }

    // ---- fakes of the definition store and access

    private sealed class OneAction(string payload) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult<PublishedDefinition?>(kind == "action" ? new PublishedDefinition(kind, key, appKey, 1, payload, "h") : null);
    }

    private sealed class AllowAll : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }
}
