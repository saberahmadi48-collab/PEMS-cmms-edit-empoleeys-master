namespace PEMS.Persistence.Identity;

/// <summary>
/// کاتالوگ نقش‌های پایه‌ی PEMS (منبع تعریف Domain/Category/توضیح). فقط روی شواهد مخزن بنا شده؛
/// ممیزی و دلیل هر نقش جدید: docs/audits/role-catalog-audit.md. نقش = مرز مسئولیت/اختیار،
/// Scope (دپارتمان/دیسیپلین/دامنه‌ی سازمانی) جدا از نقش مدیریت می‌شود.
/// </summary>
public static class RoleCatalog
{
    public const string WarehouseManager = "WH.Manager";
    public const string WarehouseStorekeeper = "WH.Storekeeper";
    // SCRUM-471 (owner decision 2026-10-29): the procurement roles behind the pre-procurement queue
    public const string ProcurementBuyer = "PRC.Buyer";
    public const string ProcurementManager = "PRC.Manager";

    public static class Domains
    {
        public const string Platform = "Platform";
        public const string Engineering = "Engineering";
        public const string DocumentControl = "DocumentControl";
        public const string Maintenance = "Maintenance";
        public const string Operations = "Operations";
        public const string Warehouse = "Warehouse";
        public const string Contracts = "Contracts";
        public const string Quality = "Quality";
        public const string Hse = "HSE";
        public const string Finance = "Finance";
        public const string Legal = "Legal";
        public const string General = "General";

        public static readonly string[] All =
            { Platform, Engineering, DocumentControl, Maintenance, Operations, Warehouse, Contracts, Quality, Hse, Finance, Legal, General };
    }

    public static class Categories
    {
        public const string Platform = "Platform";
        public const string Domain = "Domain";
        public const string Functional = "Functional";
        public const string ReadOnly = "ReadOnly";

        public static readonly string[] All = { Platform, Domain, Functional, ReadOnly };
    }

    /// <summary>ردیف ماتریس مجوز قدیمی (ModuleName + پرچم‌ها) - منبع واقعیِ ویرایش‌پذیر توسط مدیر.</summary>
    public sealed record ModuleGrant(string Module, bool View = true, bool Create = false, bool Edit = false, bool Approve = false);

    public sealed record RoleDefinition(
        string Name,
        string DisplayName,
        string Description,
        string Domain,
        string Category,
        int SortOrder,
        ModuleGrant[]? DefaultPermissions = null,
        string[]? VisibleModules = null)
    {
        /// <summary>کلید منبع بومی‌سازی: Role_MNT_Manager_Name / Role_MNT_Manager_Desc.</summary>
        public string ResourceKey => Name.Replace('.', '_');
    }

    public static readonly RoleDefinition[] Definitions =
    {
        new(SystemRoles.SystemAdmin, "System Admin", "Platform administration: users, roles, permissions and configuration. Bypasses the permission matrix by design.", Domains.Platform, Categories.Platform, 10),
        new(SystemRoles.EngineeringManager, "Engineering Manager", "Leads Engineering: document approval, cross-discipline review and final approval of Work Orders originating in Engineering.", Domains.Engineering, Categories.Domain, 20),
        new(SystemRoles.EngineeringSupervisor, "Engineering Supervisor", "Supervises Engineering work: document checks, work request approval and Work Order final approval.", Domains.Engineering, Categories.Domain, 30),
        new(SystemRoles.DisciplineEngineer, "Discipline Engineer", "Engineering discipline specialist: prepares documents and verifies completed work for the originating discipline.", Domains.Engineering, Categories.Functional, 40),
        new(SystemRoles.DocumentController, "Document Controller", "Controls engineering documents and correspondence: issue, transmittals and client responses.", Domains.DocumentControl, Categories.Functional, 50),
        new(SystemRoles.MaintenanceEngineer, "Maintenance Engineer", "Maintenance engineering: equipment, plans, strategies, inspections and condition monitoring.", Domains.Maintenance, Categories.Functional, 60),
        new(SystemRoles.ContractManager, "Contract Manager", "Manages contracts, procurement packages and tenders, including their approval.", Domains.Contracts, Categories.Domain, 70),
        new(SystemRoles.QAManager, "QA Manager", "Quality assurance: IRN disposition, NCR, MOC closure and permit health/safety review.", Domains.Quality, Categories.Domain, 80),
        new(SystemRoles.Viewer, "Viewer", "Read-only access to Engineering, CMMS and import status information. Cannot change data.", Domains.General, Categories.ReadOnly, 90),
        new(SystemRoles.Legal, "Legal", "Legal review participant in multi-discipline contract review.", Domains.Legal, Categories.Functional, 100),
        new(SystemRoles.Finance, "Finance", "Finance review participant in multi-discipline contract review.", Domains.Finance, Categories.Functional, 110),
        new(SystemRoles.HSE, "HSE", "Health, safety and environment review participant in multi-discipline contract review.", Domains.Hse, Categories.Functional, 120),

        new("MNT.Manager", "Maintenance Manager", "Heads Maintenance: rejects or cancels Work Orders and gives final approval for Work Orders originating in Maintenance.", Domains.Maintenance, Categories.Domain, 200, VisibleModules: new[] { "CmmsIntegration" }),
        new("MNT.PlanningHead", "Maintenance Planning Head", "Leads maintenance planning: creates, plans and prepares Work Orders and approves work requests.", Domains.Maintenance, Categories.Domain, 210, VisibleModules: new[] { "CmmsIntegration" }),
        new("MNT.PlanningExpert", "Maintenance Planning Expert", "Maintenance planner: creates, plans and prepares Work Orders.", Domains.Maintenance, Categories.Functional, 220, VisibleModules: new[] { "CmmsIntegration" }),
        new("MNT.DisciplineHead", "Maintenance Discipline Head", "Leads a maintenance discipline: assigns executors, starts and executes work, verifies and gives final approval.", Domains.Maintenance, Categories.Domain, 230, VisibleModules: new[] { "CmmsIntegration" }),
        new("MNT.SeniorExpert", "Maintenance Senior Expert", "Senior maintenance technician: executes and verifies work within the responsible discipline.", Domains.Maintenance, Categories.Functional, 240, VisibleModules: new[] { "CmmsIntegration" }),
        new("MNT.Expert", "Maintenance Expert", "Maintenance technician: executes assigned work within the responsible discipline.", Domains.Maintenance, Categories.Functional, 250, VisibleModules: new[] { "CmmsIntegration" }),
        new("OPS.IssueAuthority", "Operations Issue Authority", "Operations authority that issues (releases) Work Orders after permit/isolation/gas-test gates and verifies completed work.", Domains.Operations, Categories.Functional, 300, VisibleModules: new[] { "CmmsIntegration" }),

        new(WarehouseManager, "Warehouse Manager", "Heads the warehouse: approves store requisitions. Separate from the storekeeper who creates them; cannot approve IRN quality dispositions.", Domains.Warehouse, Categories.Domain, 400,
            DefaultPermissions: new[]
            {
                new ModuleGrant("StoreRequisition", Approve: true),
                new ModuleGrant("MaterialItem", Edit: true),
                new ModuleGrant("InventoryMasterData", Edit: true),
                new ModuleGrant("IRN", Edit: true),   // SCRUM-547 owner decision 2026-11-09: resolves the Hold routed to this role (IRN QA approval stays with QAManager)
                new ModuleGrant("Barcode"),
            },
            VisibleModules: new[] { "Inventory" }),
        new(WarehouseStorekeeper, "Storekeeper", "Warehouse operator: creates store requisitions, records receipts and maintains stock master data. Cannot approve requisitions or IRN quality dispositions.", Domains.Warehouse, Categories.Functional, 410,
            DefaultPermissions: new[]
            {
                new ModuleGrant("StoreRequisition", Create: true, Edit: true),
                new ModuleGrant("MaterialItem", Create: true, Edit: true),
                new ModuleGrant("InventoryMasterData", Create: true, Edit: true),
                new ModuleGrant("IRN", Create: true, Edit: true),
                new ModuleGrant("Barcode", Create: true),
            },
            VisibleModules: new[] { "Inventory" }),
        new(ProcurementBuyer, "Buyer", "Procurement expert: works the pre-procurement queue and turns each demand into a material requisition line, accepts engineering MRs or dismisses / returns them with a reason. Cannot approve purchase orders.", Domains.Contracts, Categories.Functional, 500,
            DefaultPermissions: new[]
            {
                new ModuleGrant("PreProcurement", Edit: true),
                new ModuleGrant("MaterialRequisition", Create: true, Edit: true),
                new ModuleGrant("ProcurementPackage"),
                new ModuleGrant("PurchaseOrder", Create: true, Edit: true),   // SCRUM-470: builds and submits POs (approval comes from the PO authority table)
                new ModuleGrant("IRN"),   // SCRUM-547 owner decision 2026-11-09: opens the IRN to decide close short / replacement of a rejected PO line
            }),
        new(ProcurementManager, "Procurement Manager", "Heads procurement: oversees the pre-procurement queue and can decide its items. Approval of purchase orders by amount comes from the purchase-order authority table; this role issues approved purchase orders (SCRUM-470).", Domains.Contracts, Categories.Domain, 510,
            DefaultPermissions: new[]
            {
                new ModuleGrant("PreProcurement", Edit: true, Approve: true),
                new ModuleGrant("MaterialRequisition", Edit: true),
                new ModuleGrant("ProcurementPackage"),
                new ModuleGrant("PurchaseOrder", Approve: true),   // SCRUM-470 owner decision 2026-10-30: issues approved POs (the approval itself comes from the PO authority table)
            }),
    };

    public static RoleDefinition? Find(string? name) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));

    private static string Normalize(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// نقش‌های سفارشی که هم‌معنا/هم‌نام یک نقش پایه به‌نظر می‌رسند (فقط گزارش؛ هرگز ادغام/بازنویسی نمی‌شود).
    /// مقایسه بر پایه‌ی Name/DisplayName نرمال‌شده (بدون فاصله/نقطه/حروف بزرگ-کوچک).
    /// </summary>
    public static List<(string Custom, string Baseline)> FindPossibleDuplicates(IEnumerable<(string Name, string? DisplayName)> existingRoles)
    {
        var result = new List<(string, string)>();
        foreach (var (name, display) in existingRoles)
        {
            if (Find(name) != null) continue; // خودِ نقش پایه

            var keys = new[] { Normalize(name), Normalize(display) }.Where(k => k.Length > 0).ToArray();
            foreach (var d in Definitions)
            {
                var baseKeys = new[] { Normalize(d.Name), Normalize(d.DisplayName) };
                if (keys.Any(k => baseKeys.Contains(k)))
                {
                    result.Add((name, d.Name));
                    break;
                }
            }
        }
        return result;
    }
}
