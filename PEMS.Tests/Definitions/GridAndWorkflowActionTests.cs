using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Tests.TestSupport;
using PEMS.UI.Abstractions;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;
using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-437: a list (grid rows from a data source definition) and a button (a workflow action) from stored definitions only; the rights are checked before anything is read or run.</summary>
public sealed class GridAndWorkflowActionTests
{
    private sealed class Store(params PublishedDefinition[] items) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult(items.FirstOrDefault(i => i.Kind == kind && i.Key == key));
    }

    private sealed class Access(Func<string?, IReadOnlyCollection<string>, bool> rule) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(rule(permission, roles));
    }

    private sealed class Rows(string key = "people", IReadOnlyCollection<string>? roles = null) : IGridDataProvider
    {
        public GridDataRequest? Last;
        public int Calls;
        public bool Throw;
        public string Key => key;
        public IReadOnlyCollection<string>? AllowedRoles => roles;
        public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
        {
            Calls++; Last = request;
            if (Throw) throw new GridQueryException("unknown field");
            return Task.FromResult<(IReadOnlyList<object>, int)>((new object[] { new { name = "A" }, new { name = "B" } }, 42));
        }
    }

    private static PublishedDefinition Def(string key, string payload) => new("data-source", key, "core", 1, payload, "h");
    private static KeyValuePair<string, string?>[] Q(params (string, string)[] p) => p.Select(x => new KeyValuePair<string, string?>(x.Item1, x.Item2)).ToArray();

    // ---- the kind

    [Fact]
    public void A_grid_data_source_names_a_registered_grid_provider_and_nothing_else()
    {
        var kind = new DataSourceDefinitionKind([], [new Rows("people")]);
        Assert.Empty(kind.Validate("""{"kind":"grid","provider":"people","permission":"Hr.View","parameters":{"active":"true"}}""", new DefinitionLimits()));
        Assert.Contains("unknown-provider", kind.Validate("""{"kind":"grid","provider":"ghost"}""", new DefinitionLimits()).Select(e => e.Code));
        Assert.Contains("overlap", kind.Validate("""{"kind":"grid","provider":"people","items":[]}""", new DefinitionLimits()).Select(e => e.Code));
        Assert.Contains("required", kind.Validate("""{"kind":"grid"}""", new DefinitionLimits()).Select(e => e.Code));
    }

    // ---- the executor

    [Fact]
    public async Task The_rows_come_from_the_provider_with_the_parsed_grid_query_the_user_and_the_parameters_of_the_definition()
    {
        var provider = new Rows();
        var executor = new DataSourceExecutor(new Store(Def("p", """{"kind":"grid","provider":"people","parameters":{"active":"true"}}""")), new Access((_, _) => true), [provider]);
        var result = await executor.QueryAsync("p", Q(("page", "2"), ("pageSize", "10"), ("sort", "-name"), ("q", "ali")), "fa", ["Admin"], "hamid");
        Assert.Equal((DataStatus.Ok, 42, 2), (result.Status, result.Total, result.Items!.Count));
        var grid = provider.Last!.Grid;
        Assert.Equal((2, 10, "ali"), (grid.Page, grid.PageSize, grid.Search));
        Assert.Equal(("name", true), (grid.Sort[0].Field, grid.Sort[0].Descending));
        Assert.Equal(("hamid", "fa", "true"), (provider.Last.UserName, provider.Last.Culture, provider.Last.Parameters["active"]));
    }

    [Fact]
    public async Task Without_the_permission_or_the_provider_role_nothing_is_read()
    {
        var provider = new Rows("people", ["Boss"]);
        var store = new Store(Def("p", """{"kind":"grid","provider":"people","permission":"Hr.View"}"""));
        Assert.Equal(DataStatus.Forbidden, (await new DataSourceExecutor(store, new Access((perm, _) => perm is null), [provider]).QueryAsync("p", [], "en", ["Boss"], "x")).Status);   // the definition's permission
        Assert.Equal(DataStatus.Forbidden, (await new DataSourceExecutor(store, new Access((_, _) => true), [provider]).QueryAsync("p", [], "en", ["Other"], "x")).Status);          // the provider's roles
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task An_unknown_key_a_source_of_options_and_a_bad_query_are_answered_without_reading()
    {
        var provider = new Rows();
        var executor = new DataSourceExecutor(new Store(Def("opts", """{"kind":"static","items":[{"value":"a","label":"A"}]}"""), Def("p", """{"kind":"grid","provider":"people"}""")), new Access((_, _) => true), [provider]);
        Assert.Equal(DataStatus.NotFound, (await executor.QueryAsync("nope", [], "en", [], null)).Status);
        Assert.Equal(DataStatus.NotFound, (await executor.QueryAsync("opts", [], "en", [], null)).Status);          // a list of options is not served as rows
        provider.Throw = true;
        Assert.Equal(DataStatus.Invalid, (await executor.QueryAsync("p", [], "en", [], null)).Status);              // a field the provider does not declare
    }

    [Fact]
    public async Task A_grid_source_is_not_offered_as_the_options_of_a_field()
    {
        var store = new Store(Def("p", """{"kind":"grid","provider":"people"}"""));
        var hydrator = new FormOptionsHydrator(store, new Access((_, _) => true), []);
        var form = new ResolvedDefinition(new PublishedDefinition("form", "f", "core", 1, "{}", "h"), JsonNode.Parse("""{"fields":[{"name":"x","optionsFrom":"p"}]}""")!);
        await hydrator.HydrateAsync(form, new PipelineRequest("form", "f", "core", "en", [], null));
        Assert.Empty(((IReadOnlyDictionary<string, IReadOnlyList<FieldOption>>)form.Hydrated[FormOptionsHydrator.OptionsKey]!)["x"]);
        Assert.Single((List<string>)form.Hydrated[FormOptionsHydrator.ProblemsKey]!);
    }

    // ---- the host

    private static DataHostController Host(DataSourceExecutor executor, string query = "")
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")) };
        http.Request.QueryString = new QueryString(query);
        return new DataHostController(executor, new FakeCurrentUserService { UserName = "hamid" }) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public async Task The_data_host_answers_items_and_total_403_404_and_400()
    {
        var store = new Store(Def("p", """{"kind":"grid","provider":"people","permission":"Hr.View"}"""));
        var allowed = new DataSourceExecutor(store, new Access((_, _) => true), [new Rows()]);
        var ok = Assert.IsType<JsonResult>(await Host(allowed, "?page=1&pageSize=5").Rows("p", CancellationToken.None));
        var doc = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        Assert.Equal(42, (int)doc["total"]!);
        Assert.Equal("A", (string)doc["items"]![0]!["name"]!);

        Assert.IsType<NotFoundResult>(await Host(allowed).Rows("nope", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Host(allowed).Rows("../x", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await Host(new DataSourceExecutor(store, new Access((_, _) => true), [new Rows() { Throw = true }])).Rows("p", CancellationToken.None));   // a field the provider does not declare
        var refused = new DataSourceExecutor(store, new Access((_, _) => false), [new Rows()]);
        Assert.IsType<ForbidResult>(await Host(refused).Rows("p", CancellationToken.None));
    }

    // ---- the workflow action

    private sealed class Runtime(WorkflowInstanceDto? instance) : IWorkflowRuntime
    {
        public (int Id, string Action, int? Employee, string? Comment)? Executed;
        public Exception? Throws;
        public Task<int> StartInstanceAsync(string module, string businessObjectType, int businessObjectId, string workflowCategory, int? projectId, int? initiatedByEmployeeId, IReadOnlyList<string>? firstStageParticipantRoles = null) => throw new NotSupportedException();
        public Task ExecuteActionAsync(int workflowInstanceId, string actionType, int? actorEmployeeId, IReadOnlyList<string> actorRoles, string? comment, string? ipAddress, string? userAgent, IReadOnlyList<string>? nextStageParticipantRoles = null)
        {
            if (Throws is not null) throw Throws;
            Executed = (workflowInstanceId, actionType, actorEmployeeId, comment);
            return Task.CompletedTask;
        }
        public Task<WorkflowInstanceDto?> GetInstanceAsync(string businessObjectType, int businessObjectId) => Task.FromResult(instance);
        public Task<WorkflowInstanceDto?> GetInstanceByIdAsync(int workflowInstanceId) => Task.FromResult(instance);
        public Task<IReadOnlyList<WorkflowHistoryDto>> GetHistoryAsync(int workflowInstanceId) => Task.FromResult<IReadOnlyList<WorkflowHistoryDto>>([]);
        public Task<IReadOnlyList<PendingTaskDto>> GetPendingTasksAsync(int employeeId, IReadOnlyList<string> actorRoles) => Task.FromResult<IReadOnlyList<PendingTaskDto>>([]);
    }

    private sealed class ReadModel(params WorkflowAvailableAction[] actions) : IWorkflowReadModelService
    {
        public Task<WorkflowRuntimeViewModel?> GetRuntimeViewAsync(int workflowInstanceId, WorkflowUserContext user, CancellationToken ct = default) => Task.FromResult<WorkflowRuntimeViewModel?>(null);
        public Task<IReadOnlyList<WorkflowAvailableAction>> GetAvailableActionsAsync(int workflowInstanceId, WorkflowUserContext user, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowAvailableAction>>(actions);
        public Task<IReadOnlyList<WorkflowInstance>> FindInstancesForEntityAsync(string module, string businessObjectType, int businessObjectId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WorkflowInstance>>([]);
        public Task<WorkflowInstance?> GetCurrentInstanceForEntityAsync(string module, string businessObjectType, int businessObjectId, CancellationToken ct = default) => Task.FromResult<WorkflowInstance?>(null);
        public Task<WorkflowRuntimeViewModel?> GetRuntimeViewForEntityAsync(string module, string businessObjectType, int businessObjectId, WorkflowUserContext user, CancellationToken ct = default) => Task.FromResult<WorkflowRuntimeViewModel?>(null);
    }

    private static WorkflowExecuteAction Handler(Runtime runtime, ReadModel readModel, string db = "wf")
    {
        var services = new ServiceCollection().AddSingleton<IWorkflowRuntime>(runtime).AddSingleton<IWorkflowReadModelService>(readModel).AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).BuildServiceProvider();
        return new WorkflowExecuteAction(services.GetRequiredService<IServiceScopeFactory>());
    }

    private static ActionInvocation Invoke(params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["Reviewer"], "nobody", JsonNode.Parse("""{"objectTypes":["Permit"]}""")!.AsObject());

    private static WorkflowInstanceDto Instance(string type = "Permit", string status = "Running") => new() { WorkflowInstanceId = 5, BusinessObjectType = type, Status = status };
    private static WorkflowAvailableAction Approve(bool allowed = true) => new() { ActionType = "Approve", IsAllowed = allowed, DeniedReason = "Not your stage" };

    [Fact]
    public void The_handler_needs_the_business_object_types_it_may_act_on()
    {
        var handler = Handler(new Runtime(null), new ReadModel());
        Assert.NotEmpty(handler.ValidateParameters(null));
        Assert.NotEmpty(handler.ValidateParameters(JsonNode.Parse("""{"objectTypes":[]}""")!.AsObject()));
        Assert.NotEmpty(handler.ValidateParameters(JsonNode.Parse("""{"objectTypes":[1]}""")!.AsObject()));
        Assert.Empty(handler.ValidateParameters(JsonNode.Parse("""{"objectTypes":["Permit"]}""")!.AsObject()));
        var kind = new ActionDefinitionKind([handler]);
        Assert.Contains("required", kind.Validate("""{"kind":"handler","handler":"workflow.execute"}""", new DefinitionLimits()).Select(e => e.Code));
        Assert.Empty(kind.Validate("""{"kind":"handler","handler":"workflow.execute","parameters":{"objectTypes":["Permit"]}}""", new DefinitionLimits()));
    }

    [Fact]
    public async Task An_action_the_engine_offers_is_run_through_the_runtime_with_the_comment()
    {
        var runtime = new Runtime(Instance());
        var outcome = await Handler(runtime, new ReadModel(Approve())).ExecuteAsync(Invoke(("instanceId", "5"), ("actionType", "Approve"), ("comment", "  fine  ")), CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Equal((5, "Approve", (int?)null, "fine"), runtime.Executed!.Value);
    }

    [Fact]
    public async Task Everything_the_engine_or_the_definition_does_not_allow_is_refused_and_nothing_runs()
    {
        var runtime = new Runtime(Instance());
        Task<ActionOutcome> Run(Runtime r, ReadModel m, params (string, string?)[] v) => Handler(r, m).ExecuteAsync(Invoke(v), CancellationToken.None);

        Assert.False((await Run(runtime, new ReadModel(Approve()), ("actionType", "Approve"))).Ok);                                   // no instance
        Assert.False((await Run(runtime, new ReadModel(Approve()), ("instanceId", "5"))).Ok);                                         // no action
        Assert.False((await Run(runtime, new ReadModel(Approve()), ("instanceId", "5"), ("actionType", "Reject"), ("comment", ""))).Ok);   // a reject needs a reason
        Assert.False((await Run(runtime, new ReadModel(Approve(false)), ("instanceId", "5"), ("actionType", "Approve"))).Ok);         // denied for this user
        Assert.False((await Run(runtime, new ReadModel(), ("instanceId", "5"), ("actionType", "Approve"))).Ok);                        // not offered at this stage
        Assert.False((await Run(new Runtime(Instance("Contract")), new ReadModel(Approve()), ("instanceId", "5"), ("actionType", "Approve"))).Ok);   // object type not listed
        Assert.False((await Run(new Runtime(Instance(status: "Completed")), new ReadModel(Approve()), ("instanceId", "5"), ("actionType", "Approve"))).Ok);
        Assert.False((await Run(new Runtime(null), new ReadModel(Approve()), ("instanceId", "5"), ("actionType", "Approve"))).Ok);
        Assert.Null(runtime.Executed);

        var throwing = new Runtime(Instance()) { Throws = new UnauthorizedAccessException("Segregation of duties.") };
        var refused = await Run(throwing, new ReadModel(Approve()), ("instanceId", "5"), ("actionType", "Approve"));
        Assert.False(refused.Ok);
        Assert.Equal("Segregation of duties.", refused.FormError!.Resolve(new System.Globalization.CultureInfo("en")));
    }
}
