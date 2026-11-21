using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

/// <summary>
/// The Work Order section guard as a setting of an operation action (SCRUM-437): <c>sectionGuard: { section, action, workOrderId, reason, recordRef, audit }</c>.
/// Before the service is called, <see cref="IWorkOrderSectionGuard"/> decides (section x phase x permission; a refusal is logged by the guard and shown with its own bilingual message);
/// after a successful call the change is recorded (who, when, status, before/after) best-effort, exactly as the controller did. <c>audit</c> is the "after" text with {argument} placeholders
/// filled from the service arguments (a format can follow a colon, e.g. {plannedStart:O}). workOrderId, reason and recordRef are sources (field:name, literal:text, context:name).
/// </summary>
public sealed record SectionGuardPlan(int WorkOrderId, WoSection Section, WoSectionAction Action, string? Reason, string? RecordRef, string? Audit, bool RecordRefFromResult = false, bool BeforeFromResult = false);

public static partial class OperationSectionGuard
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "section", "action", "workOrderId", "reason", "recordRef", "audit", "recordRefFromResult", "beforeFromResult" };

    [GeneratedRegex(@"\{(\w+)(?::(\w+))?\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<DefinitionFieldError> Validate(JsonObject? guard, Func<string, bool> isSource)
    {
        if (guard is null) yield break;
        foreach (var key in guard.Select(kv => kv.Key).Where(k => !Keys.Contains(k)))
            yield return new("/sectionGuard/" + key, "unknown-parameter", "Def_Err_UnknownField", $"'{key}' is not a section guard setting ({string.Join(", ", Keys)}).");
        if (!Enum.TryParse<WoSection>(EntityJson.Str(guard["section"]), out _))
            yield return new("/sectionGuard/section", "wrong-guard", "Def_Err_Type", $"section must be one of: {string.Join(", ", Enum.GetNames<WoSection>())}.");
        if (!Enum.TryParse<WoSectionAction>(EntityJson.Str(guard["action"]), out _))
            yield return new("/sectionGuard/action", "wrong-guard", "Def_Err_Type", $"action must be one of: {string.Join(", ", Enum.GetNames<WoSectionAction>())}.");
        foreach (var name in new[] { "workOrderId", "reason", "recordRef" })
            if ((name == "workOrderId" || guard[name] is not null) && !isSource(EntityJson.Str(guard[name]) ?? ""))
                yield return new("/sectionGuard/" + name, "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or context:employeeId|roles|userName|culture.");
    }

    /// <summary>Asks the section guard. Returns a refusal, or null with the plan to record after the call.</summary>
    public static async Task<(ActionOutcome? Refusal, SectionGuardPlan? Plan)> AuthorizeAsync(
        IServiceProvider services, JsonObject guard, Func<string?, string?> resolve, string culture, WoCaller caller)
    {
        var sectionGuard = services.GetService<IWorkOrderSectionGuard>();
        var no = TranslatableText.Of(("en", "The Work Order section guard is not configured; the change is not allowed."), ("fa", "دروازه‌ی بخش‌های Work Order پیکربندی نشده است؛ تغییر مجاز نیست."));
        if (sectionGuard is null) return (ActionOutcome.Refused(no), null);

        if (!int.TryParse(resolve(EntityJson.Str(guard["workOrderId"])), NumberStyles.Integer, CultureInfo.InvariantCulture, out var workOrderId))
            return (ActionOutcome.Refused(TranslatableText.Of(("en", "The work order is missing."), ("fa", "Work Order مشخص نیست."))), null);
        var plan = new SectionGuardPlan(workOrderId, Enum.Parse<WoSection>(EntityJson.Str(guard["section"])!), Enum.Parse<WoSectionAction>(EntityJson.Str(guard["action"])!),
            resolve(EntityJson.Str(guard["reason"])), resolve(EntityJson.Str(guard["recordRef"])), EntityJson.Str(guard["audit"]), guard["recordRefFromResult"]?.GetValue<bool>() == true, guard["beforeFromResult"]?.GetValue<bool>() == true);

        var result = await sectionGuard.AuthorizeAsync(plan.WorkOrderId, plan.Section, plan.Action, caller, plan.Reason, plan.RecordRef);
        if (result.Allowed) return (null, plan);
        return (ActionOutcome.Refused(TranslatableText.Of(("en", result.MessageEn), ("fa", result.MessageFa))), null);
    }

    /// <summary>Records the successful change; never throws (the change is already saved).</summary>
    public static async Task RecordAsync(IServiceProvider services, SectionGuardPlan plan, WoCaller caller, IReadOnlyDictionary<string, object?> arguments, object? answer = null)
    {
        var sectionGuard = services.GetService<IWorkOrderSectionGuard>();
        if (sectionGuard is null) return;
        var after = plan.Audit is null ? null : Placeholder().Replace(plan.Audit, m =>
        {
            arguments.TryGetValue(m.Groups[1].Value, out var value);
            return value is IFormattable f && m.Groups[2].Success ? f.ToString(m.Groups[2].Value, CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        });
        var recordRef = plan.RecordRef;
        if (plan.RecordRefFromResult)       // the id of the row the service created: the last number of a tuple answer, or an int answer
            recordRef = answer switch { System.Runtime.CompilerServices.ITuple t when t.Length > 2 => Convert.ToString(t[t.Length - 1], CultureInfo.InvariantCulture), int or long => Convert.ToString(answer, CultureInfo.InvariantCulture), _ => recordRef };
        // the text of the row as it was, for a removal: the third item of a tuple answer (Success, Error, Before)
        var before = plan.BeforeFromResult && answer is System.Runtime.CompilerServices.ITuple b && b.Length > 2 ? b[2] as string : null;
        try { await sectionGuard.RecordChangeAsync(plan.WorkOrderId, plan.Section, plan.Action, caller, recordRef, plan.Reason, before, after); }
        catch (Exception) { /* the audit is best-effort, as in the controller */ }
    }
}
