using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>Lifecycle, concurrency, validation, rollback, references, cache stamp and packages of the definition store (SCRUM-434).</summary>
public class DefinitionServiceTests
{
    private const string User = "tester";
    private static readonly string Button = """{"label":{"en":"Save"},"tone":"primary"}""";

    private static async Task<int> CreatePublishedFormAsync(DefinitionTestWorld w, string key = "items.new")
    {
        var (s, _, _) = w.NewService();
        var created = await s.CreateAsync(DefinitionTestWorld.CreateForm(key), User);
        var draft = await s.GetDraftAsync(created.Value!.Summary.Id);
        var published = await s.PublishAsync(created.Value.Summary.Id, draft.Value!.Info.Revision, User);
        Assert.True(published.Success, string.Join(";", published.Errors?.Select(e => e.Pointer + e.Code) ?? []));
        return created.Value.Summary.Id;
    }

    [Fact]
    public async Task Create_makes_the_envelope_and_a_draft_version_one()
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var r = await s.CreateAsync(DefinitionTestWorld.CreateForm(), User);
        Assert.True(r.Success);
        var d = r.Value!;
        Assert.Equal(("form", "items.new", "core", DefinitionOrigin.Customer, null, true), (d.Summary.Kind, d.Summary.Key, d.Summary.AppKey, d.Summary.Origin, d.Summary.PublishedVersion, d.Summary.HasDraft));
        var v = Assert.Single(d.Versions);
        Assert.Equal((1, DefinitionStatus.Draft, 64), (v.Number, v.Status, v.ContentHash.Length));
    }

    [Theory]
    [InlineData("nope", "a.b", "/kind")]
    [InlineData("form", "Bad Key", "/key")]
    [InlineData("form", "", "/key")]
    public async Task Create_rejects_an_unregistered_kind_and_a_bad_key(string kind, string key, string pointer)
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var r = await s.CreateAsync(DefinitionTestWorld.CreateForm(key) with { Kind = kind }, User);
        Assert.Equal(DefinitionFailure.Invalid, r.Failure);
        Assert.Contains(r.Errors!, e => e.Pointer == pointer);
    }

    [Fact]
    public async Task Create_with_a_malformed_or_oversized_payload_is_invalid()
    {
        var w = new DefinitionTestWorld { Limits = new DefinitionLimits { MaxPayloadBytes = 50 } };
        var (s, _, _) = w.NewService();
        Assert.Contains((await s.CreateAsync(DefinitionTestWorld.CreateForm(payload: "{ not json"), User)).Errors!, e => e.Code == "malformed");
        Assert.Contains((await s.CreateAsync(DefinitionTestWorld.CreateForm(payload: "[1]"), User)).Errors!, e => e.Code == "type");
        Assert.Contains((await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Errors!, e => e.Code == "too-large");
    }

    [Fact]
    public async Task Create_with_a_used_key_is_a_conflict()
    {
        var w = new DefinitionTestWorld();
        var (s, _, _) = w.NewService();
        Assert.True((await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Success);
        var again = await w.NewService().Service.CreateAsync(DefinitionTestWorld.CreateForm(), User);
        Assert.Equal((DefinitionFailure.Conflict, "duplicate-key"), (again.Failure, again.Code));
    }

    [Fact]
    public async Task Payload_is_stored_canonically_so_property_order_does_not_change_the_hash()
    {
        var w = new DefinitionTestWorld();
        var (s, _, _) = w.NewService();
        var a = await s.CreateAsync(DefinitionTestWorld.CreateForm("a.one", """{"b":1,"a":{"y":2,"x":1}}"""), User);
        var b = await s.CreateAsync(DefinitionTestWorld.CreateForm("a.two", """{ "a": {"x":1, "y":2}, "b": 1 }"""), User);
        Assert.Equal(a.Value!.Versions[0].ContentHash, b.Value!.Versions[0].ContentHash);
        Assert.Equal("""{"a":{"x":1,"y":2},"b":1}""", (await s.GetDraftAsync(a.Value.Summary.Id)).Value!.PayloadJson);
    }

    [Fact]
    public async Task Saving_a_draft_needs_the_current_revision_and_resets_validation()
    {
        var w = new DefinitionTestWorld();
        var (s, _, _) = w.NewService();
        var id = (await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!.Summary.Id;
        var draft = (await s.GetDraftAsync(id)).Value!;

        var validated = await s.ValidateAsync(id, draft.Info.Revision, User);
        Assert.Equal(DefinitionStatus.Validated, validated.Value!.Info.Status);

        var stale = await s.SaveDraftAsync(id, DefinitionTestWorld.Form, null, draft.Info.Revision, User);   // revision moved on after validate
        Assert.Equal(DefinitionFailure.Concurrency, stale.Failure);

        var saved = await s.SaveDraftAsync(id, DefinitionTestWorld.Form.Replace("Item form", "Item"), "edit", validated.Value.Info.Revision, User);
        Assert.True(saved.Success);
        Assert.Equal((DefinitionStatus.Draft, "edit"), (saved.Value!.Info.Status, saved.Value.Info.ChangeSummary));
        Assert.True(saved.Value.Info.Revision > validated.Value.Info.Revision);
    }

    [Fact]
    public async Task Two_users_editing_the_same_draft_second_one_is_refused()
    {
        var w = new DefinitionTestWorld();
        var id = (await w.NewService().Service.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!.Summary.Id;
        var (a, _, _) = w.NewService();
        var (b, _, _) = w.NewService();
        var rev = (await a.GetDraftAsync(id)).Value!.Info.Revision;

        Assert.True((await a.SaveDraftAsync(id, DefinitionTestWorld.Form, "A", rev, "ann")).Success);
        var second = await b.SaveDraftAsync(id, DefinitionTestWorld.Form, "B", rev, "bob");
        Assert.Equal(DefinitionFailure.Concurrency, second.Failure);
        Assert.Equal("A", (await a.GetDraftAsync(id)).Value!.Info.ChangeSummary);
    }

    [Fact]
    public async Task A_concurrent_write_that_slips_past_the_check_is_caught_by_the_database_token()
    {
        var w = new DefinitionTestWorld();
        var id = (await w.NewService().Service.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!.Summary.Id;
        var (s, _, db) = w.NewService();
        var rev = (await s.GetDraftAsync(id)).Value!.Info.Revision;

        // another user changes the row after this service read it but before it saves
        await using (var other = w.NewContext())
        {
            var row = await other.DefinitionVersions.FirstAsync(v => v.DefinitionId == id);
            row.Revision++;
            await other.SaveChangesAsync();
        }
        var r = await s.SaveDraftAsync(id, DefinitionTestWorld.Form, null, rev, User);
        Assert.Equal(DefinitionFailure.Concurrency, r.Failure);
    }

    [Fact]
    public async Task Validate_reports_form_errors_with_pointers_and_changes_nothing()
    {
        var w = new DefinitionTestWorld();
        var (s, _, _) = w.NewService();
        var id = (await s.CreateAsync(DefinitionTestWorld.CreateForm(payload: """{"id":"x","title":"T","action":"/x","fields":[],"submitLabel":"S","extra":1}"""), User)).Value!.Summary.Id;
        var rev = (await s.GetDraftAsync(id)).Value!.Info.Revision;
        var r = await s.ValidateAsync(id, rev, User);
        Assert.Equal(DefinitionFailure.Invalid, r.Failure);
        Assert.NotEmpty(r.Errors!);
        var again = (await s.GetDraftAsync(id)).Value!.Info;
        Assert.Equal((DefinitionStatus.Draft, rev), (again.Status, again.Revision));
    }

    [Fact]
    public async Task Form_validation_rejects_duplicate_field_names()
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var payload = """{"id":"i","title":"T","action":"/x","fields":[{"name":"a","label":"A"},{"name":"A","label":"B"}],"submitLabel":"S"}""";
        var id = (await s.CreateAsync(DefinitionTestWorld.CreateForm(payload: payload), User)).Value!.Summary.Id;
        var r = await s.ValidateAsync(id, (await s.GetDraftAsync(id)).Value!.Info.Revision, User);
        Assert.Contains(r.Errors!, e => e.Pointer == "/fields/1/name" && e.Code == "duplicate");
    }

    [Fact]
    public async Task Publish_makes_the_version_immutable_current_and_bumps_the_stamp()
    {
        var w = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(w);
        var (s, _, db) = w.NewService();
        var d = (await s.GetAsync(id)).Value!;
        Assert.Equal((1, false), (d.Summary.PublishedVersion, d.Summary.HasDraft));
        Assert.Equal(DefinitionStatus.Published, d.Versions[0].Status);
        Assert.Equal(1, (await db.DefinitionStamps.SingleAsync(x => x.Kind == "form")).Value);
        Assert.Equal(DefinitionFailure.NotFound, (await s.GetDraftAsync(id)).Failure);
    }

    [Fact]
    public async Task Editing_a_published_definition_goes_through_a_new_draft_and_retires_the_old_version()
    {
        var w = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(w);
        var (s, _, _) = w.NewService();
        var env = (await s.GetAsync(id)).Value!.Summary.Revision;

        var draft = await s.CreateDraftAsync(id, null, env, User);
        Assert.Equal((2, DefinitionStatus.Draft, 1), (draft.Value!.Info.Number, draft.Value.Info.Status, draft.Value.Info.ClonedFromNumber));
        var second = await s.CreateDraftAsync(id, null, (await s.GetAsync(id)).Value!.Summary.Revision, User);
        Assert.Equal((DefinitionFailure.Conflict, "draft-exists"), (second.Failure, second.Code));

        var edited = await s.SaveDraftAsync(id, DefinitionTestWorld.Form.Replace("Item", "Thing"), "rename", draft.Value.Info.Revision, User);
        var published = await s.PublishAsync(id, edited.Value!.Info.Revision, User);
        Assert.Equal(new[] { DefinitionStatus.Retired, DefinitionStatus.Published }, published.Value!.Versions.Select(v => v.Status));
        Assert.Equal(2, published.Value.Summary.PublishedVersion);
    }

    [Fact]
    public async Task Rollback_publishes_an_older_version_again_as_a_new_number()
    {
        var w = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(w);
        var (s, _, _) = w.NewService();
        var d = await s.CreateDraftAsync(id, null, (await s.GetAsync(id)).Value!.Summary.Revision, User);
        var saved = await s.SaveDraftAsync(id, DefinitionTestWorld.Form.Replace("Item", "Thing"), null, d.Value!.Info.Revision, User);
        await s.PublishAsync(id, saved.Value!.Info.Revision, User);

        var env = (await s.GetAsync(id)).Value!.Summary.Revision;
        var back = await s.RollbackAsync(id, 1, env, User);
        Assert.True(back.Success);
        Assert.Equal(3, back.Value!.Summary.PublishedVersion);
        var v3 = back.Value.Versions.Single(v => v.Number == 3);
        Assert.Equal((DefinitionStatus.Published, 1), (v3.Status, v3.ClonedFromNumber));
        Assert.Equal(back.Value.Versions.Single(v => v.Number == 1).ContentHash, v3.ContentHash);
        Assert.Equal(DefinitionStatus.Retired, back.Value.Versions.Single(v => v.Number == 2).Status);
        Assert.Equal(DefinitionStatus.Retired, back.Value.Versions.Single(v => v.Number == 1).Status);   // history is never rewritten

        var again = await s.RollbackAsync(id, 3, (await s.GetAsync(id)).Value!.Summary.Revision, User);
        Assert.Equal((DefinitionFailure.Conflict, "already-published"), (again.Failure, again.Code));
        var missing = await s.RollbackAsync(id, 9, (await s.GetAsync(id)).Value!.Summary.Revision, User);
        Assert.Equal("version-not-found", missing.Code);
    }

    [Fact]
    public async Task Rollback_with_a_stale_envelope_revision_is_refused()
    {
        var w = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(w);
        var (s, _, _) = w.NewService();
        Assert.Equal(DefinitionFailure.Concurrency, (await s.RollbackAsync(id, 1, 1, User)).Failure);
    }

    [Fact]
    public async Task Discarding_a_draft_removes_it_but_the_only_version_cannot_be_discarded()
    {
        var w = new DefinitionTestWorld();
        var (s, _, db) = w.NewService();
        var only = (await s.CreateAsync(DefinitionTestWorld.CreateForm("a.only"), User)).Value!.Summary.Id;
        var onlyRev = (await s.GetDraftAsync(only)).Value!.Info.Revision;
        Assert.Equal("only-version", (await s.DiscardDraftAsync(only, onlyRev, User)).Code);

        var id = await CreatePublishedFormAsync(w, "a.two");
        var d = await s.CreateDraftAsync(id, null, (await s.GetAsync(id)).Value!.Summary.Revision, User);
        var r = await s.DiscardDraftAsync(id, d.Value!.Info.Revision, User);
        Assert.True(r.Success);
        Assert.Single(r.Value!.Versions);
        Assert.False(r.Value.Summary.HasDraft);
    }

    [Fact]
    public async Task Publishing_a_page_checks_components_and_options_and_records_references()
    {
        var w = new DefinitionTestWorld();
        var (s, _, db) = w.NewService();
        var good = DefinitionTestWorld.Page(("save", "button", Button));
        var id = (await s.CreateAsync(new CreateDefinition("page", "orders.list", "core", "\"Orders\"", null, good, null), User)).Value!.Summary.Id;
        var rev = (await s.GetDraftAsync(id)).Value!.Info.Revision;
        var published = await s.PublishAsync(id, rev, User);
        Assert.True(published.Success, string.Join(";", published.Errors?.Select(e => e.Pointer + " " + e.Code) ?? []));
        var reference = await db.DefinitionReferences.SingleAsync(r => r.ToKind == "component");   // the layout of the page is a reference too (SCRUM-451)
        Assert.Equal(("component", "button", "renders"), (reference.ToKind, reference.ToKey, reference.Role));
    }

    [Theory]
    [InlineData("""{"layout":"l","regions":{"main":[{"id":"a","component":"nope","options":{}}]}}""", "/regions/main/0/component", "unknown-component")]
    [InlineData("""{"layout":"l","regions":{"main":[{"id":"a","component":"button","options":{"label":{"en":"x"},"bogus":1}}]}}""", "/regions/main/0/options/bogus", "unknown-property")]
    [InlineData("""{"layout":"l","regions":{"main":[{"id":"a","component":"button","options":{"label":{"en":"x"}}},{"id":"a","component":"button","options":{"label":{"en":"x"}}}]}}""", "/regions/main/1/id", "duplicate")]
    [InlineData("""{"regions":{"main":[]}}""", "/layout", "required")]
    [InlineData("""{"layout":"l"}""", "/regions", "required")]
    [InlineData("""{"layout":"l","regions":{"main":[]},"script":"x"}""", "/script", "unknown-property")]
    [InlineData("""{"layout":"l","regions":{"main":[{"component":"button"}]}}""", "/regions/main/0/id", "required")]
    public async Task Page_validation_points_at_the_broken_part(string payload, string pointer, string code)
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var id = (await s.CreateAsync(new CreateDefinition("page", "p.one", "core", "\"P\"", null, payload, null), User)).Value!.Summary.Id;
        var r = await s.ValidateAsync(id, (await s.GetDraftAsync(id)).Value!.Info.Revision, User);
        Assert.Contains(r.Errors!, e => e.Pointer == pointer && e.Code == code);
    }

    [Fact]
    public async Task A_page_may_not_exceed_the_instance_limit()
    {
        var w = new DefinitionTestWorld { Limits = new DefinitionLimits { MaxInstancesPerPage = 2 } };
        var (s, _, _) = w.NewService();
        var page = DefinitionTestWorld.Page(("a", "button", Button), ("b", "button", Button), ("c", "button", Button));
        var id = (await s.CreateAsync(new CreateDefinition("page", "p.big", "core", "\"P\"", null, page, null), User)).Value!.Summary.Id;
        var r = await s.ValidateAsync(id, (await s.GetDraftAsync(id)).Value!.Info.Revision, User);
        Assert.Contains(r.Errors!, e => e.Code == "too-many-instances");
    }

    [Fact]
    public async Task The_runtime_store_serves_only_the_published_version_and_follows_a_new_publish()
    {
        var w = new DefinitionTestWorld();
        var (s, store, _) = w.NewService();
        var created = (await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!;
        Assert.Null(await store.GetPublishedAsync("form", "items.new"));          // a draft is invisible to consumers

        var rev = (await s.GetDraftAsync(created.Summary.Id)).Value!.Info.Revision;
        await s.PublishAsync(created.Summary.Id, rev, User);
        var v1 = await store.GetPublishedAsync("form", "items.new");
        Assert.Equal(1, v1!.VersionNumber);

        var d = await s.CreateDraftAsync(created.Summary.Id, null, (await s.GetAsync(created.Summary.Id)).Value!.Summary.Revision, User);
        var saved = await s.SaveDraftAsync(created.Summary.Id, DefinitionTestWorld.Form.Replace("Item", "Thing"), null, d.Value!.Info.Revision, User);
        await s.PublishAsync(created.Summary.Id, saved.Value!.Info.Revision, User);
        var v2 = await store.GetPublishedAsync("form", "items.new");                 // same instance: the publish evicted the stamp at once
        Assert.Equal((2, true), (v2!.VersionNumber, v2.PayloadJson.Contains("Thing")));
    }

    [Fact]
    public async Task Export_then_import_creates_a_draft_and_never_publishes()
    {
        var source = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(source);
        var package = (await source.NewService().Service.ExportAsync(id, null)).Value!;
        Assert.Equal(DefinitionPackage.CurrentFormat, package.Format);

        var target = new DefinitionTestWorld();
        var (t, _, _) = target.NewService();
        var imported = await t.ImportAsync(package, User);
        Assert.Equal((1, 0), (imported.Value!.Created, imported.Value.Updated));
        var d = (await t.GetAsync(imported.Value.DefinitionIds[0])).Value!;
        Assert.Equal((null, true), (d.Summary.PublishedVersion, d.Summary.HasDraft));

        var again = await t.ImportAsync(package, User);                                // the open draft is not overwritten
        Assert.Contains(again.Errors!, e => e.Code == "draft-exists");
    }

    [Fact]
    public async Task Import_refuses_a_changed_payload_an_unknown_kind_and_an_invalid_definition_in_one_go()
    {
        var w = new DefinitionTestWorld();
        var id = await CreatePublishedFormAsync(w);
        var (s, _, db) = w.NewService();
        var item = (await s.ExportAsync(id, null)).Value!.Items[0];
        var package = new DefinitionPackage(DefinitionPackage.CurrentFormat, DateTime.UtcNow, new[]
        {
            item with { Key = "other.one", PayloadJson = item.PayloadJson.Replace("Item", "Changed") },    // hash no longer matches
            item with { Key = "other.two", Kind = "nope" },
        });
        var r = await new DefinitionTestWorld().NewService().Service.ImportAsync(package, User);
        Assert.Contains(r.Errors!, e => e.Pointer == "/items/0/contentHash" && e.Code == "hash-mismatch");
        Assert.Contains(r.Errors!, e => e.Pointer == "/items/1/kind");
        var empty = await s.ImportAsync(new DefinitionPackage("other/9", DateTime.UtcNow, Array.Empty<DefinitionPackageItem>()), User);
        Assert.Contains(empty.Errors!, e => e.Code == "unsupported-format");
        Assert.Contains(empty.Errors!, e => e.Code == "empty");
    }

    [Fact]
    public async Task Export_of_an_unpublished_definition_is_a_conflict_and_a_missing_version_is_not_found()
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var id = (await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!.Summary.Id;
        Assert.Equal("not-published", (await s.ExportAsync(id, null)).Code);
        Assert.Equal(DefinitionFailure.NotFound, (await s.ExportAsync(id, 7)).Failure);
        Assert.True((await s.ExportAsync(id, 1)).Success);
    }

    [Fact]
    public async Task List_filters_sorts_and_pages()
    {
        var w = new DefinitionTestWorld();
        var (s, _, _) = w.NewService();
        for (var i = 0; i < 5; i++) await s.CreateAsync(DefinitionTestWorld.CreateForm($"items.f{i}"), User);
        await s.CreateAsync(new CreateDefinition("page", "p.x", "core", "\"P\"", null, "{}", null), User);
        var all = await s.ListAsync(new DefinitionListQuery(null, null, null, null, Array.Empty<(string, bool)>(), 1, 25), default);
        Assert.Equal(6, all.Total);
        var forms = await s.ListAsync(new DefinitionListQuery("form", null, null, null, new[] { ("key", true) }, 1, 2), default);
        Assert.Equal((5, 2, "items.f4"), (forms.Total, forms.Items.Count, forms.Items[0].Key));
        Assert.Equal(0, (await s.ListAsync(new DefinitionListQuery(null, null, null, true, Array.Empty<(string, bool)>(), 1, 25), default)).Total);
    }

    [Fact]
    public async Task Update_envelope_changes_the_title_with_the_revision()
    {
        var (s, _, _) = new DefinitionTestWorld().NewService();
        var d = (await s.CreateAsync(DefinitionTestWorld.CreateForm(), User)).Value!;
        var r = await s.UpdateEnvelopeAsync(d.Summary.Id, """{"en":"New"}""", null, d.Summary.Revision, User);
        Assert.Equal("""{"en":"New"}""", r.Value!.Summary.TitleJson);
        Assert.Equal(DefinitionFailure.Concurrency, (await s.UpdateEnvelopeAsync(d.Summary.Id, "\"x\"", null, d.Summary.Revision, User)).Failure);
        Assert.Equal(DefinitionFailure.Invalid, (await s.UpdateEnvelopeAsync(d.Summary.Id, "{bad", null, r.Value.Summary.Revision, User)).Failure);
    }
}
