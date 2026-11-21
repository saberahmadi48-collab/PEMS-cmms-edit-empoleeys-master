using PEMS.UI.Components;
using PEMS.Web.Models;
using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Tests.Workflow;

/// <summary>SCRUM-58: the engine's stages and history map to the registered components without a status string of the module.</summary>
public sealed class WorkflowComponentsTests
{
    private static WorkflowRuntimeViewModel View() => new()
    {
        Stages =
        [
            new() { Order = 3, Name = "Approval", NameFa = "تأیید", StageType = "Approval", Status = "Upcoming", ResponsibleRole = "Manager" },
            new() { Order = 1, Name = "Draft", NameFa = "پیش‌نویس", StageType = "Review", Status = "Completed" },
            new() { Order = 2, Name = "Review", StageType = "Review", Status = "Current", ResponsibleRole = "Lead" },
            new() { Order = 4, Name = "Closed", StageType = "Approval", Status = "Rejected" },
            new() { Order = 5, Name = "Opt", StageType = "Approval", Status = "Skipped" }
        ]
    };

    [Fact]
    public void Steps_follow_the_stage_order_and_map_each_status_to_a_state()
    {
        var steps = WorkflowComponents.Steps(View(), fa: false);
        Assert.Equal(["Draft", "Review", "Approval", "Closed", "Opt"], steps.Select(s => s.Label));
        Assert.Equal([StageState.Done, StageState.Current, StageState.Pending, StageState.Rejected, StageState.Done], steps.Select(s => s.State));
        Assert.Equal("Lead", steps[1].Role);
    }

    [Fact]
    public void Steps_use_the_persian_name_when_there_is_one_and_the_request_is_persian()
    {
        var steps = WorkflowComponents.Steps(View(), fa: true);
        Assert.Equal("پیش‌نویس", steps[0].Label);
        Assert.Equal("Review", steps[1].Label);   // no Persian name: falls back
    }

    [Fact]
    public void Actions_map_label_tone_denial_and_confirmation()
    {
        var items = WorkflowComponents.Actions(
        [
            new WorkflowAvailableAction { ActionType = "Approve", Label = "Approve", LabelFa = "تأیید", IsAllowed = true },
            new WorkflowAvailableAction { ActionType = "Reject", Label = "Reject", IsAllowed = false, DeniedReason = "Not your stage" },
            new WorkflowAvailableAction { ActionType = "Custom" }
        ], fa: true, "Sure?");
        Assert.Equal(("تأیید", "success", true, null), (items[0].Label, items[0].Tone, items[0].Allowed, items[0].Confirm));
        Assert.Equal(("Reject", "danger", false, "Not your stage", "Sure?"), (items[1].Label, items[1].Tone, items[1].Allowed, items[1].DeniedReason, items[1].Confirm));
        Assert.Equal(("Custom", "primary"), (items[2].Label, items[2].Tone));
    }

    [Fact]
    public void History_maps_actions_to_localized_titles_tones_and_routes()
    {
        var h = new[]
        {
            new WorkflowHistoryDto { ActionType = "Approve", FromStageName = "Review", ToStageName = "Approval", UserId = 7, Role = "Lead", Comment = "ok", ActionDate = new DateTime(2026, 11, 1) },
            new WorkflowHistoryDto { ActionType = "Escalate", Role = "Lead", ActionDate = new DateTime(2026, 11, 2) }
        };
        var en = WorkflowComponents.History(h, fa: false, id => id == 7 ? "A. Rezaei" : null);
        Assert.Equal(("Approved", "success", "Review → Approval", "A. Rezaei", "ok"), (en[0].Title, en[0].Tone, en[0].Detail, en[0].Actor, en[0].Comment));
        Assert.Equal(("Escalate", "neutral", null, "Lead"), (en[1].Title, en[1].Tone, en[1].Detail, en[1].Actor));
        Assert.Equal("تأیید شد", WorkflowComponents.History(h, fa: true)[0].Title);
    }
}
