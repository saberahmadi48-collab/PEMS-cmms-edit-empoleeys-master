using System.Security.Claims;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Security;

/// <summary>
/// فهرست واحد (Registry) همه‌ی قابلیت‌های Import در PEMS - مرجع یگانه؛ مرکز Import تنها نقطه‌ی شروع هر Import است
/// و هیچ دکمه/لینک/Modal شروع Import در صفحات ماژول‌ها وجود ندارد (تست منبع این را تضمین می‌کند).
/// این‌جا موتور Import ساخته نمی‌شود: هر ردیف به Controller/Action موجود اشاره می‌کند و موتور، فرمت فایل،
/// اعتبارسنجی و گردش‌کار (Staging → Review → Commit) دست‌نخورده در همان ماژول می‌ماند.
/// شرط دیدن هر ردیف دقیقاً همان شرطی است که قبلاً لینک/صفحه‌ی همان Import داشت (مجموعه‌ی نقش‌های بخش ∩
/// MenuAccess.ImportCenter، یا نقش‌های خودِ صفحه)؛ برای Importهای مرحله‌ای علاوه بر آن مجوز View ماژول از
/// IPermissionService پرسیده می‌شود. Backend همچنان مرجع نهایی است - هر Controller مستقل بررسی می‌کند.
/// </summary>
public static class ImportCatalog
{
    public static class Category
    {
        public const string Engineering = "Engineering";
        public const string Cmms = "Cmms";
        public const string Inventory = "Inventory";
        public const string Organization = "Organization";
        public const string Ai = "Ai";
    }

    /// <summary>Page = لینک به صفحه‌ی Import موجود؛ Dialog = فرم آپلودِ همان Action موجود، روی خودِ مرکز Import.</summary>
    public enum LaunchMode { Page, Dialog }

    public static class Workflow
    {
        public const string StagedReview = "Upload → Review → Commit";
        public const string Direct = "Direct (no staging)";
        public const string Intake = "Multi-file intake";
    }

    /// <param name="Key">شناسه‌ی پایدار (Localization: IC_{Key}_Title / _Desc / _Flow / _Hint).</param>
    /// <param name="Controller">Controller موجود؛ مسیر و Action تغییر نکرده‌اند.</param>
    /// <param name="Area">Area صاحب Controller ("" = بدون Area؛ مثلاً MRS Import در Warehouse است).</param>
    /// <param name="Action">Page: Action صفحه‌ی Import (Index). Dialog: Action POST موجود (Import) که فرم مرکز Import به آن Post می‌کند.</param>
    /// <param name="FileTypes">فرمت‌های واقعیِ پذیرفته‌شده (نمایشی).</param>
    /// <param name="Accept">attribute accept فرم Dialog (فقط Mode=Dialog).</param>
    /// <param name="PermissionModule">ماژول مجوز Backend برای View؛ null یعنی Controller مجوز ماژولی ندارد.</param>
    /// <param name="RequiredRoleSets">کاربر باید در حداقل یک نقش از هر مجموعه باشد (AND میان مجموعه‌ها).</param>
    public sealed record ImportEntry(
        string Key, string Category, string Controller, string Action, LaunchMode Mode,
        string FileTypes, string? Accept, bool Staged, string WorkflowType, string? PermissionModule,
        IReadOnlyList<string[]> RequiredRoleSets, string Icon, string Area = "");

    private static string[][] Sets(params string[][] s) => s;

    // ترتیب = ترتیب نمایش
    public static readonly IReadOnlyList<ImportEntry> All = new List<ImportEntry>
    {
        // ---- Engineering (Views/MDRImport, Views/VDRImport)
        new("Mdr", Category.Engineering, "MDRImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "MDRImport",
            Sets(MenuAccess.EngineeringManagement, MenuAccess.ImportCenter), "bi-file-earmark-arrow-up"),
        new("Vdr", Category.Engineering, "VDRImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "VDR",
            Sets(MenuAccess.EngineeringManagement, MenuAccess.ImportCenter), "bi-file-earmark-arrow-up"),

        // ---- CMMS
        new("Equipment", Category.Cmms, "EquipmentImport", "Index", LaunchMode.Page, ".xlsx, .pdf (tag scan)", null, true, Workflow.StagedReview, "EquipmentImport",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-file-earmark-arrow-up"),
        new("FunctionalLocation", Category.Cmms, "FunctionalLocationImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "FunctionalLocationImport",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-file-earmark-arrow-up"),
        new("EquipmentFlAssignment", Category.Cmms, "EquipmentFunctionalLocationAssignment", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "EquipmentFunctionalLocationAssignment",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-signpost-split"),
        new("MaintenanceHistory", Category.Cmms, "MaintenanceHistoryImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "MaintenanceHistoryImport",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-clock-history"),
        // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۹
        new("ConditionReading", Category.Cmms, "ConditionReadingImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "ConditionReadingImport",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-graph-up-arrow"),
        // RCM کلاس‌محور: الگوهای استراتژی و تخصیص تگ‌ها (Preview → Commit صریح؛ استراتژی‌ها Draft ساخته می‌شوند)
        new("RcmStrategy", Category.Cmms, "StrategyImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, "MaintenanceStrategy",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-diagram-3"),
        // Direct import (no staging): the existing POST Instrument/Import, launched from the Import Center dialog.
        // The controller has no module permission - it enforces the same role gate as this card (ImportCatalog.CanUse).
        new("Instrument", Category.Cmms, "Instrument", "Import", LaunchMode.Dialog, ".xlsx", ".xlsx", false, Workflow.Direct, null,
            Sets(MenuAccess.CmmsIntegration), "bi-cpu"),

        // ---- Inventory
        new("Mrs", Category.Inventory, "MrsImport", "Index", LaunchMode.Page, ".xlsx, .xlsm", null, true, Workflow.StagedReview, "MrsImport",
            Sets(MenuAccess.CmmsIntegration, MenuAccess.ImportCenter), "bi-file-earmark-excel", "Warehouse"),

        // ---- Organization: direct imports (existing POST Department/Import, Employee/Import); controllers carry
        // [Authorize(Roles=SystemAdmin,EngineeringManager)], the card keeps the narrower legacy sidebar exposure (SystemAdmin).
        new("Department", Category.Organization, "Department", "Import", LaunchMode.Dialog, ".xlsx", ".xlsx", false, Workflow.Direct, null,
            Sets(MenuAccess.Administration), "bi-building"),
        new("Employee", Category.Organization, "Employee", "Import", LaunchMode.Dialog, ".xlsx", ".xlsx", false, Workflow.Direct, null,
            Sets(MenuAccess.Administration), "bi-people"),
        // Organization hierarchy (Positions / ParentPosition / holders / effective dates): validate-then-commit, all-or-nothing.
        // The controller is [Authorize(SystemAdmin)] and also enforces this card's role gate.
        new("OrganizationStructure", Category.Organization, "OrganizationImport", "Index", LaunchMode.Page, ".xlsx", null, true, Workflow.StagedReview, null,
            Sets(MenuAccess.Administration), "bi-diagram-3", "Admin"),

        // ---- AI intake: multi-file upload into the AI pipeline (page Index + POST Upload); same role gate enforced in the controller.
        new("DocumentUpload", Category.Ai, "DocumentUpload", "Index", LaunchMode.Page, ".pdf, .docx, .xlsx, images", null, false, Workflow.Intake, null,
            Sets(MenuAccess.AIIntelligence), "bi-cloud-upload"),
    };

    /// <summary>ترتیب نمایش دسته‌ها.</summary>
    public static readonly string[] CategoryOrder =
        { Category.Engineering, Category.Cmms, Category.Inventory, Category.Organization, Category.Ai };

    /// <summary>Controllerهای «صفحه‌ی Import مستقل» (Import Center در آن‌ها Active است).</summary>
    public static readonly string[] StandaloneImportControllers = All
        .Where(e => e.Mode == LaunchMode.Page).Select(e => e.Controller).Append("ImportCenter").ToArray();

    public static ImportEntry Get(string key) => All.Single(e => e.Key == key);

    public static bool IsVisibleByRole(ImportEntry entry, ClaimsPrincipal user) =>
        entry.RequiredRoleSets.All(set => set.Any(user.IsInRole));

    /// <summary>برای Controllerهایی که مجوز ماژولی ندارند: همان گیت نقشیِ کارتِ مرکز Import را در Backend اعمال می‌کند.</summary>
    public static bool CanUse(string key, ClaimsPrincipal user) => IsVisibleByRole(Get(key), user);

    public static IReadOnlyList<ImportEntry> VisibleByRole(ClaimsPrincipal user) =>
        All.Where(e => IsVisibleByRole(e, user)).ToList();

    /// <summary>نقش + (برای Importهای دارای ماژول مجوز) مجوز View از Backend.</summary>
    public static async Task<IReadOnlyList<ImportEntry>> VisibleAsync(
        ClaimsPrincipal user, IPermissionService permissions, IEnumerable<string> roles)
    {
        var roleList = roles.ToList();
        var result = new List<ImportEntry>();
        foreach (var e in VisibleByRole(user))
        {
            if (e.PermissionModule != null && !await permissions.HasPermissionAsync(roleList, e.PermissionModule, "View"))
                continue;
            result.Add(e);
        }
        return result;
    }
}
