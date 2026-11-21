using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-437: data sources and actions are definitions that name registered code; unknown names are refused at load, the rights are checked before anything runs, the options of a form come from its data source.</summary>
public sealed class DefinitionActionsTests
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

    private sealed class Handler(string key, ActionOutcome outcome, IReadOnlyCollection<string>? roles = null) : IActionHandler
    {
        public int Runs;
        public ActionInvocation? Last;
        public string Key => key;
        public IReadOnlyCollection<string>? AllowedRoles => roles;
        public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct) { Runs++; Last = invocation; return Task.FromResult(outcome); }
    }

    private sealed class Provider(string key, params string[] values) : IDataSourceProvider
    {
        public string Key => key;
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public DataSourceRequest? Last;
        public Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult<IReadOnlyList<FieldOption>>(values.Select(v => new FieldOption { Value = v, Label = TranslatableText.Literal(v) }).ToList());
        }
    }

    private static PublishedDefinition Def(string kind, string key, string payload) => new(kind, key, "core", 1, payload, "h");
    private static readonly DefinitionLimits Limits = new();
    private static readonly string[] None = [];

    // ---- kinds

    [Fact]
    public void A_data_source_is_static_with_translated_items_or_a_registered_provider()
    {
        var kind = new DataSourceDefinitionKind([new Provider("departments")]);
        Assert.Empty(kind.Validate("""{"kind":"static","items":[{"value":"a","label":{"fa":"الف","en":"A"}}]}""", Limits));
        Assert.Empty(kind.Validate("""{"kind":"provider","provider":"departments","parameters":{"x":"1"}}""", Limits));
        var codes = kind.Validate("""{"kind":"provider","provider":"ghost"}""", Limits).Select(e => e.Code);
        Assert.Contains("unknown-provider", codes);
        Assert.Contains("missing-translation", kind.Validate("""{"kind":"static","items":[{"value":"a","label":{"en":"A"}}]}""", Limits).Select(e => e.Code));
        Assert.Contains("duplicate", kind.Validate("""{"kind":"static","items":[{"value":"a","label":"x"},{"value":"a","label":"y"}]}""", Limits).Select(e => e.Code));
        Assert.Contains("unknown-property", kind.Validate("""{"kind":"static","items":[{"value":"a","label":"x"}],"sql":"select 1"}""", Limits).Select(e => e.Code));
        Assert.Contains("overlap", kind.Validate("""{"kind":"provider","provider":"departments","items":[]}""", Limits).Select(e => e.Code));
    }

    [Fact]
    public void An_action_names_a_registered_handler_and_a_local_redirect()
    {
        var kind = new ActionDefinitionKind([new Handler("department.create", ActionOutcome.Done())]);
        Assert.Empty(kind.Validate("""{"kind":"handler","handler":"department.create","success":{"fa":"شد","en":"Done"},"redirect":"/Department"}""", Limits));
        Assert.Contains("unknown-handler", kind.Validate("""{"kind":"handler","handler":"drop.database"}""", Limits).Select(e => e.Code));
        Assert.Contains("invalid-redirect", kind.Validate("""{"kind":"handler","handler":"department.create","redirect":"https://evil.example"}""", Limits).Select(e => e.Code));
        Assert.Contains("invalid-redirect", kind.Validate("""{"kind":"handler","handler":"department.create","redirect":"//evil.example"}""", Limits).Select(e => e.Code));
        Assert.Contains("missing-translation", kind.Validate("""{"kind":"handler","handler":"department.create","success":{"en":"Done"}}""", Limits).Select(e => e.Code));
        Assert.Contains("unknown-property", kind.Validate("""{"kind":"handler","handler":"department.create","script":"x"}""", Limits).Select(e => e.Code));
    }

    // ---- executor

    private const string ActionJson = """{"kind":"handler","handler":"h","permission":"Admin.Create","success":{"fa":"انجام شد","en":"Done"},"redirect":"/Done"}""";

    private static ActionInvocation Invocation(params string[] roles) => new(new Dictionary<string, string?> { ["name"] = "x" }, "en", roles, "hamid");

    [Fact]
    public async Task An_allowed_action_runs_the_handler_with_the_values_and_returns_the_message_and_redirect_of_the_definition()
    {
        var handler = new Handler("h", ActionOutcome.Done());
        var executor = new ActionExecutor(new Store(Def("action", "a", ActionJson)), new Access((p, r) => p == "Admin.Create" && r.Contains("Admin")), [handler]);
        var result = await executor.RunAsync("a", Invocation("Admin"));
        Assert.Equal(ActionStatus.Ok, result.Status);
        Assert.Equal("Done", result.Message!.Resolve(new System.Globalization.CultureInfo("en")));
        Assert.Equal("انجام شد", result.Message.Resolve(new System.Globalization.CultureInfo("fa")));
        Assert.Equal("/Done", result.Redirect);
        Assert.Equal("x", handler.Last!.Values["name"]);
    }

    [Fact]
    public async Task Without_the_permission_or_the_handler_role_nothing_runs()
    {
        var handler = new Handler("h", ActionOutcome.Done(), ["Boss"]);
        var executor = new ActionExecutor(new Store(Def("action", "a", ActionJson)), new Access((p, r) => r.Contains("Admin")), [handler]);
        Assert.Equal(ActionStatus.Forbidden, (await executor.RunAsync("a", Invocation("Other"))).Status);   // the definition's permission
        Assert.Equal(ActionStatus.Forbidden, (await executor.RunAsync("a", Invocation("Admin"))).Status);   // the handler's roles
        Assert.Equal(0, handler.Runs);
    }

    [Fact]
    public async Task A_missing_action_or_handler_is_not_found_and_a_refused_action_returns_its_errors()
    {
        var refused = new Handler("h", ActionOutcome.Refused("name", TranslatableText.Of(("en", "Taken"), ("fa", "تکراری"))));
        var executor = new ActionExecutor(new Store(Def("action", "a", ActionJson), Def("action", "b", """{"kind":"handler","handler":"ghost"}""")), new Access((_, _) => true), [refused]);
        Assert.Equal(ActionStatus.NotFound, (await executor.RunAsync("nope", Invocation())).Status);
        Assert.Equal(ActionStatus.NotFound, (await executor.RunAsync("b", Invocation())).Status);
        var result = await executor.RunAsync("a", Invocation("Admin"));
        Assert.Equal(ActionStatus.Refused, result.Status);
        Assert.Equal("Taken", result.Outcome!.FieldErrors!["name"].Resolve(new System.Globalization.CultureInfo("en")));
        Assert.Null(result.Redirect);
    }

    // ---- hydrator

    private static ResolvedDefinition FormWith(params string[] optionsFrom)
    {
        var fields = string.Join(",", optionsFrom.Select((k, i) => $"{{\"name\":\"f{i}\",\"optionsFrom\":\"{k}\"}}"));
        return new ResolvedDefinition(Def("form", "f", "{}"), System.Text.Json.Nodes.JsonNode.Parse($"{{\"fields\":[{fields},{{\"name\":\"plain\"}}]}}")!);
    }

    private static PipelineRequest Req(params string[] roles) => new("form", "f", "core", "en", roles, "hamid");

    [Fact]
    public async Task A_field_gets_its_options_from_a_static_source_or_a_provider_with_the_request()
    {
        var provider = new Provider("deps", "1", "2");
        var store = new Store(Def("data-source", "st", """{"kind":"static","items":[{"value":"a","label":{"fa":"الف","en":"A"}}]}"""), Def("data-source", "dp", """{"kind":"provider","provider":"deps","parameters":{"active":"true"}}"""));
        var hydrator = new FormOptionsHydrator(store, new Access((_, _) => true), [provider]);
        var form = FormWith("st", "dp");
        await hydrator.HydrateAsync(form, Req("Admin"));
        var options = (IReadOnlyDictionary<string, IReadOnlyList<FieldOption>>)form.Hydrated[FormOptionsHydrator.OptionsKey]!;
        Assert.Equal(["a"], options["f0"].Select(o => o.Value));
        Assert.Equal(["1", "2"], options["f1"].Select(o => o.Value));
        Assert.False(options.ContainsKey("plain"));
        Assert.Equal("true", provider.Last!.Parameters["active"]);
        Assert.Equal(["Admin"], provider.Last.Roles);
    }

    [Fact]
    public async Task A_source_the_user_may_not_read_or_that_does_not_exist_gives_no_options_and_a_problem()
    {
        var store = new Store(Def("data-source", "secret", """{"kind":"static","permission":"Hr.View","items":[{"value":"a","label":"A"}]}"""),
            Def("data-source", "roles", """{"kind":"provider","provider":"guarded"}"""));
        var guarded = new GuardedProvider();
        var hydrator = new FormOptionsHydrator(store, new Access((p, _) => p is null), [guarded]);
        var form = FormWith("secret", "ghost", "roles");
        await hydrator.HydrateAsync(form, Req("Operator"));
        var options = (IReadOnlyDictionary<string, IReadOnlyList<FieldOption>>)form.Hydrated[FormOptionsHydrator.OptionsKey]!;
        Assert.All(options.Values, v => Assert.Empty(v));
        Assert.Equal(3, ((List<string>)form.Hydrated[FormOptionsHydrator.ProblemsKey]!).Count);
        Assert.Equal(0, guarded.Loads);   // the provider was never asked
    }

    private sealed class GuardedProvider : IDataSourceProvider
    {
        public int Loads;
        public string Key => "guarded";
        public IReadOnlyCollection<string>? AllowedRoles => ["Boss"];
        public Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct) { Loads++; return Task.FromResult<IReadOnlyList<FieldOption>>([]); }
    }

    [Fact]
    public async Task Only_forms_are_hydrated()
    {
        var hydrator = new FormOptionsHydrator(new Store(), new Access((_, _) => true), []);
        var page = new ResolvedDefinition(Def("page", "p", "{}"), System.Text.Json.Nodes.JsonNode.Parse("{\"fields\":[{\"name\":\"a\",\"optionsFrom\":\"x\"}]}")!);
        await hydrator.HydrateAsync(page, Req());
        Assert.Empty(page.Hydrated);
    }

    [Fact]
    public void A_revealed_secret_is_a_successful_outcome_carrying_the_value_and_no_message_or_redirect()
    {
        var secret = new ActionSecret(TranslatableText.Literal("t"), null, [new SecretItem(TranslatableText.Literal("l"), "abc")], null, SecretTexts.Print);
        var outcome = ActionOutcome.Revealed(secret);
        Assert.True(outcome.Ok);
        Assert.Same(secret, outcome.Secret);
        Assert.Null(outcome.Message);
        Assert.Null(outcome.Redirect);
    }
}
