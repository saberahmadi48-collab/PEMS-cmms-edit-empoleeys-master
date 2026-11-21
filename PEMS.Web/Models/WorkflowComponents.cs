using PEMS.UI.Components;
using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Models;

// SCRUM-58 (#58): the workflow runtime (stages, instance, history) mapped to the registered components, so every module draws the same stage diagram and history from the engine's data
// and not from its own status strings. Pure functions: no service call, nothing to mock.

public static class WorkflowComponents
{
    private static readonly Dictionary<string, (string En, string Fa, string Tone)> Described = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Approve"] = ("Approved", "تأیید شد", "success"),
        ["Reject"] = ("Rejected", "رد شد", "danger"),
        ["Return"] = ("Returned", "برگشت داده شد", "warning"),
        ["Skip"] = ("Skipped", "رد شد (رفع نیاز)", "neutral"),
        ["Submit"] = ("Submitted", "ارسال شد", "info"),
        ["Cancel"] = ("Cancelled", "لغو شد", "danger"),
        ["Reopen"] = ("Reopened", "بازگشایی شد", "warning"),
        ["Start"] = ("Started", "شروع شد", "info"),
    };

    private static string StageIcon(string stageType) => stageType switch
    {
        "Parallel" => "bi-diagram-2", "Approval" => "bi-check2-square", "Review" => "bi-eye", "Notification" => "bi-bell", _ => "bi-circle"
    };

    /// <summary>The stages of a running instance for <c>pems-stage-diagram</c>: Completed and Skipped are done, Current is current, Rejected and Cancelled are rejected, the rest pending.</summary>
    public static IReadOnlyList<StageStep> Steps(WorkflowRuntimeViewModel view, bool fa)
        => view.Stages.OrderBy(s => s.Order).Select(s => new StageStep(
            fa && !string.IsNullOrWhiteSpace(s.NameFa) ? s.NameFa! : s.Name,
            StageIcon(s.StageType),
            s.Status switch
            {
                "Completed" or "Skipped" => StageState.Done,
                "Current" => StageState.Current,
                "Rejected" or "Cancelled" => StageState.Rejected,
                _ => StageState.Pending
            },
            s.ResponsibleRole)).ToList();

    /// <summary>The actions the engine offers (<see cref="WorkflowAvailableAction"/>) for <c>pems-workflow-action-bar</c>: the label in the language of the request, the tone by action, reject and cancel ask first.</summary>
    public static IReadOnlyList<WorkflowActionItem> Actions(IEnumerable<WorkflowAvailableAction> actions, bool fa, string confirmText, Func<string?, string?>? stage = null)
        => actions.Select(a => new WorkflowActionItem
        {
            ActionType = a.ActionType,
            Label = (fa && !string.IsNullOrWhiteSpace(a.LabelFa) ? a.LabelFa : a.Label) ?? a.ActionType,
            Tone = a.ActionType.ToLowerInvariant() switch { "approve" or "submit" or "start" => "success", "reject" or "cancel" => "danger", "return" or "reopen" => "warning", _ => "primary" },
            Allowed = a.IsAllowed,
            DeniedReason = a.DeniedReason,
            Confirm = a.ActionType.Equals("Reject", StringComparison.OrdinalIgnoreCase) || a.ActionType.Equals("Cancel", StringComparison.OrdinalIgnoreCase) ? confirmText : null,
            Target = string.IsNullOrWhiteSpace(a.ToStageName) ? null : (stage?.Invoke(a.ToStageName) ?? a.ToStageName),
            RequireComment = PEMS.Application.Common.WorkflowActionRules.ActionRequiresReason(a.ActionType)
        }).ToList();

    private static (string Title, string Tone) Describe(string actionType, bool fa)
        => Described.TryGetValue(actionType, out var a) ? (fa ? a.Fa : a.En, a.Tone) : (actionType, "neutral");

    private static string? Route(string? from, string? to)
        => from is null && to is null ? null : from is null ? to : to is null ? from : from + " → " + to;

    /// <summary>The history entries of the engine (<see cref="WorkflowHistoryDto"/>) for <c>pems-workflow-history</c>; <paramref name="actor"/> turns a user id into a name.</summary>
    public static IReadOnlyList<WorkflowHistoryEntry> History(IEnumerable<WorkflowHistoryDto> history, bool fa, Func<int?, string?>? actor = null, Func<string?, string?>? stage = null)
        => history.Select(h =>
        {
            var (title, tone) = Describe(h.ActionType, fa);
            return new WorkflowHistoryEntry { Title = string.IsNullOrWhiteSpace(h.Decision) || h.Decision.Equals(h.ActionType, StringComparison.OrdinalIgnoreCase) ? title : title + " (" + h.Decision + ")", Tone = tone, Detail = Route(stage is null ? h.FromStageName : stage(h.FromStageName), stage is null ? h.ToStageName : stage(h.ToStageName)), Actor = actor?.Invoke(h.UserId) ?? h.Role, Comment = h.Comment, When = h.ActionDate };
        }).ToList();

    /// <summary>The same for the compact summary of the runtime view.</summary>
    public static IReadOnlyList<WorkflowHistoryEntry> History(IEnumerable<WorkflowHistorySummaryItem> history, bool fa, Func<int?, string?>? actor = null)
        => history.Select(h =>
        {
            var (title, tone) = Describe(h.ActionType, fa);
            return new WorkflowHistoryEntry { Title = title, Tone = tone, Detail = Route(h.FromStageName, h.ToStageName), Actor = actor?.Invoke(h.UserId), Comment = h.Comment, When = h.ActionDate };
        }).ToList();
}
