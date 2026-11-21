using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Web.Security;

namespace PEMS.Web.Navigation;

/// <summary>
/// کاتالوگ پیش‌فرض سایدبار داده‌محور: عیناً همان ساختار، ترتیب، آیکون و کلید بومی‌سازی _SidebarMenu.cshtml (سایدبار قدیمی که پشت پرچم
/// Features:DynamicSidebar:Enabled باقی مانده). قاعده‌ی نمایش هر بخش همان دروازه‌ی قبلی است (NavigationVisibilityResolver / پرچم‌های
/// مجوزمحور)؛ قاعده‌ی هر پیوند علاوه بر آن همان مجوزی است که کنترلر مقصد در Action همین پیوند واقعاً اعمال می‌کند (از سورس خوانده
/// شده، نه اختراعی). پیوندی که کنترلرش هیچ دروازه‌ای ندارد Always می‌ماند (تصمیم مالک: مثل امروز نمایان).
/// </summary>
public static class NavigationMenuDefaults
{
    /// <summary>
    /// کلیدهای سیستمیِ حذف‌شده از منو. همگام‌سازی پیش‌فرض‌ها ردیف قدیمی آن‌ها را در دیتابیس غیرفعال می‌کند (حذف نمی‌شود).
    /// «برچسب‌ها» از منو رفت: درخت برچسب حالا داخل کارتابل نامه‌نگاری (پنل کناری و تنظیمات) مدیریت می‌شود.
    /// </summary>
    public static readonly IReadOnlyList<string> RetiredKeys = new[]
    {
        "proj.tags",
        // SCRUM-423: تدارکات، قرارداد و انبار زیر بخش واحد «تدارکات و زنجیره‌ی تأمین» (sec.procurement) رفتند. والدِ ردیف موجود را
        // همگام‌سازی عوض نمی‌کند (مالکیت مدیر)، پس کلیدها تازه‌اند و ردیف‌های قدیمی غیرفعال می‌شوند.
        "eng.package", "eng.tender", "eng.mr",
        "sec.contract", "contract.contracts", "contract.vendors", "contract.po", "contract.milestones", "contract.claims",
        "sec.inventory", "wh.dashboard", "wh.material", "wh.irn", "wh.ledger", "wh.sr", "wh.replenishment", "wh.masterdata",
        "wh.partcatalogue", "wh.partcatalogue.admin",
    };

    private const string S = NavigationMenuItemKinds.Section;
    private const string H = NavigationMenuItemKinds.Subheading;
    private const string L = NavigationMenuItemKinds.Link;
    private const string Always = NavigationVisibilityRules.Always;

    private static NavigationMenuItemDefinition Link(string key, string? parent, int order, string titleKey, string icon, string controller,
        string action = "Index", string? area = null, string rule = Always, bool isDefault = false, string? badge = null, string? extraActive = null)
        => new(key, parent, L, order, titleKey, icon, rule, area, controller, action, IsDefaultForController: isDefault, ExtraActiveControllers: extraActive, BadgeKey: badge);

    private static NavigationMenuItemDefinition Section(string key, int order, string titleKey, string icon, string rule, string extraActive, string? activeArea = null)
        => new(key, null, S, order, titleKey, icon, rule, ExtraActiveControllers: extraActive, ActiveArea: activeArea);

    private static NavigationMenuItemDefinition Sub(string key, string parent, int order, string titleKey)
        => new(key, parent, H, order, titleKey, null, Always);

    private static NavigationMenuItemDefinition ComingSoon(string key, string parent, int order, string titleKey, string icon, string title, string section, string rule = Always)
        => new(key, parent, L, order, titleKey, icon, rule,
            ExternalPath: "/ComingSoon?title=" + Uri.EscapeDataString(title) + "&section=" + Uri.EscapeDataString(section));

    public static IReadOnlyList<NavigationMenuItemDefinition> All { get; } = Build();

    private static List<NavigationMenuItemDefinition> Build() => new()
    {
        // ---- سطح بالا
        new("home", null, L, 10, "Dashboard", "bi-speedometer2", Always, ExternalPath: "/p/home"),   // SCRUM-705: a definition page (/p/...)
        new("exec.dashboard", null, L, 15, "Exec_NavTitle", "bi-graph-up-arrow", "Perm:EngineeringDocuments.View", ExternalPath: "/p/executive-dashboard"),   // SCRUM-697: a definition page (/p/...)
        new("mywork", null, L, 20, "My Work", "bi-list-check", Always, BadgeKey: NavigationBadges.MyWorkPending, ExternalPath: "/p/my-work"),   // SCRUM-730: a definition page (/p/...)
        new("quicksetup", null, L, 30, "Quick Setup", "bi-rocket-takeoff", "Module:QuickSetup", ExternalPath: "/p/quick-setup"),   // SCRUM-743: a definition page (/p/...)
        new("importcenter", null, L, 40, "IC_Title", "bi-cloud-arrow-up", "Module:ImportCenter", ExternalPath: "/p/import-center"),   // SCRUM-706: a definition page (/p/...)

        // ---- مدیریت مهندسی
        Section("sec.eng", 100, "EngineeringManagement", "bi-building", "Module:EngineeringManagement",
            "EngineeringDashboard,Projects,EngineeringDocuments,DocumentRegister,DocumentRevisions,DocumentWorkflow,VDR,Transmittal,MDB"),
        new("eng.dashboard", "sec.eng", L, 10, "EngineeringDashboard", "bi-speedometer2", Always, ExternalPath: "/p/engineering-dashboard"),   // SCRUM-688: a definition page (/p/...)
        new("eng.projects", "sec.eng", L, 20, "Projects", "bi-kanban", Always, ExternalPath: "/p/projects"),   // SCRUM-740: a definition page (/p/...), not a controller
        Link("eng.documents", "sec.eng", 30, "EngineeringDocuments", "bi-file-earmark-text", "EngineeringDocuments"),
        Link("eng.register", "sec.eng", 40, "DocumentRegister", "bi-journal-bookmark", "DocumentRegister"),
        Link("eng.revisions", "sec.eng", 50, "Document Revision", "bi-clock-history", "DocumentRevisions"),
        Link("eng.docworkflow", "sec.eng", 60, "Document Workflow", "bi-diagram-2", "DocumentWorkflow"),
        Link("eng.vdr", "sec.eng", 70, "VDR", "bi-journal-check", "VDR"),
        Link("eng.transmittal", "sec.eng", 80, "Transmittal", "bi-send", "Transmittal"),
        Link("eng.mdb", "sec.eng", 90, "MDB", "bi-collection", "MDB"),
        new("eng.disciplines", "sec.eng", L, 130, "Disciplines", "bi-diagram-3", "Perm:DisciplineWorkspace.View", ExternalPath: "/p/discipline-workspaces"),   // SCRUM-682: a definition page (/p/...)

        // ---- هوش مصنوعی و دانش
        Section("sec.ai", 200, "AI & Knowledge", "bi-robot", "Module:AIIntelligence",
            "EngineeringDocumentIntelligence,KnowledgeBase,AISearch,ChatAssistant,AIInsights"),
        Link("ai.docintel", "sec.ai", 10, "DocumentIntelligence", "bi-diagram-3-fill", "EngineeringDocumentIntelligence", rule: "Perm:EngineeringDocuments.View"),
        new("ai.kb", "sec.ai", L, 20, "KnowledgeBaseSearch", "bi-stars", Always, ExternalPath: "/p/knowledge-base"),   // SCRUM-712: a definition page (/p/...), not a controller
        new("ai.search", "sec.ai", L, 30, "AI Search", "bi-search-heart", Always, ExternalPath: "/p/ai-search"),   // SCRUM-669: a definition page (/p/...)
        new("ai.chat", "sec.ai", L, 40, "Engineering Chat Assistant", "bi-chat-dots", Always, ExternalPath: "/p/chat-assistant"),   // SCRUM-672: a definition page (/p/...)
        new("ai.copilot", "sec.ai", L, 50, "Engineering Copilot", "bi-stars", Always, ExternalPath: "/p/ai-insights"),   // SCRUM-668: a definition page (/p/...)

        // ---- مکاتبات و کنترل مدارک
        Section("sec.correspondence", 300, "CorrespondenceDocControl", "bi-envelope-paper", "Module:CorrespondenceDocControl", "Letter,DCC,TechnicalQuery"),
        Link("corr.letters", "sec.correspondence", 10, "Correspondence", "bi-envelope-paper", "Letter", isDefault: true),
        Link("corr.referrals", "sec.correspondence", 20, "MyReferrals", "bi-person-check", "Letter", "MyReferrals"),
        Link("corr.dcc", "sec.correspondence", 30, "DCC", "bi-building-gear", "DCC"),
        new("corr.tq", "sec.correspondence", L, 40, "TQ / TSR / RFI", "bi-question-circle", Always, ExternalPath: "/p/technical-queries"),   // SCRUM-758: a definition page (/p/...)

        // ---- کنترل کیفیت
        Section("sec.quality", 400, "QualityControl", "bi-patch-check", "Module:QualityControl", "QC,MOC"),
        Link("quality.qc", "sec.quality", 10, "QC / QA", "bi-patch-check", "QC"),
        new("quality.moc", "sec.quality", L, 20, "MOC", "bi-arrow-repeat", Always, ExternalPath: "/p/mocs"),   // SCRUM-720: a definition page (/p/...)

        // ---- تدارکات و زنجیره‌ی تأمین (SCRUM-423): خرید، قرارداد، فروشنده و انبار در یک بخش، مثل CMMS. هر پیوند همان دروازه‌ی
        // قبلی‌اش را دارد به‌اضافه‌ی ماژولِ بخشِ قبلی‌اش (مهندسی/قرارداد/انبار)، پس هیچ نقشی پیوندی بیشتر یا کمتر نمی‌بیند؛ بخش وقتی
        // دیده می‌شود که کاربر یکی از آن ماژول‌ها را ببیند.
        Section("sec.procurement", 500, "Nav_ProcurementSupplyChain", "bi-cart3", "Module:EngineeringManagement|Module:ContractManagement|Module:Inventory|Flag:PartCatalogue|Flag:PartCatalogueReports",
            "ProcurementPackage,Tender,MaterialRequisition,Contract,Vendor,PurchaseOrder,Inventory,MaterialItem,IrnReceipt,InventoryMasterData,InventoryTransaction,StoreRequisition,Replenishment,PartCatalogueRequest,PartCatalogueReport,PartCatalogueAdmin"),
        new("proc.vendors", "sec.procurement", L, 10, "Vendors", "bi-building", "Module:ContractManagement&Perm:Vendors.View", ExternalPath: "/p/vendors"),   // SCRUM-763: a definition page (/p/...)
        Sub("proc.sub.purchasing", "sec.procurement", 20, "Nav_Proc_Purchasing"),
        // SCRUM-471: the procurement cartable (pre-procurement queue), first under Purchasing
        new("proc.preprocurement", "proc.sub.purchasing", L, 5, "Ppr_Title", "bi-inbox", "Perm:PreProcurement.View", ExternalPath: "/p/pre-procurement"),   // SCRUM-735: a definition page (/p/...)
        new("proc.mr", "proc.sub.purchasing", L, 10, "Material Requisition", "bi-cart-check", "Module:EngineeringManagement&Perm:MaterialRequisition.View", ExternalPath: "/p/material-requisitions"),   // SCRUM-726: a definition page (/p/...)
        new("proc.package", "proc.sub.purchasing", L, 20, "ProcurementPackage", "bi-folder2-open", "Module:EngineeringManagement&Perm:ProcurementPackage.View", ExternalPath: "/p/procurement-packages"),   // SCRUM-736: a definition page (/p/...)
        new("proc.tender", "proc.sub.purchasing", L, 30, "Tender", "bi-clipboard-check", "Module:EngineeringManagement&Perm:Tender.View", ExternalPath: "/p/tenders"),   // SCRUM-759: a definition page (/p/...)
        new("proc.po", "proc.sub.purchasing", L, 40, "Purchase Orders", "bi-receipt", "Module:ContractManagement&Perm:PurchaseOrder.View", ExternalPath: "/p/purchase-orders"),   // SCRUM-741: a definition page (/p/...)
        Sub("proc.sub.contracts", "sec.procurement", 30, "Contracts"),
        new("proc.contracts", "proc.sub.contracts", L, 10, "Contracts", "bi-file-earmark-ruled", "Module:ContractManagement&Perm:Contracts.View", ExternalPath: "/p/contracts"),   // SCRUM-676: a definition page (/p/...)
        ComingSoon("proc.milestones", "proc.sub.contracts", 20, "Contract Milestones", "bi-flag", "Contract Milestones", "Contract Management", "Module:ContractManagement"),
        ComingSoon("proc.claims", "proc.sub.contracts", 30, "Claims", "bi-exclamation-diamond", "Claims", "Contract Management", "Module:ContractManagement"),
        Sub("proc.sub.warehouse", "sec.procurement", 40, "Warehouse"),
        new("proc.wh.dashboard", "proc.sub.warehouse", L, 10, "Dashboard", "bi-speedometer2", "Module:Inventory&Perm:MaterialItem.View", ExternalPath: "/p/inventory"),   // SCRUM-652: a definition page (/p/...)
        new("proc.wh.material", "proc.sub.warehouse", L, 20, "Material Master", "bi-box-seam", "Module:Inventory&Perm:MaterialItem.View", ExternalPath: "/p/material-items"),   // SCRUM-656: a definition page (/p/...)
        new("proc.wh.irn", "proc.sub.warehouse", L, 30, "Incoming Receipts (IRN)", "bi-truck", "Module:Inventory&Perm:IRN.View", ExternalPath: "/p/irn-receipts"),   // SCRUM-655: a definition page (/p/...)
        new("proc.wh.ledger", "proc.sub.warehouse", L, 40, "Transaction Ledger", "bi-list-ul", "Module:Inventory&Perm:MaterialItem.View", ExternalPath: "/p/inventory-ledger"),   // SCRUM-654: a definition page (/p/...)
        new("proc.wh.sr", "proc.sub.warehouse", L, 50, "Store Requisitions", "bi-clipboard-check", "Module:Inventory&Perm:StoreRequisition.View", BadgeKey: NavigationBadges.StoreRequisitionsAwaitingApproval, ExternalPath: "/p/store-requisitions"),   // SCRUM-664: a definition page (/p/...)
        new("proc.wh.replenishment", "proc.sub.warehouse", L, 60, "Repl_Menu", "bi-cart-plus", "Module:Inventory&Flag:Replenishment", ExternalPath: "/p/replenishment"),   // SCRUM-663: a definition page (/p/...)
        new("proc.wh.masterdata", "proc.sub.warehouse", L, 70, "Master Data", "bi-collection", "Module:Inventory&Perm:InventoryMasterData.View", ExternalPath: "/p/inventory-master-data"),   // SCRUM-653: a definition page (/p/...)
        new("proc.wh.partcatalogue", "proc.sub.warehouse", L, 80, "Pc_Menu", "bi-journal-plus", "Flag:PartCatalogue", ExternalPath: "/p/pc-requests", BadgeKey: NavigationBadges.MyWorkPartCatalogue),   // SCRUM-662: a definition page (/p/...)
        // SCRUM-478: Part Catalogue reports
        new("proc.wh.partcatalogue.reports", "proc.sub.warehouse", L, 85, "PcRpt_Menu", "bi-bar-chart-line", "Flag:PartCatalogueReports", ExternalPath: "/p/pc-report"),   // SCRUM-661: a definition page (/p/...)
        new("proc.wh.partcatalogue.admin", "proc.sub.warehouse", L, 90, "Pc_Title_Admin", "bi-sliders", "Flag:PartCatalogueAdmin", ExternalPath: "/p/pc-admin"),   // SCRUM-658: a definition page (/p/...)

        // ---- کنترل پروژه
        Section("sec.projctrl", 600, "ProjectControls", "bi-sliders", "Module:ProjectControls", "Task,TaskManager,Tag,Report,Meeting"),
        Link("proj.tasks", "sec.projctrl", 10, "Tasks", "bi-check2-square", "Task", rule: "Perm:Task.View", badge: NavigationBadges.TasksOpen),
        Link("proj.taskmanager", "sec.projctrl", 15, "Nav_TaskManager", "bi-kanban", "TaskManager", rule: "Perm:Task.View"),
        new("proj.meetings", "sec.projctrl", L, 20, "Meetings", "bi-people", Always, ExternalPath: "/p/meetings"),   // SCRUM-728: a definition page (/p/...)
        new("proj.reports", "sec.projctrl", L, 30, "Reports", "bi-graph-up", Always, ExternalPath: "/p/report-correspondence"),   // SCRUM-747: a definition page (/p/...)

        // ---- CMMS (قاعده‌ی هر آیتم همان IsCmmsItemVisible: مجوز + Override مدیر)
        Section("sec.cmms", 700, "CmmsIntegration", "bi-tools", "Module:CmmsIntegration",
            "Equipment,Instrument,Asset,MaintenanceStrategy,FailureModeCatalog,StrategyImport,MaintenancePlan,MaintenanceCalendar,JobPlan,WorkRequest,WorkOrder,SparePart,Team,Crew,ShiftHandover,MaintenanceMasterData,EquipmentClassification,RcmFmea,EquipmentCriticality,CriticalityAssessment,FunctionalLocation,Barcode,Reliability,Inspection,Kpi,Permit,CmmsReadiness,CmmsOperations"),
        Link("cmms.equipment", "sec.cmms", 10, "Equipment", "bi-gear", "Equipment", rule: "CmmsItem:Equipment"),
        Link("cmms.instrument", "sec.cmms", 20, "Instrument Passport", "bi-cpu", "Instrument", rule: "CmmsItem:Instrument"),
        Link("cmms.asset", "sec.cmms", 30, "Assets", "bi-diagram-2", "Asset", rule: "CmmsItem:Asset"),
        Sub("cmms.sub.planning", "sec.cmms", 40, "Maintenance Planning"),
        Link("cmms.strategy", "cmms.sub.planning", 10, "Maintenance Strategy", "bi-diagram-2", "MaintenanceStrategy", rule: "CmmsItem:MaintenanceStrategy"),
        new("cmms.plan", "cmms.sub.planning", L, 20, "Maintenance Plan", "bi-calendar-check", "CmmsItem:MaintenancePlan", ExternalPath: "/p/maintenance-plans"),   // SCRUM-724: a definition page (/p/...)
        new("cmms.backlog", "cmms.sub.planning", L, 30, "Maintenance Backlog", "bi-hourglass-split", "CmmsItem:CmmsOperations", ExternalPath: "/p/maintenance-backlog"),   // SCRUM-673: a definition page (/p/...)
        Link("cmms.calendar", "cmms.sub.planning", 40, "Maintenance Calendar", "bi-calendar3-week", "MaintenanceCalendar", rule: "CmmsItem:MaintenanceCalendar"),
        new("cmms.jobplan", "sec.cmms", L, 50, "Job Plans", "bi-list-check", "CmmsItem:JobPlan", ExternalPath: "/p/job-plans"),   // SCRUM-711: a definition page (/p/...)
        new("cmms.workrequest", "sec.cmms", L, 60, "Work Requests", "bi-clipboard-plus", "CmmsItem:WorkRequest", ExternalPath: "/p/work-requests"),   // SCRUM-765: a definition page (/p/...)
        Link("cmms.workorder", "sec.cmms", 70, "Work Order", "bi-clipboard-check", "WorkOrder", rule: "CmmsItem:WorkOrder", badge: NavigationBadges.WorkOrdersOpen),
        new("cmms.sparepart", "sec.cmms", L, 80, "Spare Parts", "bi-tools", "CmmsItem:SparePart", ExternalPath: "/p/spare-parts"),   // SCRUM-751: a definition page (/p/...)
        new("cmms.team", "sec.cmms", L, 90, "Teams", "bi-people", "CmmsItem:Team", ExternalPath: "/p/teams"),   // SCRUM-757: a definition page (/p/...)
        new("cmms.crew", "sec.cmms", L, 100, "Crews", "bi-people-fill", "CmmsItem:Crew", ExternalPath: "/p/crews"),   // SCRUM-677: a definition page (/p/...)
        new("cmms.shifthandover", "sec.cmms", L, 110, "Shift Handovers", "bi-arrow-left-right", "CmmsItem:ShiftHandover", ExternalPath: "/p/shift-handovers"),   // SCRUM-749: a definition page (/p/...)
        new("cmms.masterdata", "sec.cmms", L, 120, "Maintenance Master Data", "bi-collection", "CmmsItem:MaintenanceMasterData", ExternalPath: "/p/maintenance-master-data"),   // SCRUM-723: a definition page (/p/...)
        Link("cmms.classification", "sec.cmms", 130, "Equipment Classification", "bi-diagram-3-fill", "EquipmentClassification", rule: "CmmsItem:EquipmentClassification"),
        new("cmms.readiness", "sec.cmms", L, 140, "CMMS Readiness", "bi-clipboard-check", "CmmsItem:CmmsReadiness", ExternalPath: "/p/cmms-readiness"),   // SCRUM-674: a definition page (/p/...)
        new("cmms.operations", "sec.cmms", L, 150, "CMMS Operations", "bi-speedometer2", "CmmsItem:CmmsOperations", ExternalPath: "/p/cmms-operations"),   // SCRUM-673
        Link("cmms.rcm", "sec.cmms", 160, "RCM / FMEA", "bi-clipboard2-pulse", "RcmFmea", rule: "CmmsItem:RcmFmea"),
        Link("cmms.criticality", "sec.cmms", 170, "Criticality Analysis", "bi-shield-exclamation", "EquipmentCriticality", rule: "CmmsItem:EquipmentCriticality"),
        Link("cmms.assessment", "sec.cmms", 180, "Criticality Assessment", "bi-clipboard2-check", "CriticalityAssessment", "Dashboard", rule: "CmmsItem:CriticalityAssessment"),
        Link("cmms.reliability", "sec.cmms", 190, "Reliability", "bi-graph-up-arrow", "Reliability", "Dashboard", rule: "CmmsItem:Reliability"),
        new("cmms.inspection", "sec.cmms", L, 200, "Inspections", "bi-clipboard-pulse", "CmmsItem:Inspection", ExternalPath: "/p/inspections"),   // SCRUM-707: a definition page (/p/...)
        new("cmms.kpi", "sec.cmms", L, 210, "KPI Dashboard", "bi-speedometer2", "CmmsItem:Kpi", ExternalPath: "/p/kpi"),   // SCRUM-713: a definition page (/p/...)
        new("cmms.permit", "sec.cmms", L, 220, "Permits", "bi-shield-check", "CmmsItem:Permit", ExternalPath: "/p/permits"),   // SCRUM-734: a definition page (/p/...)
        Link("cmms.fl", "sec.cmms", 230, "Functional Locations", "bi-diagram-3", "FunctionalLocation", rule: "CmmsItem:FunctionalLocation"),
        new("cmms.barcode", "sec.cmms", L, 240, "Barcode", "bi-upc-scan", "CmmsItem:Barcode", ExternalPath: "/p/barcodes"),   // SCRUM-671: a definition page (/p/...)

        // ---- عملیات و تولید
        Section("sec.operations", 800, "Operations & Production", "bi-diagram-3-fill", "Module:Operations", "Production,ProductionOrder,ProductionRun"),
        new("ops.dashboard", "sec.operations", L, 10, "Dashboard", "bi-speedometer2", "Perm:Production.View", ExternalPath: "/p/production-dashboard"),   // SCRUM-737: a definition page (/p/...)
        new("ops.orders", "sec.operations", L, 20, "Production Orders", "bi-clipboard-data", "Perm:Production.View", ExternalPath: "/p/production-orders"),   // SCRUM-738: a definition page (/p/...)

        // ---- مدیریت سیستم (برای دارنده‌ی DisciplineMaster بدون Administration فقط زیرگروه سازمان/دیسیپلین دیده می‌شود)
        Section("sec.admin", 1000, "Administration", "bi-gear", "Module:Administration|Flag:DisciplineMaster",
            "Settings,Users,Department,Employee,OrganizationImport,Intake,OCRProcessing,AIExtraction,TableExtraction,Relationship",
            activeArea: "Admin"),
        new("admin.settings", "sec.admin", L, 10, "Settings", "bi-gear", "Module:Administration", ExternalPath: "/p/settings"),   // SCRUM-748: a definition page (/p/...), not a controller
        // SCRUM-40: الگوهای شماره‌گذاری اسناد تدارکات (کنترلر فقط SystemAdmin)
        // SCRUM-638: a definition page (/p/...), not a controller
        new("admin.numbering", "sec.admin", L, 15, "Num_Title", "bi-123", "Module:Administration&Role:SystemAdmin", ExternalPath: "/p/numbering"),
        Sub("admin.sub.access", "sec.admin", 20, "User & Access"),
        // SCRUM-650: a definition page (/p/...), not a controller
        new("admin.users", "admin.sub.access", L, 10, "Users", "bi-people", "Module:Administration", ExternalPath: "/p/users"),
        // SCRUM-644: a definition page (/p/...), not a controller
        new("admin.roles", "admin.sub.access", L, 20, "Roles", "bi-shield-check", "Module:Administration", ExternalPath: "/p/roles"),
        // SCRUM-651: a definition page (/p/...), not a controller
        new("admin.workflow", "admin.sub.access", L, 30, "Workflow Management", "bi-diagram-3", "Module:Administration", ExternalPath: "/p/workflow-management"),
        new("admin.navmenu", "admin.sub.access", L, 35, "Nav_Admin_Title", "bi-menu-button-wide", "Module:Administration&Role:SystemAdmin", ExternalPath: "/p/navigation-menu"),   // a definition page (/p/...), not a controller
        // SCRUM-634: a definition page (/p/...), not a controller
        new("admin.help", "admin.sub.access", L, 40, "Help Administration", "bi-life-preserver", "Module:Administration", ExternalPath: "/p/help-admin"),
        Sub("admin.sub.org", "sec.admin", 30, "Organization"),
        new("admin.departments", "admin.sub.org", L, 10, "Departments", "bi-building", "Module:Administration", ExternalPath: "/p/departments"),   // SCRUM-681: a definition page (/p/...), not a controller
        new("admin.employees", "admin.sub.org", L, 20, "Employees", "bi-people", "Module:Administration", ExternalPath: "/p/employees"),   // SCRUM-687: a definition page (/p/...)
        new("admin.disciplines", "admin.sub.org", L, 30, "Disc_Master_Nav", "bi-sliders", "Module:Administration|Flag:DisciplineMaster", ExternalPath: "/p/disciplines"),   // SCRUM-682
        // SCRUM-639: definition pages (/p/...), not a controller
        new("admin.positions", "admin.sub.org", L, 40, "Org_Positions_Nav", "bi-person-badge", "Module:Administration", ExternalPath: "/p/organization-positions"),
        new("admin.org", "admin.sub.org", L, 50, "Org_Title", "bi-diagram-3", "Module:Administration", ExternalPath: "/p/organization-chart"),
        Sub("admin.sub.ai", "sec.admin", 40, "AI Administration"),
        new("admin.intake", "admin.sub.ai", L, 10, "AIIntakeQueue", "bi-inboxes", "Module:Administration", ExternalPath: "/p/intake"),   // SCRUM-709: a definition page (/p/...)
        Link("admin.ocr", "admin.sub.ai", 20, "OCR Processing", "bi-badge-ad", "OCRProcessing", rule: "Module:Administration&Perm:OCRProcessing.View"),
        new("admin.aiextraction", "admin.sub.ai", L, 30, "AI Extraction", "bi-cpu", "Module:Administration&Perm:AIExtraction.View", ExternalPath: "/p/ai-extraction"),   // SCRUM-667: a definition page (/p/...)
        Link("admin.tables", "admin.sub.ai", 40, "Table Extraction", "bi-table", "TableExtraction", rule: "Module:Administration&Perm:TableExtraction.View"),
        Link("admin.relationship", "admin.sub.ai", 50, "RelationshipExplorer", "bi-diagram-3", "Relationship", rule: "Module:Administration&Perm:Relationship.View"),
        // SCRUM-645: a definition page (/p/...), not a controller
        new("admin.routing", "admin.sub.ai", L, 60, "AI Routing Rules", "bi-signpost-split", "Module:Administration", ExternalPath: "/p/routing-rules"),
        new("admin.routingconfidence", "admin.sub.ai", L, 61, "Confidence Gates", "bi-speedometer2", "Module:Administration", ExternalPath: "/p/routing-confidence"),
        // SCRUM-632: a definition page (/p/...), not a controller
        new("admin.contractauthority", "admin.sub.ai", L, 70, "Contract Approval Authority", "bi-shield-check", "Module:Administration", ExternalPath: "/p/contract-approval-authority"),
        // SCRUM-470: the PO authority table
        // SCRUM-643: a definition page (/p/...), not a controller
        new("admin.poauthority", "admin.sub.ai", L, 75, "Po_AuthorityTitle", "bi-receipt-cutoff", "Module:Administration", ExternalPath: "/p/po-authority"),
        Sub("admin.sub.monitoring", "sec.admin", 50, "System Monitoring"),
        // SCRUM-629, 630: the audit pages are definition pages (/p/...), not controllers
        new("admin.auditlog", "admin.sub.monitoring", L, 10, "AuditLog", "bi-clock-history", "Module:Administration", ExternalPath: "/p/audit-log"),
        new("admin.authaudit", "admin.sub.monitoring", L, 20, "Authorization Audit", "bi-journal-check", "Module:Administration", ExternalPath: "/p/authorization-audit"),
        // SCRUM-633: a definition page (/p/...), not a controller
        new("admin.dbupdate", "admin.sub.monitoring", L, 25, "Database Update", "bi-database-up", "Module:Administration", ExternalPath: "/p/database-update"),
        // SCRUM-635: a definition page (/p/...), not a controller
        new("admin.license", "admin.sub.monitoring", L, 26, "Licence", "bi-key", "Module:Administration", ExternalPath: "/p/license"),
        new("admin.performance", "admin.sub.monitoring", L, 30, "Performance Testing", "bi-speedometer2", "Module:Administration", ExternalPath: "/p/performance"),   // SCRUM-733: a definition page (/p/...), not a controller
        new("admin.hangfire", "admin.sub.monitoring", L, 40, "Background Jobs Dashboard", "bi-hdd-stack", "Module:Administration",
            ExternalPath: "/hangfire", OpenInNewTab: true),
    };
}
