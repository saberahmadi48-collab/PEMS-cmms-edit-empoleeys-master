using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-517: a shipped workflow document is created once, never overwrites what the customer runs, and a changed document is stored as a reviewable draft with a diff.</summary>
public sealed class ShippedWorkflowsTests
{
    private const string V1 = """
        { "module": "Maintenance", "context": "Request", "name": "Request Approval", "description": "Two steps.",
          "stages": [ { "key": "sub", "name": "Submitted", "order": 10, "responsibleRole": "Planner" }, { "key": "rev", "name": "Review", "order": 20, "responsibleRole": "Planner" } ],
          "transitions": [ { "from": null, "to": "sub", "action": "Start" }, { "from": "sub", "to": "rev", "action": "Approve" }, { "from": "rev", "to": null, "action": "Complete" } ] }
        """;

    private static string V2 => V1.Replace("\"order\": 20, \"responsibleRole\": \"Planner\" }", "\"order\": 20, \"responsibleRole\": \"Manager\" }, { \"key\": \"qa\", \"name\": \"QA\", \"order\": 30 }");

    private sealed class AlwaysValid : IWorkflowVersionValidator
    {
        public Task<WorkflowValidationResult> ValidateAsync(int workflowVersionId, CancellationToken ct = default) => Task.FromResult(new WorkflowValidationResult());
    }

    private static (WorkflowShipper Shipper, PEMSDbContext Db) Setup()
    {
        var db = PemsDbContextFactory.CreateInMemory("ship-" + Guid.NewGuid());
        return (new WorkflowShipper(db, new WorkflowDefinitionService(db, new AlwaysValid())), db);
    }

    private static ShippedWorkflow Doc(string json)
    {
        var (w, problems) = ShippedWorkflow.Parse(json);
        Assert.Empty(problems);
        return w!;
    }

    [Theory]
    [InlineData("{ \"module\": \"M\", \"context\": \"C\", \"name\": \"N\", \"stages\": [] }", "at least one stage")]
    [InlineData("{ \"module\": \"M\", \"context\": \"C\", \"name\": \"N\", \"stages\": [ { \"key\": \"a\", \"name\": \"A\" }, { \"key\": \"a\", \"name\": \"B\" } ] }", "used twice")]
    [InlineData("{ \"module\": \"M\", \"context\": \"C\", \"name\": \"N\", \"stages\": [ { \"key\": \"a\", \"name\": \"A\" } ], \"transitions\": [ { \"from\": \"x\", \"action\": \"Go\" } ] }", "unknown stage")]
    public void A_bad_document_is_refused_with_the_reason(string json, string expected)
        => Assert.Contains(ShippedWorkflow.Parse(json).Problems, p => p.Contains(expected));

    [Fact]
    public void The_hash_is_stable_for_the_same_content_and_changes_with_it()
    {
        Assert.Equal(Doc(V1).Hash(), Doc(V1).Hash());
        Assert.NotEqual(Doc(V1).Hash(), Doc(V2).Hash());
    }

    [Fact]
    public async Task A_new_workflow_is_created_published_and_shipping_it_again_changes_nothing()
    {
        var (shipper, db) = Setup();
        var first = await shipper.ShipAsync(Doc(V1), new WorkflowActor(null, "test"));
        var again = await shipper.ShipAsync(Doc(V1), new WorkflowActor(null, "test"));
        Assert.Equal((ShipOutcome.Created, ShipOutcome.Unchanged), (first.Outcome, again.Outcome));
        Assert.Equal(("Published", 2, 3), ((await db.WorkflowVersions.SingleAsync()).Status, await db.WorkflowStages.CountAsync(), await db.WorkflowTransitions.CountAsync()));
    }

    [Fact]
    public async Task A_changed_document_is_stored_as_a_draft_with_a_diff_and_the_active_version_stays()
    {
        var (shipper, db) = Setup();
        var actor = new WorkflowActor(null, "test");
        await shipper.ShipAsync(Doc(V1), actor);

        var update = await shipper.ShipAsync(Doc(V2), actor);
        Assert.Equal(ShipOutcome.UpstreamStored, update.Outcome);
        Assert.Contains("stage added: QA", update.Changes);
        Assert.Contains("stage Review: responsible Planner -> Manager", update.Changes);

        var versions = await db.WorkflowVersions.OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal(["Published", "Draft"], versions.Select(v => v.Status));
        Assert.EndsWith(WorkflowShipper.UpstreamNote, versions[1].ChangeSummary);

        Assert.Equal(ShipOutcome.Unchanged, (await shipper.ShipAsync(Doc(V2), actor)).Outcome);
        var renamed = Doc(V1);
        renamed.Stages[0].Name = "Renamed";
        Assert.Equal(ShipOutcome.DraftOpen, (await shipper.ShipAsync(renamed, actor)).Outcome);
    }
}
