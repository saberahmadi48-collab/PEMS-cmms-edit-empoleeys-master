namespace PEMS.Web.Models;

// طبق «PEMS Phase 2C-3 — Safe Extraction of Shared Stage Diagram»: سه پیاده‌سازی مستقل
// (_WorkflowStagesDiagram برای EDMS، _VdrWorkflowStagesDiagram برای VDR) عملاً یک نمودار
// گرافیکی یکسان (دایره‌ها + رابط‌های بین‌شان، با وضعیت Done/Current/Pending/Rejected) را با
// CSS تقریباً بایت‌به‌بایت یکسان رندر می‌کردند. تصمیم این‌که کدام مرحله Done/Current/
// Pending/Rejected است، همیشه در همان View مصرف‌کننده (بر اساس Status/CurrentStageName
// خودش) محاسبه می‌شد - این مدل عمداً همان تقسیم مسئولیت را حفظ می‌کند: Partial فقط رندر
// می‌کند، هرگز State را خودش محاسبه نمی‌کند.
public enum StageItemState
{
    Pending,
    Current,
    Done,
    Rejected
}

public class StageDiagramItem
{
    public string Label { get; set; } = "";
    public string IconCssClass { get; set; } = "";

    // طبق ممیزی فاز ۲C-۲: _WorkOrderStagesDiagram اصلاً فیلد نقش ندارد - این‌جا Nullable
    // است تا آن مصرف‌کننده (در فاز‌های بعدی، در صورت تأیید) بدون رندر یک خط خالی از همین
    // مدل استفاده کند.
    public string? RoleLabel { get; set; }

    public StageItemState State { get; set; }
}

public class StageDiagramViewModel
{
    public List<StageDiagramItem> Stages { get; set; } = new();

    /// <summary>The stages as the <c>pems-stage-diagram</c> component takes them (SCRUM-486): the page's states, unchanged.</summary>
    public IReadOnlyList<PEMS.UI.Components.StageStep> Steps => Stages.Select(s => new PEMS.UI.Components.StageStep(s.Label, s.IconCssClass, s.State switch
    {
        StageItemState.Done => PEMS.UI.Components.StageState.Done,
        StageItemState.Current => PEMS.UI.Components.StageState.Current,
        StageItemState.Rejected => PEMS.UI.Components.StageState.Rejected,
        _ => PEMS.UI.Components.StageState.Pending
    }, s.RoleLabel)).ToList();
}

/// <summary>The work-order cycle for the stage diagram (variant wo): the stages and their texts of the former _WorkOrderStagesDiagram partial; a pending multi-discipline review adds its own stage.</summary>
public static class WorkOrderStages
{
    public static IReadOnlyList<PEMS.UI.Components.StageStep> Build(string? status, bool hasPendingReview)
    {
        status ??= "Open";
        var order = hasPendingReview
            ? new[] { "Open", "Assigned", "InProgress", "DisciplineReview", "Completed", "Closed" }
            : new[] { "Open", "Assigned", "InProgress", "Completed", "Closed" };
        var labels = new Dictionary<string, string>
        {
            ["Open"] = "ثبت شده", ["Assigned"] = "تخصیص‌یافته", ["InProgress"] = "در حال انجام",
            ["DisciplineReview"] = "بازبینی چند-دیسیپلینه", ["Completed"] = "تکمیل‌شده", ["Closed"] = "بسته‌شده"
        };
        var icons = new Dictionary<string, string>
        {
            ["Open"] = "bi-plus-circle", ["Assigned"] = "bi-person-check", ["InProgress"] = "bi-tools",
            ["DisciplineReview"] = "bi-people", ["Completed"] = "bi-check2-circle", ["Closed"] = "bi-archive"
        };
        var current = Array.IndexOf(order, status);
        if (current < 0) current = 0;
        if (hasPendingReview && status == "InProgress") current = Array.IndexOf(order, "DisciplineReview");
        return order.Select((key, i) => new PEMS.UI.Components.StageStep(labels[key], icons[key],
            i < current ? PEMS.UI.Components.StageState.Done : i == current ? PEMS.UI.Components.StageState.Current : PEMS.UI.Components.StageState.Pending)).ToList();
    }
}
