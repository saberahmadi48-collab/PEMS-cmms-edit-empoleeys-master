using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Context;

public class PEMSDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, int>, IWorkflowDbContext
{
    public PEMSDbContext(DbContextOptions<PEMSDbContext> options)
        : base(options)
    {

    }


    // این دو مقدار توسط یک Middleware ساده در PEMS.Web ابتدای هر Request از روی کاربر لاگین‌شده
    // پر می‌شوند (نه از طریق IHttpContextAccessor داخل خود Persistence، تا وابستگی اضافه به
    // پکیج‌های ASP.NET Core در این لایه اضافه نشود).
    public int? CurrentUserId { get; set; }
    public string? CurrentUserName { get; set; }


    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }

    // طبق سند RBAC-ABAC Authorization Architecture (فاز ۱): مدل داده‌ی هسته‌ی مجوزدهی عمومی -
    // RolePermission/RoleModuleAction دست‌نخورده باقی می‌مانند، این‌ها لایه‌ی عمومی‌شده‌ی زیرشان است
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermissionGrant> RolePermissionGrants { get; set; }
    public DbSet<RoleAssignment> RoleAssignments { get; set; }
    public DbSet<AuthorizationAuditLog> AuthorizationAuditLogs { get; set; }

    // طبق سند RBAC-ABAC Authorization Architecture (فاز ۲): سلسله‌مراتب سازمانی/سمت + تفکیک وظایف
    public DbSet<Position> Positions { get; set; }
    public DbSet<SoDPolicy> SoDPolicies { get; set; }

    // طبق سند «PEMS Phase 6 — User Module & Navigation Visibility Architecture»: تنها Entity
    // تازه‌ی این فاز - استثنای دستیِ Visibility، عمداً جدا از مدل داده‌ی Authorization بالا
    public DbSet<NavigationVisibilityOverride> NavigationVisibilityOverrides { get; set; }

    // سایدبار داده‌محور (مدیریت توسط SystemAdmin؛ قاعده‌ی نمایش آیتم‌های سیستمی از کد)
    public DbSet<NavigationMenuItem> NavigationMenuItems { get; set; }

    public DbSet<AIDocumentClassification> AIDocumentClassifications { get; set; }
    public DbSet<DocumentTagLink> DocumentTagLinks { get; set; }
    public DbSet<DocumentComment> DocumentComments { get; set; }

    public DbSet<Document> Documents { get; set; }
    public DbSet<ContractDocument> ContractDocuments { get; set; }
    public DbSet<MaintenanceDocument> MaintenanceDocuments { get; set; }


    // نام‌گذاری «LegacyUsers» تا با DbSet<ApplicationUser> Users که از IdentityUserContext
    // به ارث می‌رسد تداخل نکند. نام جدول فیزیکی دیتابیس همچنان "Users" است (پایین‌تر در
    // OnModelCreating با ToTable("Users") مشخص شده) تا هیچ Migration/داده‌ی موجودی خراب نشود.
    public DbSet<User> LegacyUsers { get; set; }
    public DbSet<Discipline> Disciplines { get; set; }
    public DbSet<DocumentTypeCode> DocumentTypeCodes { get; set; }
    public DbSet<VendorDocumentTypologyCode> VendorDocumentTypologyCodes { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Employee> Employees { get; set; }


    public DbSet<EngineeringDocument> EngineeringDocuments { get; set; }
    public DbSet<DocumentAttachment> DocumentAttachments { get; set; }
    public DbSet<DocumentRevision> DocumentRevisions { get; set; }
    public DbSet<DocumentWorkflow> DocumentWorkflows { get; set; }


    public DbSet<Equipment> Equipments { get; set; }
    public DbSet<EquipmentTag> EquipmentTags { get; set; }
    public DbSet<Instrument> Instruments { get; set; }
    public DbSet<CalibrationHistory> CalibrationHistories { get; set; }
    public DbSet<TableExtractionBatch> TableExtractionBatches { get; set; }
    public DbSet<TableExtractionRow> TableExtractionRows { get; set; }
    public DbSet<IOPoint> IOPoints { get; set; }
    public DbSet<EquipmentDatasheet> EquipmentDatasheets { get; set; }


    public DbSet<ControlLoop> ControlLoops { get; set; }
    public DbSet<Alarm> Alarms { get; set; }
    public DbSet<CauseEffect> CauseEffects { get; set; }


    public DbSet<ElectricalEquipment> ElectricalEquipments { get; set; }
    public DbSet<Cable> Cables { get; set; }
    public DbSet<Panel> Panels { get; set; }
    public DbSet<CableConnection> CableConnections { get; set; }


    public DbSet<MechanicalEquipment> MechanicalEquipments { get; set; }
    public DbSet<RotatingEquipment> RotatingEquipments { get; set; }
    public DbSet<StaticEquipment> StaticEquipments { get; set; }


    public DbSet<PipingLine> PipingLines { get; set; }
    public DbSet<Valve> Valves { get; set; }
    public DbSet<PipingComponent> PipingComponents { get; set; }
    public DbSet<PipeSpecification> PipeSpecifications { get; set; }
    public DbSet<EquipmentNozzle> EquipmentNozzles { get; set; }


    public DbSet<Asset> Assets { get; set; }
    public DbSet<FunctionalLocation> FunctionalLocations { get; set; }
    public DbSet<EquipmentCriticality> EquipmentCriticalities { get; set; }


    public DbSet<MaintenanceStrategy> MaintenanceStrategies { get; set; }
    public DbSet<MaintenanceStrategyAssignment> MaintenanceStrategyAssignments { get; set; }
    public DbSet<MaintenanceStrategyHistory> MaintenanceStrategyHistories { get; set; }
    public DbSet<MaintenancePlan> MaintenancePlans { get; set; }
    public DbSet<WorkOrder> WorkOrders { get; set; }
    public DbSet<MaintenanceHistory> MaintenanceHistories { get; set; }
    public DbSet<SparePart> SpareParts { get; set; }
    public DbSet<MaintenanceSpareUsage> MaintenanceSpareUsages { get; set; }
    public DbSet<FailureRecord> FailureRecords { get; set; }

    // طبق سند Inventory & Material Management: Material Master
    public DbSet<MaterialCategory> MaterialCategories { get; set; }
    public DbSet<MaterialUOM> MaterialUOMs { get; set; }
    public DbSet<MaterialItem> MaterialItems { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<StorageLocation> StorageLocations { get; set; }
    public DbSet<MaterialStock> MaterialStocks { get; set; }
    public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
    public DbSet<MaterialReservation> MaterialReservations { get; set; }

    // Phase 2 / P5 - توصیه‌های تأمین (فقط‌خواندن از موجودی/تقاضا؛ هیچ MR/PO/رزرویی نمی‌سازد)
    public DbSet<NetDemandRun> NetDemandRuns { get; set; }
    public DbSet<ReplenishmentRecommendation> ReplenishmentRecommendations { get; set; }
    public DbSet<ReplenishmentRecommendationSource> ReplenishmentRecommendationSources { get; set; }
    public DbSet<ReplenishmentRecommendationHistory> ReplenishmentRecommendationHistories { get; set; }
    public DbSet<StoreRequisition> StoreRequisitions { get; set; }

    // ---- Warehouse: درخواست کاتالوگ قطعه (Part Catalogue Request)
    public DbSet<PartCatalogueTechnicalClass> PartCatalogueTechnicalClasses { get; set; }
    public DbSet<PartCatalogueCommodityGroup> PartCatalogueCommodityGroups { get; set; }
    public DbSet<PartCatalogueProductFamily> PartCatalogueProductFamilies { get; set; }
    public DbSet<PartCatalogueSection> PartCatalogueSections { get; set; }
    public DbSet<PartCatalogueManufacturer> PartCatalogueManufacturers { get; set; }
    public DbSet<PartCatalogueSupplier> PartCatalogueSuppliers { get; set; }
    public DbSet<PartCatalogueCurrency> PartCatalogueCurrencies { get; set; }
    public DbSet<PartCatalogueStockGrade> PartCatalogueStockGrades { get; set; }
    public DbSet<PartCataloguePlanningMethod> PartCataloguePlanningMethods { get; set; }
    public DbSet<PartCatalogueTypeDesignation> PartCatalogueTypeDesignations { get; set; }
    public DbSet<PartCatalogueSectionCommodityGroup> PartCatalogueSectionCommodityGroups { get; set; }
    public DbSet<PartCatalogueCommodityGroupChild> PartCatalogueCommodityGroupChildren { get; set; }
    public DbSet<PartCatalogueWorkflowStep> PartCatalogueWorkflowSteps { get; set; }
    public DbSet<PartCatalogueApproverAssignment> PartCatalogueApproverAssignments { get; set; }
    public DbSet<PartCatalogueOrgLink> PartCatalogueOrgLinks { get; set; }
    public DbSet<PartCatalogueRequest> PartCatalogueRequests { get; set; }
    public DbSet<PartCatalogueSettings> PartCatalogueSettings { get; set; }
    public DbSet<KpiTarget> KpiTargets { get; set; }
    public DbSet<PartCatalogueRequestEquipment> PartCatalogueRequestEquipment { get; set; }
    public DbSet<PartCatalogueRequestAttribute> PartCatalogueRequestAttributes { get; set; }
    public DbSet<PartCatalogueRequestNote> PartCatalogueRequestNotes { get; set; }
    public DbSet<PartCatalogueRequestAttachment> PartCatalogueRequestAttachments { get; set; }
    public DbSet<PartCatalogueRequestStep> PartCatalogueRequestSteps { get; set; }
    public DbSet<PartCatalogueRequestApproval> PartCatalogueRequestApprovals { get; set; }
    public DbSet<StoreRequisitionLine> StoreRequisitionLines { get; set; }
    public DbSet<StoreRequisitionStatusHistory> StoreRequisitionStatusHistories { get; set; }
    public DbSet<WorkOrderMaterial> WorkOrderMaterials { get; set; }
    public DbSet<MrsImportBatch> MrsImportBatches { get; set; }
    public DbSet<MrsImportRow> MrsImportRows { get; set; }
    public DbSet<MrsImportRowMetadata> MrsImportRowMetadata { get; set; }
    public DbSet<NumberingSequence> NumberingSequences { get; set; }

    // طبق ممیزی Material Management (Gap A) + Step 1/Step 2: IRN - سند رسمی دریافت مادی،
    // جدا از MrsImportBatch/Row (که مکانیزم Import اکسل است).
    public DbSet<IrnReceipt> IrnReceipts { get; set; }
    public DbSet<IrnReceiptLine> IrnReceiptLines { get; set; }
    public DbSet<IrnReceiptStatusHistory> IrnReceiptStatusHistories { get; set; }
    public DbSet<IrnDispositionRecord> IrnDispositionRecords { get; set; }

    // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ (Production Core) - همه‌ی
    // موجودیت‌های جدید، بدون تغییر در جدول‌های بالا (Warehouse/StorageLocation/MaterialStock/
    // InventoryTransaction دست‌نخورده می‌مانند - طبق قاعده‌ی REUSE > EXTEND > CREATE).
    public DbSet<ProductionWorkCenter> ProductionWorkCenters { get; set; }
    public DbSet<ShiftDefinition> ShiftDefinitions { get; set; }
    public DbSet<ProductionOrder> ProductionOrders { get; set; }
    public DbSet<ProductionRun> ProductionRuns { get; set; }
    public DbSet<ProductionBatch> ProductionBatches { get; set; }
    public DbSet<ProductionInput> ProductionInputs { get; set; }
    public DbSet<ProductionOutput> ProductionOutputs { get; set; }
    public DbSet<ProductionLoss> ProductionLosses { get; set; }
    public DbSet<ProductionEvent> ProductionEvents { get; set; }

    // طبق «PEMS Operations Phase 2A — Foundational Hardening»: بدون تغییر در جدول‌های فاز ۱ -
    // فقط دو موجودیت جدید (Target/Downtime) و یک ستون افزودنی روی InventoryTransaction (بالاتر
    // در Configuration خودش، نه این‌جا).
    public DbSet<ProductionTarget> ProductionTargets { get; set; }
    public DbSet<ProductionDowntimeEvent> ProductionDowntimeEvents { get; set; }

    // طبق CMMS Production Baseline v1 بند ۱۰.۳-۱۰.۴ (CMMS-02: Maintenance Planning)
    public DbSet<JobPlan> JobPlans { get; set; }
    public DbSet<MaintenanceTask> MaintenanceTasks { get; set; }
    public DbSet<JobPlanRequiredSkill> JobPlanRequiredSkills { get; set; }
    public DbSet<JobPlanRequiredMaterial> JobPlanRequiredMaterials { get; set; }
    public DbSet<WorkOrderTask> WorkOrderTasks { get; set; }

    // طبق CMMS Production Baseline v1 بند P0-05/۸-۹ (CMMS-03: Work Management)
    public DbSet<WorkRequest> WorkRequests { get; set; }
    public DbSet<WorkRequestStatusHistory> WorkRequestStatusHistories { get; set; }

    public DbSet<Permit> Permits { get; set; }
    public DbSet<PermitStatusHistory> PermitStatusHistories { get; set; }
    public DbSet<RoleModuleAction> RoleModuleActions { get; set; }

    public DbSet<IsolationPoint> IsolationPoints { get; set; }
    public DbSet<IsolationStatusHistory> IsolationStatusHistories { get; set; }

    public DbSet<GasTest> GasTests { get; set; }
    public DbSet<GasReading> GasReadings { get; set; }
    public DbSet<GasParameterLimit> GasParameterLimits { get; set; }
    public DbSet<GasTestValidityRule> GasTestValidityRules { get; set; }
    public DbSet<GasTestStatusHistory> GasTestStatusHistories { get; set; }

    public DbSet<SimopsConflict> SimopsConflicts { get; set; }
    public DbSet<SimopsInteractionRule> SimopsInteractionRules { get; set; }
    public DbSet<SimopsConflictStatusHistory> SimopsConflictStatusHistories { get; set; }
    public DbSet<ShiftHandover> ShiftHandovers { get; set; }
    public DbSet<ShiftHandoverItem> ShiftHandoverItems { get; set; }
    public DbSet<ShiftHandoverStatusHistory> ShiftHandoverStatusHistories { get; set; }

    public DbSet<ExecutionGateDecision> ExecutionGateDecisions { get; set; }
    public DbSet<PermitExecutionRequirement> PermitExecutionRequirements { get; set; }

    public DbSet<WebAuthnCredential> WebAuthnCredentials { get; set; }
    public DbSet<PermitApprovalCompetencyRequirement> PermitApprovalCompetencyRequirements { get; set; }
    public DbSet<EmployeeAreaScope> EmployeeAreaScopes { get; set; }
    public DbSet<PermitApprovalAuditRecord> PermitApprovalAuditRecords { get; set; }


    public DbSet<InspectionRecord> InspectionRecords { get; set; }
    public DbSet<InspectionChecklist> InspectionChecklists { get; set; }


    public DbSet<Transmittal> Transmittals { get; set; }
    public DbSet<TransmittalRecipient> TransmittalRecipients { get; set; }
    public DbSet<DocumentHold> DocumentHolds { get; set; }
    public DbSet<TransmittalItem> TransmittalItems { get; set; }

    public DbSet<MaterialRequisition> MaterialRequisitions { get; set; }
    public DbSet<MaterialRequisitionItem> MaterialRequisitionItems { get; set; }
    public DbSet<MaterialRequisitionQuotation> MaterialRequisitionQuotations { get; set; }

    public DbSet<AIExtractedTag> AIExtractedTags { get; set; }
    public DbSet<AIExtractedSpecification> AIExtractedSpecifications { get; set; }

    public DbSet<ChatMessage> ChatMessages { get; set; }

    // طبق «PEMS Bilingual Help & Knowledge System»
    public DbSet<HelpCategory> HelpCategories { get; set; }
    public DbSet<HelpArticle> HelpArticles { get; set; }
    public DbSet<HelpArticleTranslation> HelpArticleTranslations { get; set; }
    public DbSet<HelpRelatedArticle> HelpRelatedArticles { get; set; }
    public DbSet<HelpVisual> HelpVisuals { get; set; }
    public DbSet<HelpStandardReference> HelpStandardReferences { get; set; }


    public DbSet<Vendor> Vendors { get; set; }
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
    public DbSet<VDRItem> VDRItems { get; set; }
    public DbSet<VDRSubmission> VDRSubmissions { get; set; }
    public DbSet<VDRImportBatch> VDRImportBatches { get; set; }
    public DbSet<VDRImportRow> VDRImportRows { get; set; }
    public DbSet<MDRImportBatch> MDRImportBatches { get; set; }
    public DbSet<MDRImportRow> MDRImportRows { get; set; }
    public DbSet<ManufacturerDataBook> ManufacturerDataBooks { get; set; }
    public DbSet<MDBDocument> MDBDocuments { get; set; }

    // ارجاع IDC (فاز D - IDC واقعی) - کدام دیسیپلین‌ها برای این مدرک انتخاب شده‌اند
    public DbSet<DocumentIdcReferral> DocumentIdcReferrals { get; set; }

    // بخش QC/QA - دیسیپلین‌های QC (جدا از دیسیپلین مهندسی)، ITP، و NCR
    public DbSet<QCDiscipline> QCDisciplines { get; set; }
    public DbSet<InspectionTestPlan> InspectionTestPlans { get; set; }
    public DbSet<ITPLineItem> ITPLineItems { get; set; }
    public DbSet<NonConformanceReport> NonConformanceReports { get; set; }

    // فاز ۱ نقشه‌راه معماری: TQ/TSR/RFI مستقل
    public DbSet<TechnicalQuery> TechnicalQueries { get; set; }

    // فاز ۲ نقشه‌راه معماری: MOC مستقل
    public DbSet<MOC> MOCs { get; set; }

    // بازنگری CMMS: نقش‌ها و گروه‌های کاری تعمیرونگهداری
    public DbSet<MaintenanceRole> MaintenanceRoles { get; set; }
    public DbSet<MaintenanceGroup> MaintenanceGroups { get; set; }

    // طبق «PEMS — Enterprise Maintenance Master Data & Standards Implementation»: تاکسونومی
    // مرجع نگهداری (Master Data) - جدا از MaintenanceStrategy/MaintenanceTask که رکوردهای
    // تراکنشی‌اند
    public DbSet<MaintenanceType> MaintenanceTypes { get; set; }
    public DbSet<MaintenanceStrategyType> MaintenanceStrategyTypes { get; set; }
    public DbSet<EquipmentClass> EquipmentClasses { get; set; }
    public DbSet<StandardReference> StandardReferences { get; set; }
    public DbSet<MaintenanceActivity> MaintenanceActivities { get; set; }
    public DbSet<MaintenanceActivityEquipmentClass> MaintenanceActivityEquipmentClasses { get; set; }
    public DbSet<MaintenanceActivityStrategyType> MaintenanceActivityStrategyTypes { get; set; }
    public DbSet<MaintenanceActivityStandardReference> MaintenanceActivityStandardReferences { get; set; }

    // طبق «PEMS Phase 2 — Equipment Classification + ISO 14224 Failure Taxonomy + PM/Job Plan
    // Intelligence»: تاکسونومی مرجع خرابی + قالب نگهداری پیشگیرانه
    public DbSet<FailureMode> FailureModes { get; set; }
    public DbSet<FailureCause> FailureCauses { get; set; }
    public DbSet<FailureMechanism> FailureMechanisms { get; set; }
    public DbSet<FailureEffect> FailureEffects { get; set; }
    public DbSet<FailureConsequence> FailureConsequences { get; set; }
    public DbSet<FailureModeCause> FailureModeCauses { get; set; }
    public DbSet<FailureCauseMechanism> FailureCauseMechanisms { get; set; }
    public DbSet<PMTemplate> PMTemplates { get; set; }

    // طبق «PEMS Phase 3 — Equipment Classification Completion» بند ۹: صف بازبینی انسانی
    public DbSet<EquipmentClassificationSuggestion> EquipmentClassificationSuggestions { get; set; }

    // طبق «PEMS Phase 4 — RCM/FMEA/FMECA Decision Intelligence»
    public DbSet<FunctionalRequirement> FunctionalRequirements { get; set; }
    public DbSet<FunctionalFailure> FunctionalFailures { get; set; }
    public DbSet<FMEAAnalysis> FMEAAnalyses { get; set; }
    public DbSet<FMEAItem> FMEAItems { get; set; }
    public DbSet<RCMAnalysis> RCMAnalyses { get; set; }
    public DbSet<RCMDecision> RCMDecisions { get; set; }

    // طبق «PEMS Phase 7 — Maintenance Intelligence & Reliability Operationalization» بند ۲۰
    public DbSet<RcmReviewRecommendation> RcmReviewRecommendations { get; set; }

    // طبق «PEMS Phase 7.6 — Engineering Criticality Assessment & Controlled Maintenance Activation»
    public DbSet<CriticalityAssessment> CriticalityAssessments { get; set; }
    public DbSet<CriticalityAssessmentEvidence> CriticalityAssessmentEvidences { get; set; }

    // طبق «PEMS Phase 9 — Enterprise Reliability, RAM & Predictive Maintenance Intelligence»
    public DbSet<ConditionMonitoringPoint> ConditionMonitoringPoints { get; set; }
    public DbSet<ConditionReading> ConditionReadings { get; set; }
    public DbSet<PredictiveMaintenanceModel> PredictiveMaintenanceModels { get; set; }
    public DbSet<PredictiveMaintenanceResult> PredictiveMaintenanceResults { get; set; }
    public DbSet<AIReliabilityInsight> AIReliabilityInsights { get; set; }

    // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۱
    public DbSet<EvaluationProfile> EvaluationProfiles { get; set; }
    public DbSet<EvaluationLimit> EvaluationLimits { get; set; }
    public DbSet<Finding> Findings { get; set; }

    // طبق «PEMS — Equipment Passport Audit & CBM» فاز ۱ ممیزی §3.7
    public DbSet<ConditionPriorityMapping> ConditionPriorityMappings { get; set; }

    // طبق «Functional Location Master Data Import»
    public DbSet<FunctionalLocationImportBatch> FunctionalLocationImportBatches { get; set; }
    public DbSet<FunctionalLocationImportRow> FunctionalLocationImportRows { get; set; }

    // طبق «PEMS CMMS — Equipment Functional Location Assignment & Import»: همون الگوی
    // Staging/Batch بالا - فقط برای پیشنهاد/تایید تخصیص Equipment.FunctionalLocationId موجود،
    // نه ساخت رکورد جدید.
    public DbSet<EquipmentFunctionalLocationAssignmentBatch> EquipmentFunctionalLocationAssignmentBatches { get; set; }
    public DbSet<EquipmentFunctionalLocationAssignmentRow> EquipmentFunctionalLocationAssignmentRows { get; set; }

    // طبق «PEMS CMMS — Data Migration & Controlled Data Onboarding»: همون الگوی Staging/Batch
    // بالا - برای رکورد تاریخچه‌ی نگهداری/تعمیر (MaintenanceHistory، Append-only).
    public DbSet<MaintenanceHistoryImportBatch> MaintenanceHistoryImportBatches { get; set; }
    public DbSet<MaintenanceHistoryImportRow> MaintenanceHistoryImportRows { get; set; }

    // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۹
    public DbSet<ConditionReadingImportBatch> ConditionReadingImportBatches { get; set; }
    public DbSet<ConditionReadingImportRow> ConditionReadingImportRows { get; set; }

    // طبق «PEMS — Enterprise Barcode System»: یک جدول هویت مشترک برای کل PEMS - نه یک جدول
    // جدا به‌ازای هر ماژول (Equipment/Material/Warehouse/StorageLocation).
    public DbSet<BarcodeIdentity> BarcodeIdentities { get; set; }

    // طبق «PEMS — Enterprise File Storage Management»
    public DbSet<StorageProvider> StorageProviders { get; set; }
    public DbSet<StorageRoute> StorageRoutes { get; set; }

    public DbSet<Team> Teams { get; set; }
    public DbSet<TeamMember> TeamMembers { get; set; }
    public DbSet<Crew> Crews { get; set; }
    public DbSet<CrewMember> CrewMembers { get; set; }

    // طبق «PEMS Dynamic Maintenance Organization»
    public DbSet<EmployeeAssignment> EmployeeAssignments { get; set; }

    // طبق PEMS_CMMS_MAINTENANCE_ORGANIZATION_BLUEPRINT.md: مهارت‌ها
    public DbSet<Skill> Skills { get; set; }
    public DbSet<EmployeeSkill> EmployeeSkills { get; set; }
    public DbSet<RequiredSkill> RequiredSkills { get; set; }

    // طبق PEMS_CMMS_MAINTENANCE_ORGANIZATION_BLUEPRINT.md بخش ۱۸-۱۹: صلاحیت و گواهینامه
    public DbSet<Qualification> Qualifications { get; set; }
    public DbSet<EmployeeQualification> EmployeeQualifications { get; set; }
    public DbSet<Certification> Certifications { get; set; }
    public DbSet<EmployeeCertification> EmployeeCertifications { get; set; }

    // طبق سند معماری Labor Cost
    public DbSet<EmployeeLaborRate> EmployeeLaborRates { get; set; }
    public DbSet<WorkOrderLabor> WorkOrderLabors { get; set; }

    // طبق درخواست: ثبت نظر چند دیسیپلین روی یک WorkOrder
    public DbSet<WorkOrderDisciplineReferral> WorkOrderDisciplineReferrals { get; set; }
    public DbSet<WorkOrderDisciplineFinding> WorkOrderDisciplineFindings { get; set; }

    // طبق PEMS_CMMS_ARCHITECTURE.md بخش ۸: تاریخچه‌ی تغییر وضعیت WorkOrder (چه‌کسی/چه‌زمانی/چرا)
    public DbSet<WorkOrderStatusHistory> WorkOrderStatusHistories { get; set; }
    public DbSet<WorkOrderChangeLog> WorkOrderChangeLogs { get; set; }

    // ==============================================================================
    // Workflow Platform (فاز A) - این DbSet ها اینترفیس IWorkflowDbContext (تعریف‌شده در
    // پروژه‌ی مستقل PEMS.WorkflowPlatform) را پیاده‌سازی می‌کنند؛ طبق اصل Dependency
    // Inversion، PEMS.WorkflowPlatform هیچ وابستگی‌ای به این پروژه ندارد.
    // ==============================================================================
    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }
    public DbSet<WorkflowVersion> WorkflowVersions { get; set; }
    public DbSet<WorkflowStage> WorkflowStages { get; set; }
    public DbSet<WorkflowTransition> WorkflowTransitions { get; set; }
    public DbSet<WorkflowInstance> WorkflowInstances { get; set; }
    public DbSet<WorkflowInstanceStage> WorkflowInstanceStages { get; set; }
    public DbSet<WorkflowInstanceStageCandidate> WorkflowInstanceStageCandidates { get; set; }
    public DbSet<WorkflowActorResponsibility> WorkflowActorResponsibilities { get; set; }
    public DbSet<WorkflowActorResponsibilityPosition> WorkflowActorResponsibilityPositions { get; set; }
    public DbSet<CommodityGroupResponsible> CommodityGroupResponsibles { get; set; }
    public DbSet<WorkflowHistory> WorkflowHistories { get; set; }
    public DbSet<WorkflowBinding> WorkflowBindings { get; set; }
    public DbSet<WorkflowConfigAudit> WorkflowConfigAudits { get; set; }

    public DbSet<EquipmentImportBatch> EquipmentImportBatches { get; set; }
    public DbSet<EquipmentImportRow> EquipmentImportRows { get; set; }

    public DbSet<RoutingRule> RoutingRules { get; set; }
    public DbSet<AIConfidenceSettings> AIConfidenceSettings { get; set; }
    public DbSet<EngineeringRelationship> EngineeringRelationships { get; set; }

    public DbSet<KnowledgeBaseEntry> KnowledgeBaseEntries { get; set; }

    public DbSet<SavedFilter> SavedFilters { get; set; }

    public DbSet<UserDashboardWidget> UserDashboardWidgets { get; set; }

    public DbSet<LetterStar> LetterStars { get; set; }

    public DbSet<LetterTemplate> LetterTemplates { get; set; }

    public DbSet<LetterApprovalStep> LetterApprovalSteps { get; set; }
    public DbSet<LetterVersion> LetterVersions { get; set; }
    public DbSet<LetterViewLog> LetterViewLogs { get; set; }
    public DbSet<LetterNumberSequence> LetterNumberSequences { get; set; }

    public DbSet<LetterRecipient> LetterRecipients { get; set; }
    public DbSet<CorrespondentParty> CorrespondentParties { get; set; }
    public DbSet<LetterLink> LetterLinks { get; set; }

    // کارتابل نامه‌نگاری: وضعیت شخصی (سطل/آرشیو)، تفویض، قانون مسیریابی، گروه مخاطبین، یادآور، تنظیمات
    public DbSet<LetterUserState> LetterUserStates { get; set; }
    public DbSet<LetterDelegation> LetterDelegations { get; set; }
    public DbSet<LetterRoutingRule> LetterRoutingRules { get; set; }
    public DbSet<LetterContactGroup> LetterContactGroups { get; set; }
    public DbSet<LetterContactGroupMember> LetterContactGroupMembers { get; set; }
    public DbSet<LetterReminder> LetterReminders { get; set; }
    public DbSet<LetterUserSetting> LetterUserSettings { get; set; }

    public DbSet<Tag> Tags { get; set; }
    public DbSet<LetterTag> LetterTags { get; set; }
    public DbSet<TaskItem> TaskItems { get; set; }

    // Task Manager (فضا، طرح وضعیت، Sprint، نظر، پیوند، ناظر) - روی همان TaskItem، نه موجودیت موازی
    public DbSet<WorkSpace> WorkSpaces { get; set; }
    public DbSet<WorkSpaceMember> WorkSpaceMembers { get; set; }
    public DbSet<WorkItemType> WorkItemTypes { get; set; }
    public DbSet<WorkItemStatus> WorkItemStatuses { get; set; }
    public DbSet<WorkItemStatusTransition> WorkItemStatusTransitions { get; set; }
    public DbSet<Sprint> Sprints { get; set; }
    public DbSet<WorkItemComment> WorkItemComments { get; set; }
    public DbSet<WorkItemLink> WorkItemLinks { get; set; }
    public DbSet<WorkItemWatcher> WorkItemWatchers { get; set; }

    public DbSet<Meeting> Meetings { get; set; }
    public DbSet<MeetingAttendee> MeetingAttendees { get; set; }

    public DbSet<Letter> Letters { get; set; }
    public DbSet<LetterReferral> LetterReferrals { get; set; }
    public DbSet<LetterReview> LetterReviews { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    public DbSet<ProcurementPackage> ProcurementPackages { get; set; }
    public DbSet<ProcurementCommunication> ProcurementCommunications { get; set; }
    public DbSet<ProcurementCommunicationResponse> ProcurementCommunicationResponses { get; set; }
    public DbSet<ProcurementApproval> ProcurementApprovals { get; set; }

    // Package Procurement Contract Finalization (Phase 1 — Domain/Data Foundation)
    public DbSet<VendorQualificationHistory> VendorQualificationHistories { get; set; }
    public DbSet<ContractRevision> ContractRevisions { get; set; }
    public DbSet<ContractDisciplineReview> ContractDisciplineReviews { get; set; }
    public DbSet<Tender> Tenders { get; set; }
    public DbSet<TenderInvitedVendor> TenderInvitedVendors { get; set; }
    public DbSet<Bid> Bids { get; set; }
    public DbSet<BidEvaluation> BidEvaluations { get; set; }
    public DbSet<BidNegotiation> BidNegotiations { get; set; }
    public DbSet<ContractNTP> ContractNTPs { get; set; }
    public DbSet<ContractApprovalAuthorityRule> ContractApprovalAuthorityRules { get; set; }

    public DbSet<DisciplineTask> DisciplineTasks { get; set; }



    public override int SaveChanges()
    {
        // Phase 1 Hardening: نسخه‌ی Published/Retired گردش‌کار تغییرناپذیر است (ساختار، مراحل، گذارها)
        WorkflowVersionImmutabilityGuard.Enforce(this);

        ApplyCreatedDate();

        var pending = PrepareAuditEntries();

        var result = base.SaveChanges();

        if (pending.Count > 0)
        {
            FinalizeAuditEntries(pending);
            base.SaveChanges();
        }

        return result;
    }


    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await WorkflowVersionImmutabilityGuard.EnforceAsync(this, cancellationToken);

        ApplyCreatedDate();

        var pending = PrepareAuditEntries();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pending.Count > 0)
        {
            FinalizeAuditEntries(pending);
            await base.SaveChangesAsync(cancellationToken);
        }

        return result;
    }


    // =========================
    // Audit Log (Security Requirements: Audit Log, Change History)
    // =========================

    private void ApplyCreatedDate()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                var property = entry.Entity
                    .GetType()
                    .GetProperty("CreatedDate");

                if (property != null && property.PropertyType == typeof(DateTime))
                {
                    entry.Property("CreatedDate").CurrentValue = DateTime.Now;
                }
            }

            // BaseEntity: CreatedBy / ModifiedBy خودکار از کاربر جاری (CurrentUserName) پر می‌شود
            if (entry.Entity is PEMS.Domain.Common.BaseEntity)
            {
                if (entry.State == EntityState.Added)
                    entry.Property("CreatedBy").CurrentValue = CurrentUserName;

                if (entry.State is EntityState.Added or EntityState.Modified)
                    entry.Property("ModifiedBy").CurrentValue = CurrentUserName;
            }
        }
    }


    private sealed class PendingAudit
    {
        public Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry = null!;
        public string EntityName = string.Empty;
        public string Action = string.Empty;
        public string? ChangedFieldsSummary;
        public string? KnownKeyValue;
    }


    private static readonly HashSet<string> NeverAuditedProperties = new(StringComparer.Ordinal) { "SecurityStamp", "ConcurrencyStamp", "RowVersion" };
    private static readonly HashSet<string> MaskedAuditProperties = new(StringComparer.Ordinal) { "PasswordHash" };
    private static readonly HashSet<string> SecuritySensitiveEntities = new(StringComparer.Ordinal) { "ApplicationUser", "IdentityUserRole`1", "RoleAssignment", "RolePermissionGrant", "ApplicationRole" };

    private List<PendingAudit> PrepareAuditEntries()
    {
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog)
                continue;

            if (entry.State != EntityState.Added &&
                entry.State != EntityState.Modified &&
                entry.State != EntityState.Deleted)
                continue;

            var entityName = entry.Entity.GetType().Name;

            var action = entry.State switch
            {
                EntityState.Added => "Insert",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            string? changedSummary = null;

            if (entry.State == EntityState.Modified)
            {
                var changes = new List<string>();

                foreach (var prop in entry.Properties)
                {
                    if (!prop.IsModified) continue;
                    // E24: مقادیر محرمانه/فنی هرگز در ممیزی ذخیره نمی‌شوند (قبلاً هش رمز قبل→بعد در ChangedFieldsSummary می‌ماند)
                    if (NeverAuditedProperties.Contains(prop.Metadata.Name)) continue;

                    var oldVal = prop.OriginalValue?.ToString() ?? "null";
                    var newVal = prop.CurrentValue?.ToString() ?? "null";

                    if (oldVal == newVal) continue;

                    if (MaskedAuditProperties.Contains(prop.Metadata.Name)) { changes.Add($"{prop.Metadata.Name}: [changed]"); continue; }

                    changes.Add($"{prop.Metadata.Name}: {oldVal} -> {newVal}");
                }

                if (changes.Count == 0)
                    continue; // چیزی واقعاً تغییر نکرده، نیازی به ثبت نیست

                changedSummary = string.Join(" | ", changes);
            }

            // E24: ایجاد/حذف موجودیت‌های امنیتی (کاربر، نقش کاربر، تخصیص نقش، مجوز نقش) مقدار فیلدهای غیرمحرمانه را هم ثبت می‌کند (چه کسی/چه چیزی/چه زمانی)
            if (entry.State != EntityState.Modified && SecuritySensitiveEntities.Contains(entityName))
            {
                var values = entry.Properties
                    .Where(pr => pr.Metadata.ClrType.IsPrimitive || pr.Metadata.ClrType == typeof(string) || pr.Metadata.ClrType == typeof(int?) || pr.Metadata.ClrType == typeof(bool?) || pr.Metadata.ClrType == typeof(DateTime) || pr.Metadata.ClrType == typeof(DateTime?))
                    .Where(pr => !NeverAuditedProperties.Contains(pr.Metadata.Name) && !MaskedAuditProperties.Contains(pr.Metadata.Name))
                    .Select(pr => $"{pr.Metadata.Name}={(entry.State == EntityState.Deleted ? pr.OriginalValue : pr.CurrentValue)}");
                changedSummary = string.Join("; ", values);
                if (changedSummary.Length > 1000) changedSummary = changedSummary[..1000];
            }

            string? knownKey = null;

            if (entry.State != EntityState.Added)
            {
                var pk = entry.Metadata.FindPrimaryKey();

                if (pk != null && pk.Properties.Count > 0)
                    knownKey = entry.Property(pk.Properties[0].Name).CurrentValue?.ToString();
            }

            pending.Add(new PendingAudit
            {
                Entry = entry,
                EntityName = entityName,
                Action = action,
                ChangedFieldsSummary = changedSummary,
                KnownKeyValue = knownKey
            });
        }

        return pending;
    }


    private void FinalizeAuditEntries(List<PendingAudit> pending)
    {
        foreach (var p in pending)
        {
            var keyValue = p.KnownKeyValue;

            if (keyValue == null)
            {
                var pk = p.Entry.Metadata.FindPrimaryKey();

                if (pk != null && pk.Properties.Count > 0)
                    keyValue = p.Entry.Property(pk.Properties[0].Name).CurrentValue?.ToString();
            }

            AuditLogs.Add(new AuditLog
            {
                EntityName = p.EntityName,
                EntityKeyValue = keyValue ?? "",
                Action = p.Action,
                ChangedFieldsSummary = p.ChangedFieldsSummary,
                UserId = CurrentUserId,
                UserName = CurrentUserName,
                Timestamp = DateTime.Now
            });
        }
    }



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // طبق استاندارد جدید (Volume V - Database Design): پیکربندی Entity های اصلی که به‌مرور
        // فعال توسعه داده می‌شوند، هرکدام یک کلاس IEntityTypeConfiguration<T> جدا در
        // PEMS.Persistence/Configurations دارند، به‌جای پیکربندی داخل همین متد.
        // Entity های قدیمی‌تر که هنوز UI ندارند، فعلاً همچنان این‌جا پیکربندی می‌شوند تا زمانی
        // که واقعاً روی آن‌ها کار شود (ریسک بی‌دلیل تغییر چیزی که کار می‌کند را نمی‌پذیریم).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PEMSDbContext).Assembly);

        // Configurations پروژه‌ی مستقل PEMS.WorkflowPlatform (اسمبلی جداگانه)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowInstance).Assembly);


        // ==============================================================================
        // طبق فاز ۱ ممیزی گردش‌کار تایید مدارک: فیلد RowVersion روی BaseEntity قبلاً هیچ‌وقت
        // به EF Core به‌عنوان Concurrency Token واقعی معرفی نشده بود - یعنی وقتی دو کاربر
        // هم‌زمان روی یک مدرک Approve/Reject می‌زدند، دومی بی‌صدا اولی را می‌بلعید. فعلاً فقط
        // روی Document (و زیرکلاس‌های آن مثل EngineeringDocument از طریق TPT) فعال می‌شود -
        // نه سراسری روی همه‌ی Entity ها - تا ریسک تغییر رفتار ماژول‌های دیگر که هنوز با آن
        // هماهنگ نیستند صفر بماند.
        // ==============================================================================
        modelBuilder.Entity<PEMS.Domain.Entities.Document>()
            .Property(x => x.RowVersion)
            .IsRowVersion();


        // ==============================
        // Legacy User Entity (پیش از Identity)
        // ==============================
        // نام DbSet به "LegacyUsers" تغییر کرد تا با Users (از Identity) تداخل نکند،
        // ولی نام جدول فیزیکی همچنان "Users" است تا Migration/داده‌ی موجود دست‌نخورده بماند.

        modelBuilder.Entity<User>()
            .ToTable("Users");


        // ==============================
        // Transmittal
        // ==============================

        modelBuilder.Entity<Transmittal>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transmittal>()
            .HasOne(x => x.SentBy)
            .WithMany()
            .HasForeignKey(x => x.SentById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TransmittalItem>()
            .HasOne(x => x.Transmittal)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.TransmittalId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TransmittalItem>()
            .HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);


        // ==============================
        // Material Requisition / MRQ
        // ==============================
        // پیکربندی پایه‌ی MaterialRequisition به ProcurementConfigurations.cs منتقل شد.

        modelBuilder.Entity<MaterialRequisitionItem>()
            .HasOne(x => x.MR)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.MRId)
            .OnDelete(DeleteBehavior.Cascade);


        // ==============================
        // Procurement Package (Unified MR/MRQ/Communications)
        // ==============================
        // پیکربندی MaterialRequisitionQuotation, ProcurementPackage, ProcurementCommunication,
        // ProcurementApproval به PEMS.Persistence/Configurations/ProcurementConfigurations.cs منتقل شد.


        // ==============================
        // AI Extracted Data (Staging)
        // ==============================

        modelBuilder.Entity<AIExtractedTag>()
            .HasOne(x => x.Attachment)
            .WithMany(x => x.ExtractedTags)
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AIExtractedTag>()
            .HasOne(x => x.LinkedEquipment)
            .WithMany()
            .HasForeignKey(x => x.LinkedEquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AIExtractedTag>()
            .HasOne(x => x.CreatedTag)
            .WithMany()
            .HasForeignKey(x => x.CreatedTagId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AIExtractedTag>()
            .HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AIExtractedTag>()
            .Property(x => x.Confidence)
            .HasPrecision(5, 4);

        modelBuilder.Entity<AIExtractedSpecification>()
            .HasOne(x => x.Attachment)
            .WithMany(x => x.ExtractedSpecifications)
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AIExtractedSpecification>()
            .HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AIExtractedSpecification>()
            .Property(x => x.Confidence)
            .HasPrecision(5, 4);

        modelBuilder.Entity<DocumentAttachment>()
            .Property(x => x.AIConfidence)
            .HasPrecision(5, 4);

        modelBuilder.Entity<DocumentAttachment>()
            .HasOne(x => x.IntakeProject)
            .WithMany()
            .HasForeignKey(x => x.IntakeProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentAttachment>()
            .HasOne(x => x.UploadedBy)
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);


        // ==============================
        // Vendor / Contract / Purchase Order
        // ==============================
        // پیکربندی این‌ها طبق استاندارد جدید (Volume V - "یک کلاس IEntityTypeConfiguration به
        // ازای هر Entity، نه پیکربندی داخل DbContext") به PEMS.Persistence/Configurations منتقل شد.

        // ==============================
        // Enterprise Document Core (Table-Per-Type inheritance)
        // ==============================
        // پیکربندی Document, EngineeringDocument, VDRItem, ContractDocument, MaintenanceDocument,
        // DocumentComment به PEMS.Persistence/Configurations/DocumentCoreConfigurations.cs منتقل شد.

        modelBuilder.Entity<VDRSubmission>()
            .HasOne(x => x.VDRItem)
            .WithMany(x => x.Submissions)
            .HasForeignKey(x => x.VDRItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VDRSubmission>()
            .HasOne(x => x.Attachment)
            .WithMany()
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VDRSubmission>()
            .HasOne(x => x.CommentSheetAttachment)
            .WithMany()
            .HasForeignKey(x => x.CommentSheetAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VDRSubmission>()
            .HasOne(x => x.ReplySheetAttachment)
            .WithMany()
            .HasForeignKey(x => x.ReplySheetAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق درخواست: شماره رویژن تکراری برای یک قلم VDR هرگز نباید ثبت شود - حتی در
        // شرایط مسابقه یا وارد کردن دوباره‌ی همان فایل اکسل
        modelBuilder.Entity<VDRSubmission>()
            .HasIndex(x => new { x.VDRItemId, x.RevisionNo })
            .IsUnique();

        modelBuilder.Entity<VDRSubmission>()
            .HasOne(x => x.ClientTransmittal)
            .WithMany()
            .HasForeignKey(x => x.ClientTransmittalId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VDRImportBatch>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<VDRImportRow>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.VDRImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MDRImportBatch>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MDRImportRow>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.MDRImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // طبق درخواست: ایمپورت اکسل تجهیزات (CMMS)
        modelBuilder.Entity<EquipmentImportBatch>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EquipmentImportBatch>()
            .HasOne(x => x.ImportedBy)
            .WithMany()
            .HasForeignKey(x => x.ImportedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EquipmentImportRow>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.EquipmentImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // طبق «PEMS CMMS — Equipment Functional Location Assignment & Import»: همون الگوی
        // Batch/Row بالا - فقط Staging پیشنهاد تخصیص، بدون ساخت Equipment/FunctionalLocation جدید.
        modelBuilder.Entity<EquipmentFunctionalLocationAssignmentBatch>()
            .HasOne(x => x.ImportedBy)
            .WithMany()
            .HasForeignKey(x => x.ImportedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EquipmentFunctionalLocationAssignmentRow>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.EquipmentFunctionalLocationAssignmentBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // طبق «PEMS CMMS — Data Migration & Controlled Data Onboarding»: همون الگوی Batch/Row
        // بالا - Staging برای رکورد پیشنهادی MaintenanceHistory (بدون هیچ رکورد واقعی تا Commit).
        modelBuilder.Entity<MaintenanceHistoryImportBatch>()
            .HasOne(x => x.ImportedBy)
            .WithMany()
            .HasForeignKey(x => x.ImportedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MaintenanceHistoryImportRow>()
            .HasOne(x => x.Batch)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.MaintenanceHistoryImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // ==============================================================================
        // طبق «PEMS — Enterprise Barcode System»:
        // - Code باید در کل جدول یکتا باشد (طبق بند «Duplicate Protection»).
        // - برای هر Target حداکثر یک بارکد Active مجاز است - یک ایندکس یکتای فیلترشده
        //   (نه فقط بررسی سمت اپلیکیشن) تا Race واقعی هم در سطح دیتابیس مسدود شود
        //   (طبق بند «Concurrency-safe creation»).
        // - زنجیره‌ی Replace خودارجاع است (Restrict - نه Cascade، چون حذف یک بارکد هرگز
        //   زنجیره‌ی تاریخچه‌اش را نباید حذف کند؛ اصلاً Delete هم برای این جدول پیاده نشده).
        // ==============================================================================
        modelBuilder.Entity<BarcodeIdentity>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<BarcodeIdentity>()
            .HasIndex(x => new { x.TargetType, x.TargetId, x.Status })
            .IsUnique()
            .HasFilter("[Status] = 'Active'");

        modelBuilder.Entity<BarcodeIdentity>()
            .HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BarcodeIdentity>()
            .HasOne(x => x.DisabledBy)
            .WithMany()
            .HasForeignKey(x => x.DisabledById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BarcodeIdentity>()
            .HasOne(x => x.RevokedBy)
            .WithMany()
            .HasForeignKey(x => x.RevokedById)
            .OnDelete(DeleteBehavior.Restrict);

        // خودارجاع دوسویه‌ی ساده (نه WithOne) - هر دو جهت زنجیره‌ی Replace مستقل تنظیم می‌شوند
        // (در ReplaceAsync هر دو صریحاً مقداردهی می‌شوند)، نه با تکیه بر Navigation Fixup خودکار EF.
        modelBuilder.Entity<BarcodeIdentity>()
            .HasOne(x => x.ReplacedByBarcodeIdentity)
            .WithMany()
            .HasForeignKey(x => x.ReplacedByBarcodeIdentityId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BarcodeIdentity>()
            .HasOne(x => x.ReplacesBarcodeIdentity)
            .WithMany()
            .HasForeignKey(x => x.ReplacesBarcodeIdentityId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<DisciplineTask>()
            .HasOne(x => x.Discipline)
            .WithMany()
            .HasForeignKey(x => x.DisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DisciplineTask>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DisciplineTask>()
            .HasOne(x => x.ResponsibleEmployee)
            .WithMany()
            .HasForeignKey(x => x.ResponsibleEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۶-۷: تحویل مستندات نهایی (MDB)
        modelBuilder.Entity<ManufacturerDataBook>()
            .HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ManufacturerDataBook>()
            .HasOne(x => x.Vendor)
            .WithMany()
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ManufacturerDataBook>()
            .HasOne(x => x.Po)
            .WithMany()
            .HasForeignKey(x => x.PoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ManufacturerDataBook>()
            .HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MDBDocument>()
            .HasOne(x => x.MDB)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.MDBId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MDBDocument>()
            .HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق بخش G ممیزی (Transmittal Governance)
        modelBuilder.Entity<TransmittalRecipient>()
            .HasOne(x => x.Transmittal)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.TransmittalId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transmittal>()
            .HasOne(x => x.SupersedesTransmittal)
            .WithMany()
            .HasForeignKey(x => x.SupersedesTransmittalId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق بخش M ممیزی (Hold Model)
        modelBuilder.Entity<DocumentHold>()
            .HasOne(x => x.Document)
            .WithMany(x => x.Holds)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentHold>()
            .HasOne(x => x.HoldByEmployee)
            .WithMany()
            .HasForeignKey(x => x.HoldByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentHold>()
            .HasOne(x => x.ReleaseByEmployee)
            .WithMany()
            .HasForeignKey(x => x.ReleaseByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentHold>()
            .HasOne(x => x.ResponsibleDiscipline)
            .WithMany()
            .HasForeignKey(x => x.ResponsibleDisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        // Vendor.RiskScore precision -> منتقل شد به VendorConfiguration.cs

        modelBuilder.Entity<RolePermission>()
            .HasIndex(x => new { x.RoleName, x.ModuleName })
            .IsUnique();


        modelBuilder.Entity<AIDocumentClassification>()
            .HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AIDocumentClassification>()
            .HasOne(x => x.Attachment)
            .WithMany()
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AIDocumentClassification>()
            .Property(x => x.ConfidenceScore)
            .HasPrecision(5, 4);

        modelBuilder.Entity<DocumentTagLink>()
            .HasOne(x => x.Document)
            .WithMany(x => x.TagLinks)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentTagLink>()
            .HasOne(x => x.EquipmentTag)
            .WithMany()
            .HasForeignKey(x => x.EquipmentTagId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentTagLink>()
            .Property(x => x.Confidence)
            .HasPrecision(5, 4);


        // ==============================
        // Functional Location
        // ==============================

        modelBuilder.Entity<FunctionalLocation>()
            .HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS Production Baseline v1 بند ۶-۷: رابطه‌ی واقعی Equipment/Asset به
        // FunctionalLocation، به‌علاوه ساختار درختی Parent/Child خود Equipment.
        // Restrict (نه Cascade) عمداً - حذف یک FunctionalLocation نباید هزاران Equipment را
        // بی‌صاحب کند، دقیقاً هم‌ارز با انتخاب بالا برای خودِ FunctionalLocation.Parent.
        modelBuilder.Entity<Equipment>()
            .HasOne(x => x.FunctionalLocationRef)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Equipment>()
            .HasOne(x => x.ParentEquipment)
            .WithMany(x => x.ChildEquipments)
            .HasForeignKey(x => x.ParentEquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Asset>()
            .HasOne(x => x.FunctionalLocationRef)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS Production Baseline v1 بند ۱۰.۲: MaintenancePlan به یک Job Plan قابل استفاده‌ی
        // مجدد وصل می‌شود - Restrict چون حذف یک Job Plan نباید Planهای متکی به آن را بی‌صاحب کند.
        modelBuilder.Entity<MaintenancePlan>()
            .HasOne(x => x.JobPlan)
            .WithMany()
            .HasForeignKey(x => x.JobPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS Production Baseline v1 بند P0-05/۸ (CMMS-03: Work Management): همه‌ی ارجاعات
        // WorkRequest به موجودیت‌های دیگر اختیاری‌اند (یک درخواست ممکن است فقط به یک Functional
        // Location اشاره کند، نه لزوماً یک تجهیز مشخص) - همه Restrict، هم‌راستا با CMMS-01/02.
        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.Asset)
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.FunctionalLocation)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.SuggestedDiscipline)
            .WithMany()
            .HasForeignKey(x => x.SuggestedDisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.RequestedBy)
            .WithMany()
            .HasForeignKey(x => x.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.ApprovedBy)
            .WithMany()
            .HasForeignKey(x => x.ApprovedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkRequest>()
            .HasOne(x => x.LinkedWorkOrder)
            .WithMany()
            .HasForeignKey(x => x.LinkedWorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorkOrder>()
            .HasOne(x => x.SourceWorkRequest)
            .WithMany()
            .HasForeignKey(x => x.SourceWorkRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // ==============================================================================
        // طبق CMMS-10a ADR-01: Employee.VendorId اختیاری است (فقط پرسنل غیر-Internal پر
        // می‌شود) - Restrict، هم‌راستا با سایر ارجاعات این پروژه به Vendor.
        // ==============================================================================
        modelBuilder.Entity<Employee>()
            .HasOne(x => x.VendorRef)
            .WithMany()
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10a: ردیف‌های موجود Employee باید 'Internal' بگیرند، نه رشته‌ی خالی - EF
        // مقدار پیش‌فرض C# (= "Internal") را خودکار به DEFAULT ستون تبدیل نمی‌کند.
        modelBuilder.Entity<Employee>()
            .Property(x => x.PersonnelType)
            .HasDefaultValue("Internal");

        // طبق CMMS-10a بند ۸: WorkOrder 1 ─── N Permit - همه‌ی ارجاعات Permit اختیاری‌اند به‌جز
        // RequestedByEmployee (همون الگوی WorkRequest.RequestedById).
        modelBuilder.Entity<Permit>()
            .HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Permit>()
            .HasOne(x => x.FunctionalLocation)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Permit>()
            .HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Permit>()
            .HasOne(x => x.RequestedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.RequestedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Permit>()
            .HasOne(x => x.PermitIssuerEmployee)
            .WithMany()
            .HasForeignKey(x => x.PermitIssuerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Permit>()
            .HasOne(x => x.PermitReceiverEmployee)
            .WithMany()
            .HasForeignKey(x => x.PermitReceiverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitStatusHistory>()
            .HasOne(x => x.Permit)
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitStatusHistory>()
            .HasOne(x => x.ChangedBy)
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10c: IsolationPoint همیشه زیرمجموعه‌ی یک Permit است - همه‌ی ارجاعات دیگر
        // اختیاری‌اند (همون الگوی Permit خودش نسبت به Equipment/Asset/FunctionalLocation).
        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.Permit)
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.Asset)
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.FunctionalLocation)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.IsolatedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.IsolatedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.VerifiedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.VerifiedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationPoint>()
            .HasOne(x => x.DeisolatedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.DeisolatedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationStatusHistory>()
            .HasOne(x => x.IsolationPoint)
            .WithMany()
            .HasForeignKey(x => x.IsolationPointId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IsolationStatusHistory>()
            .HasOne(x => x.ChangedBy)
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10d: GasTest همیشه زیرمجموعه‌ی یک Permit است - فرزند موازی IsolationPoint
        // (همون Permit، دو زیرشاخه‌ی مستقل).
        modelBuilder.Entity<GasTest>()
            .HasOne(x => x.Permit)
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasTest>()
            .HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasTest>()
            .HasOne(x => x.FunctionalLocation)
            .WithMany()
            .HasForeignKey(x => x.FunctionalLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasTest>()
            .HasOne(x => x.TesterEmployee)
            .WithMany()
            .HasForeignKey(x => x.TesterEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasReading>()
            .HasOne(x => x.GasTest)
            .WithMany(x => x.Readings)
            .HasForeignKey(x => x.GasTestId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasTestStatusHistory>()
            .HasOne(x => x.GasTest)
            .WithMany()
            .HasForeignKey(x => x.GasTestId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GasTestStatusHistory>()
            .HasOne(x => x.ChangedBy)
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10e: SimopsConflict برخلاف بقیه‌ی فرزندان Permit این فاز، دو والد دارد
        // (PermitAId/PermitBId) - هر دو Restrict، بدون کالکشن معکوس روی Permit (همون الگوی
        // بدون-کالکشن IsolationPoint/GasTest).
        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.PermitA)
            .WithMany()
            .HasForeignKey(x => x.PermitAId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.PermitB)
            .WithMany()
            .HasForeignKey(x => x.PermitBId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.RaisedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.RaisedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.AssessedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.AssessedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.ResolvedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.ResolvedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflict>()
            .HasOne(x => x.AcceptedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.AcceptedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflictStatusHistory>()
            .HasOne(x => x.SimopsConflict)
            .WithMany()
            .HasForeignKey(x => x.SimopsConflictId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SimopsConflictStatusHistory>()
            .HasOne(x => x.ChangedBy)
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10e: ShiftHandover دو نفر مشخص دارد (Outgoing/Incoming)، نه یک Permit والد -
        // هر دو FK به Employee، Restrict.
        modelBuilder.Entity<ShiftHandover>()
            .HasOne(x => x.OutgoingEmployee)
            .WithMany()
            .HasForeignKey(x => x.OutgoingEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftHandover>()
            .HasOne(x => x.IncomingEmployee)
            .WithMany()
            .HasForeignKey(x => x.IncomingEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftHandoverItem>()
            .HasOne(x => x.ShiftHandover)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.ShiftHandoverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftHandoverStatusHistory>()
            .HasOne(x => x.ShiftHandover)
            .WithMany()
            .HasForeignKey(x => x.ShiftHandoverId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftHandoverStatusHistory>()
            .HasOne(x => x.ChangedBy)
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق سند PEMS-SEC-PERMIT-AUTH-001: WebAuthnCredential/PermitApprovalAuditRecord به
        // ApplicationUser (نه Employee) وصل‌اند - سطح احراز هویت، نه سطح کسب‌وکار.
        modelBuilder.Entity<WebAuthnCredential>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitApprovalCompetencyRequirement>()
            .HasOne(x => x.RequiredCertification)
            .WithMany()
            .HasForeignKey(x => x.RequiredCertificationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitApprovalCompetencyRequirement>()
            .HasOne(x => x.RequiredQualification)
            .WithMany()
            .HasForeignKey(x => x.RequiredQualificationId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmployeeAreaScope>()
            .HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitApprovalAuditRecord>()
            .HasOne(x => x.Permit)
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitApprovalAuditRecord>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PermitApprovalAuditRecord>()
            .HasOne(x => x.CredentialReference)
            .WithMany()
            .HasForeignKey(x => x.CredentialReferenceId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق CMMS-10f: ExecutionGateDecision هم StartWork عادی و هم EmergencyOverride را ثبت
        // می‌کند - یک والد Permit، بدون کالکشن معکوس (همون الگوی بقیه‌ی فرزندان این فاز).
        modelBuilder.Entity<ExecutionGateDecision>()
            .HasOne(x => x.Permit)
            .WithMany()
            .HasForeignKey(x => x.PermitId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExecutionGateDecision>()
            .HasOne(x => x.ActorEmployee)
            .WithMany()
            .HasForeignKey(x => x.ActorEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);



        // ==============================
        // Maintenance Relations
        // ==============================

        modelBuilder.Entity<MaintenancePlan>()
            .HasOne(x => x.MaintenanceStrategy)
            .WithMany()
            .HasForeignKey(x => x.MaintenanceStrategyId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<WorkOrder>()
            .HasOne(x => x.MaintenancePlan)
            .WithMany()
            .HasForeignKey(x => x.MaintenancePlanId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<MaintenanceHistory>()
            .HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<MaintenanceSpareUsage>()
            .HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<MaintenanceSpareUsage>()
            .HasOne(x => x.MaterialItem)
            .WithMany()
            .HasForeignKey(x => x.MaterialItemId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<FailureRecord>()
            .HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);


        modelBuilder.Entity<InspectionChecklist>()
            .HasOne(x => x.InspectionRecord)
            .WithMany()
            .HasForeignKey(x => x.InspectionRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        // ==============================
        // Engineering Documents
        // ==============================
        // (پیکربندی EngineeringDocument کامل به DocumentCoreConfigurations.cs منتقل شد)



        modelBuilder.Entity<DocumentAttachment>()
            .HasOne(x => x.Document)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);


        // EngineeringDocument - Revision

        modelBuilder.Entity<DocumentRevision>()
            .HasOne(x => x.Document)
            .WithMany(x => x.Revisions)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentRevision>()
            .HasOne(x => x.Reviewer)
            .WithMany()
            .HasForeignKey(x => x.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentRevision>()
            .HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentRevision>()
            .HasOne(x => x.SupersededRevision)
            .WithMany()
            .HasForeignKey(x => x.SupersededRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // طبق درخواست: هیچ‌وقت نباید دو رویژن با شماره‌ی یکسان برای یک مدرک ثبت شود - حتی
        // در شرایط مسابقه (Race Condition) که دو کاربر هم‌زمان "Create New Revision" بزنند
        modelBuilder.Entity<DocumentRevision>()
            .HasIndex(x => new { x.DocumentId, x.RevisionNo })
            .IsUnique();



        // EngineeringDocument - Workflow

        modelBuilder.Entity<DocumentWorkflow>()
            .HasOne(x => x.Document)
            .WithMany(x => x.Workflows)
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);




        // ==============================
        // Decimal Precision
        // ==============================


        modelBuilder.Entity<Alarm>()
            .Property(x => x.SetPoint)
            .HasPrecision(18, 3);



        modelBuilder.Entity<Cable>()
            .Property(x => x.Length)
            .HasPrecision(18, 3);



        modelBuilder.Entity<ElectricalEquipment>()
            .Property(x => x.PowerKW)
            .HasPrecision(18, 3);


        modelBuilder.Entity<ElectricalEquipment>()
            .Property(x => x.RatedCurrent)
            .HasPrecision(18, 3);


        modelBuilder.Entity<ElectricalEquipment>()
            .Property(x => x.Voltage)
            .HasPrecision(18, 3);



        modelBuilder.Entity<EquipmentDatasheet>()
            .Property(x => x.DesignPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<EquipmentDatasheet>()
            .Property(x => x.DesignTemperature)
            .HasPrecision(18, 3);


        modelBuilder.Entity<EquipmentDatasheet>()
            .Property(x => x.OperatingPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<EquipmentDatasheet>()
            .Property(x => x.OperatingTemperature)
            .HasPrecision(18, 3);




        modelBuilder.Entity<FailureRecord>()
            .Property(x => x.DowntimeHours)
            .HasPrecision(18, 3);

        modelBuilder.Entity<InspectionRecord>(entity =>
        {
            entity.Property(x => x.MeasurementValue).HasPrecision(18, 4);
            entity.Property(x => x.LowerLimit).HasPrecision(18, 4);
            entity.Property(x => x.UpperLimit).HasPrecision(18, 4);
        });

        modelBuilder.Entity<WorkOrder>()
            .Property(x => x.ContractorCost)
            .HasPrecision(18, 2);




        modelBuilder.Entity<Instrument>()
            .Property(x => x.RangeMin)
            .HasPrecision(18, 3);


        modelBuilder.Entity<Instrument>()
            .Property(x => x.RangeMax)
            .HasPrecision(18, 3);


        modelBuilder.Entity<Instrument>()
            .Property(x => x.AlarmSetpoint)
            .HasPrecision(18, 3);


        modelBuilder.Entity<Instrument>()
            .Property(x => x.TripSetpoint)
            .HasPrecision(18, 3);


        // یک‌به‌یک با Equipment: هر تجهیز حداکثر یک پروفایل Instrument دارد
        modelBuilder.Entity<Instrument>()
            .HasIndex(x => x.EquipmentId)
            .IsUnique();




        modelBuilder.Entity<MaintenanceHistory>()
            .Property(x => x.Cost)
            .HasPrecision(18, 2);


        modelBuilder.Entity<MaintenanceHistory>()
            .Property(x => x.DurationHours)
            .HasPrecision(18, 3);




        modelBuilder.Entity<MaintenanceSpareUsage>()
            .Property(x => x.Quantity)
            .HasPrecision(18, 3);


        modelBuilder.Entity<MaintenanceSpareUsage>()
            .Property(x => x.UnitCost)
            .HasPrecision(18, 2);


        modelBuilder.Entity<MaintenanceSpareUsage>()
            .Property(x => x.TotalCost)
            .HasPrecision(18, 2);




        // ==============================
        // Mechanical Equipment
        // ==============================


        modelBuilder.Entity<MechanicalEquipment>()
            .Property(x => x.DesignPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<MechanicalEquipment>()
            .Property(x => x.DesignTemperature)
            .HasPrecision(18, 3);


        modelBuilder.Entity<MechanicalEquipment>()
            .Property(x => x.OperatingPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<MechanicalEquipment>()
            .Property(x => x.OperatingTemperature)
            .HasPrecision(18, 3);

        // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۳:
        // دقیقاً همون الگوی Instrument (HasIndex(x => x.EquipmentId).IsUnique() پایین‌تر در همین
        // فایل) - یک Equipment حداکثر یک پروفایل Mechanical Equipment دارد. چون EquipmentId
        // Nullable است، این ایندکس فقط روی مقادیر غیر-NULL اعمال می‌شود (رفتار پیش‌فرض SQL Server) -
        // چند رکورد با EquipmentId=NULL هنوز مجازند، دقیقاً همون زنجیره‌ی اختیاری موجود.
        modelBuilder.Entity<MechanicalEquipment>().HasIndex(x => x.EquipmentId).IsUnique();


        // ==============================
        // Rotating Equipment
        // ==============================


        modelBuilder.Entity<RotatingEquipment>()
            .Property(x => x.FlowRate)
            .HasPrecision(18, 3);


        modelBuilder.Entity<RotatingEquipment>()
            .Property(x => x.Head)
            .HasPrecision(18, 3);


        modelBuilder.Entity<RotatingEquipment>()
            .Property(x => x.PowerKW)
            .HasPrecision(18, 3);

        // طبق فاز ۳: یک MechanicalEquipment حداکثر یک زیررده‌ی Rotating دارد (XOR با Static در
        // سطح Controller اعمال می‌شود؛ این ایندکس فقط از تکرار تصادفی همان زیررده جلوگیری می‌کند).
        modelBuilder.Entity<RotatingEquipment>().HasIndex(x => x.MechanicalEquipmentId).IsUnique();


        // ==============================
        // Static Equipment
        // ==============================


        modelBuilder.Entity<StaticEquipment>()
            .Property(x => x.DesignPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<StaticEquipment>()
            .Property(x => x.DesignTemperature)
            .HasPrecision(18, 3);


        modelBuilder.Entity<StaticEquipment>()
            .Property(x => x.Volume)
            .HasPrecision(18, 3);

        modelBuilder.Entity<StaticEquipment>().HasIndex(x => x.MechanicalEquipmentId).IsUnique();



        // ==============================
        // Piping
        // ==============================


        modelBuilder.Entity<PipingLine>()
            .Property(x => x.DesignPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<PipingLine>()
            .Property(x => x.DesignTemperature)
            .HasPrecision(18, 3);



        modelBuilder.Entity<PipeSpecification>()
            .Property(x => x.DesignPressure)
            .HasPrecision(18, 3);


        modelBuilder.Entity<PipeSpecification>()
            .Property(x => x.DesignTemperature)
            .HasPrecision(18, 3);


        // ==============================
        // Seed Master Data
        // ==============================


        // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» Appendix 1: کد داخلی ۶ دیسیپلین اولیه
        // (PROC/MECH/ELEC/INST/PIPE/CIVL) به کد رسمی ۲-حرفی سند تغییر کرد (کد قدیمی در
        // ImportAliases نگه‌داری شده تا Import فایل‌های قدیمی نشکند - DisciplineId ثابت مانده،
        // پس هیچ FK موجودی (EngineeringDocument/MaterialRequisition و ...) بی‌اعتبار نمی‌شود).
        // ۱۷ ردیف جدید برای تکمیل ۲۳ کد رسمی Appendix 1 اضافه شده. TransmittalCode (کد بخش ۳
        // سند - X در TV-X-UU-AAAA-NNNN) طبق نگاشت تأییدشده‌ی کاربر پر شده؛ NULL یعنی سند برای
        // آن دیسیپلین نگاشت رسمی مشخصی ندارد.
        modelBuilder.Entity<Discipline>().HasData(

            new Discipline
            {
                DisciplineId = 1,
                Code = "PR",
                Name = "Process",
                Description = "Process Engineering",
                ImportAliases = "PROC,PRO,PROCESS,PROCS",
                TransmittalCode = "J",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 2,
                Code = "ME",
                Name = "Mechanical (Static)",
                Description = "Mechanical Engineering - Static Equipment",
                ImportAliases = "MECH,MEC,MECHANICAL,STATIC",
                TransmittalCode = "C",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 3,
                Code = "EL",
                Name = "Electrical",
                Description = "Electrical Engineering",
                ImportAliases = "ELEC,ELE,ELECTRICAL,PWR,POWER",
                TransmittalCode = "N",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 4,
                Code = "IN",
                Name = "Instrumentation & Control",
                Description = "Instrumentation and Control Engineering",
                ImportAliases = "INST,INS,IC,I&C,INSTRUMENT,CONTROL,INSTRUMENTATION",
                TransmittalCode = "K",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 5,
                Code = "PI",
                Name = "Piping",
                Description = "Piping Engineering",
                ImportAliases = "PIPE,PIP,PIPING,PIPELINE",
                TransmittalCode = "X",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 6,
                Code = "CV",
                Name = "Civil",
                Description = "Civil Engineering",
                ImportAliases = "CIVL,CI,CIV,CIVIL,CS",
                TransmittalCode = "A",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 7,
                Code = "PM",
                Name = "Mechanical (Rotary)",
                Description = "Mechanical Engineering - Rotary/Process Machinery & Packages",
                ImportAliases = "ROTATING,ROTARY,MACHINERY,PACKAGE",
                TransmittalCode = "G",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 8,
                Code = "TC",
                Name = "Telecommunication",
                Description = "Telecommunication Engineering",
                ImportAliases = "TELECOM,TELECOMMUNICATION",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 9,
                Code = "CP",
                Name = "Cathodic Protection",
                Description = "Cathodic Protection Engineering",
                ImportAliases = "CATHODIC",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 10,
                Code = "AR",
                Name = "Architectural",
                Description = "Architectural Engineering",
                ImportAliases = "ARCH,ARCHITECTURE",
                TransmittalCode = "A",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 11,
                Code = "ST",
                Name = "Structural",
                Description = "Structural Engineering",
                ImportAliases = "STR,STRUCTURAL",
                TransmittalCode = "A",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 12,
                Code = "HS",
                Name = "HSE",
                Description = "Health, Safety & Environment",
                ImportAliases = "HSE,SAFETY,HEALTH",
                TransmittalCode = "H",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 13,
                Code = "HV",
                Name = "HVAC",
                Description = "Heating, Ventilation & Air Conditioning",
                ImportAliases = "HVAC",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 14,
                Code = "CN",
                Name = "Construction",
                Description = "Construction Management",
                ImportAliases = "CONSTRUCTION",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 15,
                Code = "PQ",
                Name = "Procurement",
                Description = "Procurement",
                ImportAliases = "PROCUREMENT,PURCHASING",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 16,
                Code = "CC",
                Name = "Cost Control",
                Description = "Cost Control",
                ImportAliases = "COST",
                TransmittalCode = "M",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 17,
                Code = "PL",
                Name = "Project Planning & Control",
                Description = "Project Planning and Control",
                ImportAliases = "PLANNING",
                TransmittalCode = "M",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 18,
                Code = "PP",
                Name = "Project Management",
                Description = "Project Management",
                ImportAliases = "PROJECTMGMT,PROJMGMT",
                TransmittalCode = "M",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 19,
                Code = "IT",
                Name = "Information Technology",
                Description = "Information Technology",
                ImportAliases = "INFOTECH",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 20,
                Code = "LB",
                Name = "Laboratory",
                Description = "Laboratory",
                ImportAliases = "LAB,LABORATORY",
                TransmittalCode = "W",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 21,
                Code = "FN",
                Name = "Financing",
                Description = "Financing",
                ImportAliases = "FINANCE,FINANCING",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 22,
                Code = "QA",
                Name = "Quality Assurance",
                Description = "Quality Assurance",
                ImportAliases = "QUALITY",
                TransmittalCode = null,
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            },


            new Discipline
            {
                DisciplineId = 23,
                Code = "IP",
                Name = "Insulation/Painting",
                Description = "Insulation and Painting",
                ImportAliases = "INSULATION,PAINTING",
                TransmittalCode = "X",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            }

        );


        // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» Appendix 2: ۲۹ کد رسمی نوع مدرک -
        // برای اعتبارسنجی DocumentNumberingService (رفع باگ «۳ حرف اول رشتهٔ آزاد») استفاده
        // می‌شود؛ Document.DocumentType خودش عمداً رشتهٔ آزاد باقی مانده (خارج از دامنهٔ این فاز).
        modelBuilder.Entity<DocumentTypeCode>().HasData(
            new DocumentTypeCode { DocumentTypeCodeId = 1, Code = "MRQ", Name = "Material Requisition", ImportAliases = "MATERIAL REQUISITION", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 2, Code = "SPC", Name = "Material / Equipment Specification", ImportAliases = "SPECIFICATION,SPEC", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 3, Code = "POR", Name = "Purchase Order", ImportAliases = "PURCHASE ORDER,PO", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 4, Code = "JSS", Name = "Job Specification for Supply", ImportAliases = null, IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 5, Code = "JSD", Name = "Job Specification for Design", ImportAliases = null, IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 6, Code = "JSC", Name = "Job / Material Specification for Construction", ImportAliases = null, IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 7, Code = "MTO", Name = "Material Take-off", ImportAliases = "MATERIAL TAKE-OFF,MATERIAL TAKEOFF", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 8, Code = "LST", Name = "List / Schedule", ImportAliases = "LIST,SCHEDULE", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 9, Code = "PFD", Name = "PFD, UFD", ImportAliases = "UFD,PROCESS FLOW DIAGRAM", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 10, Code = "PID", Name = "P&ID, Utility & Instrument Diagram", ImportAliases = "P&ID,PIPING AND INSTRUMENTATION DIAGRAM", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 11, Code = "DWG", Name = "Drawing", ImportAliases = "DRAWING", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 12, Code = "DIA", Name = "Diagram", ImportAliases = "DIAGRAM", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 13, Code = "CLN", Name = "Calculation Note", ImportAliases = "CALCULATION NOTE,CALCULATION", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 14, Code = "STD", Name = "Standard Drawing / Design", ImportAliases = "STANDARD", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 15, Code = "PRC", Name = "Procedure", ImportAliases = "PROCEDURE", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 16, Code = "PLN", Name = "Planning", ImportAliases = "PLAN", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 17, Code = "CHO", Name = "Change Order", ImportAliases = "CHANGE ORDER", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 18, Code = "DSH", Name = "Data Sheet", ImportAliases = "DATASHEET,DATA SHEET", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 19, Code = "RPT", Name = "Report", ImportAliases = "REPORT", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 20, Code = "CNT", Name = "Contract", ImportAliases = "CONTRACT", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 21, Code = "ITP", Name = "Inspection & Test Plan", ImportAliases = "INSPECTION AND TEST PLAN,INSPECTION & TEST PLAN", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 22, Code = "IRC", Name = "Inspection and Release Certificate", ImportAliases = "INSPECTION RELEASE CERTIFICATE", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 23, Code = "RN", Name = "Release Note", ImportAliases = "RELEASE NOTE", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 24, Code = "TBA", Name = "Technical Bid Analysis", ImportAliases = null, IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 25, Code = "CBA", Name = "Commercial Bid Analysis", ImportAliases = null, IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 26, Code = "TN", Name = "Transmittal Note", ImportAliases = "TRANSMITTAL NOTE,TRANSMITTAL", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 27, Code = "NCR", Name = "Non Conformance Report", ImportAliases = "NON CONFORMANCE REPORT,NONCONFORMANCE", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 28, Code = "MNL", Name = "Manual", ImportAliases = "MANUAL", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new DocumentTypeCode { DocumentTypeCodeId = 29, Code = "INX", Name = "Index", ImportAliases = "INDEX", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) }
        );


        // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» Appendix 3: ۷۹ کد رسمی تایپولوژی
        // مدرک فروشنده - برای شماره‌گذاری فرمت SUB-CONTRACTOR/Vendor (3863-XX-VD-TT-...) استفاده می‌شود.
        modelBuilder.Entity<VendorDocumentTypologyCode>().HasData(
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 1, Code = "PL", Category = "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", Description = "Document List; Document Issuing Plan", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 2, Code = "PP", Category = "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", Description = "Manufacturing schedule; Fabrication Plan; Progress Reports", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 3, Code = "PS", Category = "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", Description = "List of Subvendors/Subcontractors; List of Suborders", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 4, Code = "PQ", Category = "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", Description = "Quality Assurance Documents (Quality Plan, Site Quality Plan, Organization Chart, Audit plans)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 5, Code = "PH", Category = "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", Description = "HSE Assurance Documents (HSE Plan, Site HSE Plan)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 6, Code = "QC", Category = "Quality Control / Inspection & Testing", Description = "Inspection & Test Plan (Quality Control Plan)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 7, Code = "QS", Category = "Quality Control / Inspection & Testing", Description = "Site (Construction) Inspection & Test Plan (relevant to Site assembled/tested equipment)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 8, Code = "QP", Category = "Quality Control / Inspection & Testing", Description = "Inspection and Testing procedures (functional, performance, material, NDE, pressure (hydraulic) tests)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 9, Code = "QT", Category = "Quality Control / Inspection & Testing", Description = "Factory Acceptance Test Procedures, Site Acceptance Test Procedures", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 10, Code = "FW", Category = "Fabrication & Welding", Description = "Welding Books: Welding Process Specifications (WPS), Welding Process Qualifications (PQR), Welding maps; Welding Repair Procedures; Heat Treatment Procedures", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 11, Code = "FO", Category = "Fabrication & Welding", Description = "Description of the fabrication cycle, other Manufacturing Procedures, Cladding procedure", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 12, Code = "FP", Category = "Fabrication & Welding", Description = "Internal Lining, Coating and Painting Procedures", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 13, Code = "BC", Category = "Basic and Process Data / Performance Design Data", Description = "Heat/Material Balance; Thermal Calculations; Utilities calculation/consumption; Effluent/Waste/Emission List; Functional sizing calculations; Process Calculations, Performance Curves; Reliability & Availability Analysis", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 14, Code = "BO", Category = "Basic and Process Data / Performance Design Data", Description = "Description of working: Interlock, Control strategies and Sequences narratives; Cause/effect diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 15, Code = "BD", Category = "Basic and Process Data / Performance Design Data", Description = "Process Flow Diagrams; Piping & Instrument Diagrams; Machinery Circuits diagrams (lubrication / hydraulic / pneumatic)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 16, Code = "GE", Category = "General Configuration - Equipment List/Data Sheets", Description = "Equipment List; Auxiliary and other Components lists", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 17, Code = "GS", Category = "General Configuration - Equipment List/Data Sheets", Description = "Equipment Data Sheets/Specifications", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 18, Code = "GH", Category = "General Configuration - Equipment List/Data Sheets", Description = "Fire Fighting Equipment Data Sheets and Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 19, Code = "GP", Category = "General Configuration - Equipment List/Data Sheets", Description = "Spare Parts Lists, SPIR forms, Spare Parts Drawings (precomm / comm / capital / operation spare parts)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 20, Code = "GL", Category = "General Configuration - Equipment List/Data Sheets", Description = "Lubricants Lists and Quantities (Schedules), Lubricant Specifications", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 21, Code = "GT", Category = "General Configuration - Equipment List/Data Sheets", Description = "List of special tools (erection, testing, operation)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 22, Code = "LG", Category = "Layouts / General Arrangements / Foundation Data", Description = "Equipment layout, General Arrangements (with data for foundations); Plot plans, Layouts and requirements for UG services (sewers, piping / cable routing)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 23, Code = "LF", Category = "Layouts / General Arrangements / Foundation Data", Description = "Foundations (or Process concrete structures) / Supports Calculations & Design", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 24, Code = "HG", Category = "HSE Design", Description = "Material Safety Data Sheets", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 25, Code = "HN", Category = "HSE Design", Description = "Noise Source Data Sheets", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 26, Code = "HF", Category = "HSE Design", Description = "Sources or Areas of Release Data Sheets, Hazardous Areas Classification Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 27, Code = "SC", Category = "Mechanical Design (Static Components)", Description = "Mechanical Calculations (Design/Code)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 28, Code = "SD", Category = "Mechanical Design (Static Components)", Description = "Vessels / Tanks / other Pressure Equipment Drawings - Nozzle Orientation", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 29, Code = "SS", Category = "Mechanical Design (Static Components)", Description = "Auxiliary Steel Structures Specifications & Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 30, Code = "RC", Category = "Mechanical Design (Rotating Components)", Description = "Mechanical Calculations (rotating elements, vibrations, pulsation, etc)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 31, Code = "RD", Category = "Mechanical Design (Rotating Components)", Description = "Machinery / Rotating Equipment Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 32, Code = "RM", Category = "Mechanical Design (Rotating Components)", Description = "Other Mechanical Equipment Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 33, Code = "PC", Category = "Piping Design", Description = "Piping Calculations (design, stress)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 34, Code = "PD", Category = "Piping Design", Description = "Piping Arrangement/Layouts, Piping Isometrics, Tie-in drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 35, Code = "PG", Category = "Piping Design", Description = "Piping Components General Specifications / Classes; Supports Specifications & Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 36, Code = "IB", Category = "Instrumentation / Automation & Process Control Design", Description = "Block Diagrams, Architectural Diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 37, Code = "IL", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrument list, I/O lists, Alarm and trip lists, Memory Maps for Serial Communications, Point Configuration Database", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 38, Code = "IS", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrument Data Sheets & Specifications", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 39, Code = "IV", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrumental component drawings (instrumentation, valves, etc)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 40, Code = "IC", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrumental Calculations (instrument sizing, SIL, etc)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 41, Code = "IT", Category = "Instrumentation / Automation & Process Control Design", Description = "Typical installation drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 42, Code = "IW", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrumental wiring diagrams: Interconnection diagrams, DCS/PLC interfacing diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 43, Code = "IG", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrument layouts, Cable run layouts", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 44, Code = "IK", Category = "Instrumentation / Automation & Process Control Design", Description = "List of cables (Instrumental)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 45, Code = "ID", Category = "Instrumentation / Automation & Process Control Design", Description = "Logic Diagrams, Loop Diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 46, Code = "IP", Category = "Instrumentation / Automation & Process Control Design", Description = "Instrumentation / Instrumental Panels Construction Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 47, Code = "IM", Category = "Instrumentation / Automation & Process Control Design", Description = "Man Machine Interface, Graphic Pages, Synoptic and Operating Panels layouts", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 48, Code = "EB", Category = "Electrical Design", Description = "Block Diagrams, Architectural Diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 49, Code = "EU", Category = "Electrical Design", Description = "List of Electric Users / Electrical Consumptions", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 50, Code = "EC", Category = "Electrical Design", Description = "Electrical Calculations; Electrical Curves", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 51, Code = "ED", Category = "Electrical Design", Description = "Electric operating diagrams, one-line and interconnection diagrams, typical wiring diagrams, electric tracing diagrams, grounding diagrams", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 52, Code = "ES", Category = "Electrical Design", Description = "Motors, Switchgears, Variable Speed Drivers, Distribution Boards, other Electrical Equipment Data Sheets / Specifications", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 53, Code = "EE", Category = "Electrical Design", Description = "Motors, Switchgears, Variable Speed Drivers, Distribution Boards, other Electrical Equipment Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 54, Code = "EG", Category = "Electrical Design", Description = "Electrical cable run layouts, Electrical equipment layout", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 55, Code = "EL", Category = "Electrical Design", Description = "List of cables (Electrical)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 56, Code = "VH", Category = "HVAC Design", Description = "Heat Dissipation Calculations", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 57, Code = "VC", Category = "HVAC Design", Description = "HVAC Design Calculation Reports / Utility Consumption Analyses", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 58, Code = "VG", Category = "HVAC Design", Description = "HVAC General Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 59, Code = "VL", Category = "HVAC Design", Description = "HVAC System Layouts/Sections (Mechanical Part)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 60, Code = "VE", Category = "HVAC Design", Description = "HVAC System Layouts/Sections (Electrical/Automatic Control Parts)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 61, Code = "VT", Category = "HVAC Design", Description = "HVAC System Material Take-Offs", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 62, Code = "AD", Category = "Civil Design / Steel Structures Design", Description = "Building / Architectural Design (specifications and drawings)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 63, Code = "AC", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Calculation reports", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 64, Code = "AE", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Assembly Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 65, Code = "AS", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Shop Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 66, Code = "AJ", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Joint Detail Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 67, Code = "AM", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Mark Lists", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 68, Code = "AA", Category = "Civil Design / Steel Structures Design", Description = "Steel Structures Accounting", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 69, Code = "DT", Category = "Transportation, Storage, Preservation & Site Installation", Description = "Shipping and Handling Instructions / Transportation drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 70, Code = "DS", Category = "Transportation, Storage, Preservation & Site Installation", Description = "Storage and Preservation Instructions / Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 71, Code = "DI", Category = "Transportation, Storage, Preservation & Site Installation", Description = "Installation, Field Assembling/Fabrication, Precommissioning Instructions/Drawings/List of Materials", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 72, Code = "DB", Category = "Design Final Documentation", Description = "Manufacturer Data Book - or any subtype of", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 73, Code = "DL", Category = "Design Final Documentation", Description = "Manufacturer Data Book Index of Contents", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 74, Code = "OI", Category = "Operation and Maintenance", Description = "Operating Instructions (Start-up and Operation)", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 75, Code = "OP", Category = "Operation and Maintenance", Description = "Maintenance Instructions / Procedures / Detailed Drawings", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 76, Code = "OM", Category = "Operation and Maintenance", Description = "Installation, Operation and Maintenance Manual - or any subtype of", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 77, Code = "OL", Category = "Operation and Maintenance", Description = "Installation, Operation and Maintenance Manual Index of Contents", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },

            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 78, Code = "MB", Category = "Certification of Manufacturing/Erection", Description = "Certification Dossier", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new VendorDocumentTypologyCode { VendorDocumentTypologyCodeId = 79, Code = "ML", Category = "Certification of Manufacturing/Erection", Description = "Certification Dossier Index of Contents", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) }
        );



        modelBuilder.Entity<Project>().HasData(

            new Project
            {
                ProjectId = 1,
                ProjectCode = "PEMS-001",
                ProjectName = "Polymer Plant Development",
                ClientName = "Petrochemical",
                Location = "Assaluyeh",
                Status = "Active",
                IsActive = true,
                CreatedDate = new DateTime(2026, 1, 1)
            }

        );

    }

}