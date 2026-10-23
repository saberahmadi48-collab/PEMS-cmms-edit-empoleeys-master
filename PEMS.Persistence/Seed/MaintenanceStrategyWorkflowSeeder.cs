using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// گردش‌کار تایید استراتژی نگهداری (RCM): Draft → InReview → Approved. عیناً هم‌الگو با
/// IrnDispositionWorkflowSeeder روی همان موتور IWorkflowRuntime (بدون تغییر در PEMS.WorkflowPlatform).
/// تفکیک وظایف (نویسنده ≠ تاییدکننده) در MaintenanceStrategyCommandService اعمال می‌شود؛ این‌جا فقط
/// نقش‌های مجاز مرحله‌ی بازبینی تعریف می‌شود. Idempotent: اگر تعریف وجود دارد کاری نمی‌کند.
/// </summary>
public static class MaintenanceStrategyWorkflowSeeder
{
    public const string Module = "CMMS";
    public const string Category = "StrategyApproval";
    public const string BusinessObjectType = "MaintenanceStrategy";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();

        var alreadyExists = await db.WorkflowDefinitions.AnyAsync(x => x.Module == Module && x.Category == Category);
        if (alreadyExists) return;

        var definition = new WorkflowDefinition
        {
            Name = "تایید استراتژی نگهداری (RCM)",
            Category = Category,
            Module = Module,
            Description = "هر نسخه‌ی استراتژی پیش از فعال‌شدن و تخصیص به تگ‌ها نیازمند بازبینی مستقل است",
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        var version = new WorkflowVersion
        {
            Definition = definition,
            VersionNumber = 1,
            Status = "Published",
            EffectiveDate = DateTime.Now,
            PublishedDate = DateTime.Now
        };

        var reviewStage = new WorkflowStage
        {
            Version = version,
            Name = "StrategyReview",
            StageOrder = 1,
            StageType = "Sequential",
            ResponsibleRole = string.Join(",", SystemRoles.SystemAdmin, SystemRoles.EngineeringSupervisor, SystemRoles.EngineeringManager),
            ApprovalStrategy = "AllApprovalsRequired",
            IsMandatory = true
        };

        version.Stages.Add(reviewStage);

        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = null, ToStage = reviewStage, ActionType = "Start", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = reviewStage, ToStage = null, ActionType = "Approve", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = reviewStage, ToStage = null, ActionType = "Reject", Priority = 0 });

        definition.Versions.Add(version);

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }
}
