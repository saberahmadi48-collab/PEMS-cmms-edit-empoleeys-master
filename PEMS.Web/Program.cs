using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using PEMS.Application.Interfaces;
using PEMS.Application.Services;

using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Web.Routing;
using PEMS.Persistence.Repositories;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;

using PEMS.Application.AI.Interfaces;
using PEMS.Application.AI.OCR;
using PEMS.Application.AI.Parser;
using PEMS.Application.AI.Search.Interfaces;
using PEMS.Application.AI.Search.Services;
using PEMS.Application.AI.Services;

using PEMS.AI.Interfaces;
using PEMS.AI.Services;

using PEMS.Web.Security;
using PEMS.Infrastructure.Cache;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

using Hangfire;
using PEMS.Web.Licensing;
using Hangfire.Dashboard;
using Serilog;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;


// Structured Logging (طبق تکنولوژی‌های تعیین‌شده در Bible - Volume II): جایگزین Logging پیش‌فرض
// برای عیب‌یابی بهتر در Production - خروجی هم روی Console هم فایل روزانه
// All settings come from env/<name>.env + env/<name>.secrets.env (PEMS_ENV; see env/README.md), loaded before anything
// reads configuration so logging itself is configurable per environment.
var builder = WebApplication.CreateBuilder(args);

var envFiles = PEMS.Web.Configuration.PemsConfigurationExtensions.AddPemsEnvFiles(
    builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(
        PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Serilog:MinimumLevel"), ignoreCase: true))
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Serilog:File:Path"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Serilog:File:RetainedFiles"))
    .CreateLogger();

builder.Host.UseSerilog();

if (envFiles is not null)
    Log.Information("Settings loaded for environment '{Env}' from {Files}{Secrets}",
        envFiles.EnvName, string.Join(", ", envFiles.LoadedFiles.Select(Path.GetFileName)),
        envFiles.SecretsFileFound ? "" : " (no secrets file: secrets must come from environment variables)");



// =========================
// MVC
// =========================

// =========================
// Bilingual Platform (English / Persian با RTL خودکار)
// =========================
// طبق ممیزی: چون SharedResource.cs خودش داخل پوشه‌ی Resources قرار دارد، مقداردهی
// ResourcesPath="Resources" باعث می‌شد نام کامل منبع مورد انتظار دوبار "Resources" را
// تکرار کند (PEMS.Web.Resources.Resources.SharedResource) که هرگز با نام واقعی resx
// جفت نمی‌شد - یعنی IStringLocalizer همیشه به‌جای ترجمه، خودِ کلید را برمی‌گرداند، چه
// برای انگلیسی چه فارسی. چون namespace کلاس از قبل شامل "Resources" است، اینجا باید خالی
// بماند.
builder.Services.AddLocalization(options => options.ResourcesPath = "");

// طبق درخواست: اعلان‌های لحظه‌ای بدون Refresh
builder.Services.AddSignalR();

builder.Services.AddScoped<PEMS.Application.Interfaces.INotificationBroadcaster, PEMS.Web.Hubs.SignalRNotificationBroadcaster>();

// Code-first install/update of the database (startup bootstrap + Admin > Database Update page)
var databaseMigrationTimeoutMinutes = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Database:MigrationCommandTimeoutMinutes");
// SCRUM-280: rollback point of an update. BackupDirectory may be empty (= SQL Server default backup folder), so it is not Required.
var databaseBackupBeforeUpdate = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Database:BackupBeforeUpdate");
var databaseBackupDirectory = builder.Configuration["Database:BackupDirectory"];
builder.Services.AddScoped<PEMS.Application.Interfaces.IDatabaseUpdateService>(sp => new PEMS.Persistence.Services.DatabaseUpdateService(
    sp.GetRequiredService<PEMS.Persistence.Context.PEMSDbContext>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PEMS.Persistence.Services.DatabaseUpdateService>>(),
    databaseMigrationTimeoutMinutes,
    databaseBackupBeforeUpdate,
    databaseBackupDirectory));

// Licensing (docs/design/LICENSING_TECHNICAL_DESIGN.md). Licensing:Enforce=false in every env file until the rollout tasks: nothing is checked or blocked.
builder.Services.AddPemsLicensing(builder.Configuration);
PEMS.UI.ServiceCollectionExtensions.AddPemsUi(builder.Services);   // component registry + request-free renderer (SCRUM-29)

// REST API /api/v1 (SCRUM-435, ADR-0009): permission policies, problem+json, OpenAPI 3.1, API-aware 401/403 for cookie sign-in.
PEMS.Web.Api.ApiServiceCollectionExtensions.AddPemsApi(builder.Services);
PEMS.Web.Api.ApiServiceCollectionExtensions.ConfigureApiAuthenticationResponses(builder.Services);
builder.Services.AddExceptionHandler<PEMS.Web.Diagnostics.PemsExceptionHandler>();   // SCRUM-464: /api failures outside controllers, same problem+json
builder.Services.AddSingleton<PEMS.Web.Api.ApiIdempotencyStore>();

// Definition store (SCRUM-434, ADR-0010/0011): write service, cached runtime read side, the registered kinds and the limits (Definitions:* settings).
builder.Services.AddSingleton(builder.Configuration.GetSection("Definitions").Get<PEMS.Application.Definitions.DefinitionLimits>() ?? new PEMS.Application.Definitions.DefinitionLimits());
builder.Services.AddSingleton(builder.Configuration.GetSection("Validation").Get<PEMS.Application.Validation.ValidationOptions>() ?? new PEMS.Application.Validation.ValidationOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Definitions:Budgets").Get<PEMS.Application.Definitions.PipelineBudgets>() ?? new PEMS.Application.Definitions.PipelineBudgets());
// SCRUM-576 and 574: compiled render plans and hydrated results per permission set
builder.Services.AddSingleton<PEMS.Application.Definitions.RenderPlanCache>();
builder.Services.AddSingleton<PEMS.Application.Definitions.HydrationCache>();
PEMS.Web.Definitions.DefinitionKindExtensions.AddPemsDefinitionKinds(builder.Services);
builder.Services.AddScoped<PEMS.Persistence.Services.DefinitionStore>();
// SCRUM-572 (ADR-0014, amended): the definitions the runtime reads are the JSON files of the data layer, hot-reloaded; nothing is read from the database until Sprint 3 moves them (no overlap)
builder.Services.AddSingleton(builder.Configuration.GetSection("Definitions:Files").Get<PEMS.Application.Definitions.DefinitionFileOptions>() ?? new PEMS.Application.Definitions.DefinitionFileOptions());
builder.Services.AddSingleton<PEMS.Persistence.Definitions.FileDefinitionStore>();
builder.Services.AddSingleton<PEMS.Application.Definitions.IDefinitionFiles>(sp => sp.GetRequiredService<PEMS.Persistence.Definitions.FileDefinitionStore>());
builder.Services.AddSingleton(new PEMS.Application.Definitions.DefinitionOwnership(builder.Configuration.GetSection("Definitions:Ownership").Get<Dictionary<string, string>>()));
// one owner per kind: the routed store serves each kind from its owner (files, database) and nothing for the others
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionStore>(sp => new PEMS.Application.Definitions.RoutedDefinitionStore(
    sp.GetRequiredService<PEMS.Application.Definitions.DefinitionOwnership>(),
    new Dictionary<PEMS.Application.Definitions.DefinitionOwner, PEMS.Application.Definitions.IDefinitionStore>
    {
        [PEMS.Application.Definitions.DefinitionOwner.Files] = sp.GetRequiredService<PEMS.Persistence.Definitions.FileDefinitionStore>(),
        [PEMS.Application.Definitions.DefinitionOwner.Database] = sp.GetRequiredService<PEMS.Persistence.Services.DefinitionStore>()
    }));
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionGrantStore, PEMS.Persistence.Definitions.EfDefinitionGrantStore>();   // SCRUM-591
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionAccess, PEMS.Persistence.Definitions.PermissionDefinitionAccess>();
builder.Services.AddScoped<PEMS.Application.Definitions.DefinitionPipeline>();
builder.Services.AddScoped<PEMS.Web.Definitions.PageRenderer>();
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionHydrator, PEMS.Web.Definitions.FormOptionsHydrator>();   // SCRUM-437: options of a form field from its data source
builder.Services.AddScoped<PEMS.Web.Definitions.ActionExecutor>();
// SCRUM-437/578 pilot: the registered providers and handlers a definition may name
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.WorkflowExecuteAction>();
// SCRUM-593 to 595: ordered steps in one transaction, result binding and the security-key step-up as settings of an action definition
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.StepsAction>();
// SCRUM-596 and 615: a file that comes in (file.upload) and a file that goes out (file.export) are settings of an action definition
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.FileUploadAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.FileDownloadAction>();   // SCRUM-615: a stored file by key, inside the folder the definition names
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.FileExportAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.DefinitionExportAction>();   // SCRUM-784
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.DefinitionImportAction>();   // SCRUM-784
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.GridViewSaveAction>();   // SCRUM-793
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.GridViewDeleteAction>();   // SCRUM-793
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionTransaction, PEMS.Web.Definitions.EfActionTransaction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IStepUpVerifier, PEMS.Web.Definitions.WebAuthnStepUpVerifier>();
// the entity catalog: the master-data entities a definition may list and create, with the permissions that guard them (the code checks them; a definition cannot widen access)
builder.Services.AddSingleton(PEMS.Web.Definitions.EntityCatalog.Default());
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.EntityGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.EntityAggregateProvider>();   // SCRUM-592: group by with count, sum, avg, min, max
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.ComponentCatalogGridProvider>();   // SCRUM-340: the component catalog as rows
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionsGridProvider>();   // SCRUM-449: the definition workbench as rows
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionKindsGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionUsagesGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionLargestGridProvider>();   // SCRUM-455
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionGrowthGridProvider>();   // SCRUM-786
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionVersionsGridProvider>();   // SCRUM-784, 789
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.GridViewsProvider>();   // SCRUM-793
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.OperationArgumentsGridProvider>();   // SCRUM-811
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.MenuTreeGridProvider>();   // SCRUM-813
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionSodGridProvider>();   // SCRUM-813
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionDiffGridProvider>();   // SCRUM-784
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.OwnershipPreflightGridProvider>();   // SCRUM-797
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionHealthGridProvider>();   // SCRUM-786
// SCRUM-786 (fail closed): the only supported retention of definition versions is forever; any other value would promise a policy that is not active
if (builder.Configuration["Definitions:RetainRetiredVersions"] is { Length: > 0 } retainSetting && !string.Equals(retainSetting, "forever", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Definitions:RetainRetiredVersions must be 'forever' (got '{retainSetting}'): retired definition versions are never deleted.");
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionTranslationsGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.DefinitionPayloadGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider>(sp => new PEMS.Web.Definitions.OperationCatalogGridProvider(sp.GetRequiredService<PEMS.Web.Definitions.OperationCatalog>()));   // SCRUM-606   // SCRUM-439, 604, 606, 607, 608   // SCRUM-419, 609
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionImpact, PEMS.Persistence.Services.DefinitionImpactService>();   // SCRUM-450, 457, 459
builder.Services.AddScoped<PEMS.Persistence.Services.DefinitionPublishGuard>();   // SCRUM-785, 788, 789: precheck, acknowledgement, smoke check and automatic rollback of the REST publish
builder.Services.AddScoped<PEMS.Application.Interfaces.IDefinitionWorkbench, PEMS.Persistence.Services.DefinitionWorkbench>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IDefinitionDesigner, PEMS.Persistence.Services.DefinitionDesigner>();   // SCRUM-439, 604, 606, 607, 608
builder.Services.AddScoped<PEMS.Application.Interfaces.IMigratorCenter, PEMS.Web.Services.MigratorCenter>();   // SCRUM-582
builder.Services.AddSingleton<PEMS.Web.Definitions.PackageFeedApplier>();   // SCRUM-807: the apply step of a feed, through the entity catalog
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.MigratorHistoryGridProvider>();   // SCRUM-814
builder.Services.AddScoped<PEMS.Application.Interfaces.IPackageBuilder, PEMS.Web.Services.PackageBuilder>();   // SCRUM-584 (signing key only on the vendor deployment)
builder.Services.AddSingleton<PEMS.Web.Definitions.IDataSourceProvider, PEMS.Web.Definitions.EntityOptionsProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.EntityCreateAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.EntityImportAction>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IAiInsightsOperations, PEMS.Web.Services.AiInsightsOperations>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IMyWorkOperations, PEMS.Web.Services.MyWorkOperations>();
builder.Services.AddScoped<PEMS.Application.Interfaces.ICmmsOperationsReads, PEMS.Web.Services.CmmsOperationsReads>();
builder.Services.AddScoped<PEMS.Application.Interfaces.ICmmsReadinessReads, PEMS.Web.Services.CmmsReadinessReads>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IGlobalSearchOperations, PEMS.Web.Services.GlobalSearchOperations>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IAiSearchOperations, PEMS.Web.Services.AiSearchOperations>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IReportReads, PEMS.Web.Services.ReportReads>();   // SCRUM-747
builder.Services.AddScoped<PEMS.Application.Interfaces.IKpiOperations, PEMS.Web.Services.KpiOperations>();   // SCRUM-713
builder.Services.AddScoped<PEMS.Application.Interfaces.INotificationOperations, PEMS.Web.Services.NotificationOperations>();   // SCRUM-731
builder.Services.AddScoped<PEMS.Application.Interfaces.IImportCenterOperations, PEMS.Web.Services.ImportCenterOperations>();   // SCRUM-706
builder.Services.AddScoped<PEMS.Application.Interfaces.IDashboardSettingsOperations, PEMS.Web.Services.DashboardSettingsOperations>();   // SCRUM-680
builder.Services.AddScoped<PEMS.Application.Interfaces.IChatAssistantOperations, PEMS.Web.Services.ChatAssistantOperations>();   // SCRUM-672
builder.Services.AddScoped<PEMS.Application.Interfaces.IQuickSetupOperations, PEMS.Web.Services.QuickSetupOperations>();   // SCRUM-743
builder.Services.AddScoped<PEMS.Application.Interfaces.IEngineeringDashboardOperations, PEMS.Web.Services.EngineeringDashboardOperations>();   // SCRUM-688
builder.Services.AddScoped<PEMS.Application.Interfaces.IHelpOperations, PEMS.Web.Services.HelpOperations>();   // SCRUM-704
builder.Services.AddScoped<PEMS.Application.Interfaces.IDisciplineWorkspaceOperations, PEMS.Web.Services.DisciplineWorkspaceOperations>();   // SCRUM-682
builder.Services.AddScoped<PEMS.Application.Interfaces.IAiExtractionOperations, PEMS.Web.Services.AiExtractionOperations>();   // SCRUM-667
builder.Services.AddScoped<PEMS.Application.Interfaces.IHomeOperations, PEMS.Web.Services.HomeOperations>();   // SCRUM-705
builder.Services.AddScoped<PEMS.Application.Interfaces.IEmployeeOperations, PEMS.Web.Services.EmployeeOperations>();   // SCRUM-687
builder.Services.AddScoped<PEMS.Application.Interfaces.ITeamOperations, PEMS.Web.Services.TeamOperations>();   // SCRUM-757
builder.Services.AddScoped<PEMS.Application.Interfaces.IInventoryOperations, PEMS.Web.Services.InventoryOperations>();   // SCRUM-652, 654
builder.Services.AddScoped<PEMS.Application.Interfaces.IWarehouseIntakeOperations, PEMS.Web.Services.WarehouseIntakeOperations>();   // SCRUM-665
builder.Services.AddScoped<PEMS.Application.Interfaces.IMaterialItemOperations, PEMS.Web.Services.MaterialItemOperations>();   // SCRUM-656
builder.Services.AddScoped<PEMS.Application.Interfaces.IReplenishmentOperations, PEMS.Web.Services.ReplenishmentOperations>();   // SCRUM-663
builder.Services.AddScoped<PEMS.Application.Interfaces.IPreProcurementOperations, PEMS.Web.Services.PreProcurementOperations>();   // SCRUM-735
builder.Services.AddScoped<PEMS.Application.Interfaces.IStoreRequisitionOperations, PEMS.Web.Services.StoreRequisitionOperations>();   // SCRUM-664
builder.Services.AddScoped<PEMS.Application.Interfaces.IVendorOperations, PEMS.Web.Services.VendorOperations>();   // SCRUM-763
builder.Services.AddScoped<PEMS.Application.Interfaces.IIntakeOperations, PEMS.Web.Services.IntakeOperations>();   // SCRUM-709
builder.Services.AddScoped<PEMS.Application.Interfaces.IMaintenanceMasterDataOperations, PEMS.Web.Services.MaintenanceMasterDataOperations>();   // SCRUM-723
builder.Services.AddScoped<PEMS.Application.Interfaces.IPermitOperations, PEMS.Web.Services.PermitOperations>();   // SCRUM-734
builder.Services.AddScoped<PEMS.Application.Interfaces.IMaintenanceStrategyOperations, PEMS.Web.Services.MaintenanceStrategyOperations>();   // SCRUM-725
builder.Services.AddScoped<PEMS.Application.Interfaces.IWorkOrderTaskOperations, PEMS.Web.Services.WorkOrderTaskOperations>();   // SCRUM-764
builder.Services.AddScoped<PEMS.Application.Interfaces.ITaskItemOperations, PEMS.Web.Services.TaskItemOperations>();   // SCRUM-755
builder.Services.AddScoped<PEMS.Application.Interfaces.IWorkRequestOperations, PEMS.Web.Services.WorkRequestOperations>();   // SCRUM-765
builder.Services.AddScoped<PEMS.Application.Interfaces.ITenderOperations, PEMS.Web.Services.TenderOperations>();   // SCRUM-759
builder.Services.AddScoped<PEMS.Application.Interfaces.IContractOperations, PEMS.Web.Services.ContractOperations>();   // SCRUM-676
builder.Services.AddScoped<PEMS.Application.Interfaces.IMaintenancePlanOperations, PEMS.Web.Services.MaintenancePlanOperations>();   // SCRUM-724
builder.Services.AddScoped<PEMS.Application.Interfaces.IJobPlanOperations, PEMS.Web.Services.JobPlanOperations>();   // SCRUM-711
builder.Services.AddScoped<PEMS.Application.Interfaces.IShiftHandoverOperations, PEMS.Web.Services.ShiftHandoverOperations>();   // SCRUM-749
builder.Services.AddScoped<PEMS.Application.Interfaces.IWorkImportOperations, PEMS.Web.Services.WorkImportOperations>();   // SCRUM-675, 722
builder.Services.AddScoped<PEMS.Application.Interfaces.IMocOperations, PEMS.Web.Services.MocOperations>();   // SCRUM-720
builder.Services.AddScoped<PEMS.Application.Interfaces.ITechnicalQueryOperations, PEMS.Web.Services.TechnicalQueryOperations>();   // SCRUM-758
builder.Services.AddScoped<PEMS.Application.Interfaces.ICrewOperations, PEMS.Web.Services.CrewOperations>();   // SCRUM-677
builder.Services.AddScoped<PEMS.Application.Interfaces.IFindingOperations, PEMS.Web.Services.FindingOperations>();   // SCRUM-699
builder.Services.AddScoped<PEMS.Application.Interfaces.IProductionOperations, PEMS.Web.Services.ProductionOperations>();   // SCRUM-737, 738, 739
builder.Services.AddScoped<PEMS.Application.Interfaces.IMeetingInspectionOperations, PEMS.Web.Services.MeetingInspectionOperations>();   // SCRUM-707, 728
builder.Services.AddScoped<PEMS.Application.Interfaces.IPermitSafetyOperations, PEMS.Web.Services.PermitSafetyOperations>();   // SCRUM-696, 702, 710
builder.Services.AddScoped<PEMS.Application.Interfaces.IPartCatalogueRequestOperations, PEMS.Web.Services.PartCatalogueRequestOperations>();   // SCRUM-662
builder.Services.AddScoped<PEMS.Application.Interfaces.IPartCatalogueAdminOperations, PEMS.Web.Services.PartCatalogueAdminOperations>();   // SCRUM-658
builder.Services.AddScoped<PEMS.Application.Interfaces.IPartCatalogueOperations, PEMS.Web.Services.PartCatalogueOperations>();   // SCRUM-659, 660, 661
builder.Services.AddScoped<PEMS.Application.Interfaces.IBarcodeOperations, PEMS.Web.Services.BarcodeOperations>();   // SCRUM-671
builder.Services.AddScoped<PEMS.Application.Interfaces.IMrsImportOperations, PEMS.Web.Services.MrsImportOperations>();   // SCRUM-657
builder.Services.AddScoped<PEMS.Application.Interfaces.IIrnReceiptOperations, PEMS.Web.Services.IrnReceiptOperations>();   // SCRUM-655
builder.Services.AddScoped<PEMS.Application.Interfaces.IPurchaseOrderOperations, PEMS.Web.Services.PurchaseOrderOperations>();   // SCRUM-741
builder.Services.AddScoped<PEMS.Application.Interfaces.IProcurementPackageOperations, PEMS.Web.Services.ProcurementPackageOperations>();   // SCRUM-736
builder.Services.AddScoped<PEMS.Application.Interfaces.IMaterialRequisitionOperations, PEMS.Web.Services.MaterialRequisitionOperations>();   // SCRUM-726
builder.Services.AddScoped<PEMS.Application.Interfaces.IMyProfileOperations, PEMS.Web.Services.MyProfileOperations>();   // SCRUM-729   // SCRUM-669   // SCRUM-703   // SCRUM-674   // SCRUM-673   // SCRUM-730: My Work as rows   // SCRUM-668: the copilot reads as operations   // batch 5: a spreadsheet of a registered entity is imported by a definition
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.EntityUpdateAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.EntityDeleteAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.EntityToggleAction>();
builder.Services.AddSingleton<PEMS.Web.Definitions.EntityRecordLoader>();
// SCRUM-437: service operations: the interface methods marked [PemsOperation] are runnable from action definitions through the one handler operation.invoke
builder.Services.AddSingleton(PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly));
builder.Services.AddSingleton<PEMS.Web.Definitions.IActionHandler, PEMS.Web.Definitions.OperationInvokeAction>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IBuildEntitlementService, PEMS.Web.Services.BuildEntitlementService>();   // SCRUM-633
builder.Services.AddScoped<PEMS.Application.Interfaces.IHelpPageService, PEMS.Web.Services.HelpPageService>();   // SCRUM-634
builder.Services.AddScoped<PEMS.Application.Interfaces.IWorkflowManagementPageService, PEMS.Web.Services.WorkflowManagementPageService>();   // SCRUM-651
builder.Services.AddScoped<PEMS.Application.Interfaces.INumberingPageService, PEMS.Web.Services.NumberingPageService>();   // SCRUM-638
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationPageService, PEMS.Web.Services.OrganizationPageService>();   // SCRUM-639
builder.Services.AddScoped<PEMS.Application.Interfaces.INavigationMenuPageService, PEMS.Web.Services.NavigationMenuPageService>();   // SCRUM-636
builder.Services.AddScoped<PEMS.Application.Interfaces.ILicencePageService, PEMS.Web.Services.LicencePageService>();   // SCRUM-635
builder.Services.AddScoped<PEMS.Application.Interfaces.INavigationVisibilityPageService, PEMS.Web.Services.NavigationVisibilityPageService>();   // SCRUM-637
builder.Services.AddScoped<PEMS.Application.Interfaces.IPermissionMatrixPageService, PEMS.Web.Services.PermissionMatrixPageService>();   // SCRUM-641
builder.Services.AddScoped<PEMS.Application.Interfaces.IUserAdminPageService, PEMS.Web.Services.UserAdminPageService>();   // SCRUM-650
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationImportPageService, PEMS.Web.Services.OrganizationImportPageService>();   // SCRUM-640
builder.Services.AddScoped<PEMS.Application.Interfaces.IAuthorizationReportService, PEMS.Web.Services.AuthorizationReportService>();   // SCRUM-631
builder.Services.AddSingleton<PEMS.Web.Definitions.OperationReader>();   // SCRUM-775: read operations as grid rows and choices
builder.Services.AddSingleton<PEMS.Web.Definitions.IGridDataProvider, PEMS.Web.Definitions.OperationGridProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IDataSourceProvider, PEMS.Web.Definitions.OperationOptionsProvider>();
builder.Services.AddSingleton<PEMS.Web.Definitions.IDataSourceProvider, PEMS.Web.Definitions.OperationFileProvider>();   // 6b: a file or JSON from a read operation, served by GET /x/{key}
builder.Services.AddScoped<PEMS.Web.Definitions.DataSourceExecutor>();
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionService, PEMS.Persistence.Services.DefinitionService>();
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionShipper>(sp => (PEMS.Persistence.Services.DefinitionService)sp.GetRequiredService<PEMS.Application.Definitions.IDefinitionService>());
builder.Services.AddScoped<PEMS.Application.Definitions.IDefinitionPreflight>(sp => (PEMS.Persistence.Services.DefinitionService)sp.GetRequiredService<PEMS.Application.Definitions.IDefinitionService>());   // SCRUM-797
builder.Services.AddSingleton<PEMS.Web.Definitions.OwnershipPreflightReport>();   // SCRUM-797: the preflight of the shipped package, cached, for Database Update and /health/database

// یک [Authorize] سراسری روی همه‌ی صفحات؛ فقط اکشن‌هایی که [AllowAnonymous] دارند
// (مثل صفحه‌ی Login) بدون ورود قابل دسترسی هستند.
builder.Services.AddControllersWithViews(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(
        new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));

    // طبق CMMS Production Baseline v1 بند P0-03: بعد از AuthorizeFilter اجرا می‌شود - اول
    // احراز هویت، بعد بررسی MustChangePassword روی همه‌ی درخواست‌ها (نه فقط لحظه‌ی Login).
    options.Filters.Add(new PEMS.Web.Security.PasswordChangeGateFilter());

    // SCRUM-435: unhandled exceptions of API controllers become RFC 9457 problems, not the HTML error page.
    options.Filters.Add<PEMS.Web.Api.ApiExceptionFilter>();

    // SCRUM-202: every uploaded file of every action passes the shared upload guard (allowlist, size, content, malware scan).
    options.Filters.Add<PEMS.Web.Security.Uploads.UploadGuardFilter>();

    // SCRUM-421: numbers and dates bind the way a browser submits them in every culture (12.5 on a Persian page, Persian digits typed by a
    // Persian user); error messages and empty values behave as before.
    PEMS.UI.Binding.MvcOptionsExtensions.AddPemsInputBinding(options);

    // SCRUM-540: the default validation messages ("The {0} field is required.") become resource keys, so they follow the page language.
    options.ModelMetadataDetailsProviders.Add(new PEMS.Web.Configuration.ValidationMessageKeys());
})
.AddViewLocalization(Microsoft.AspNetCore.Mvc.Razor.LanguageViewLocationExpanderFormat.Suffix)
// SCRUM-477: validation messages and display names are looked up in SharedResource (a text without a key is shown as written).
.AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(PEMS.Web.Resources.SharedResource)));




// =========================
// Database
// =========================

// Code-first bootstrap: the database is created (if missing) before anything connects to it
// (Hangfire storage below installs its own schema on configuration) and migrated before seeding.
var databaseOptions = builder.Configuration
    .GetSection(PEMS.Persistence.Initialization.DatabaseInitializationOptions.SectionName)
    .Get<PEMS.Persistence.Initialization.DatabaseInitializationOptions>()
    ?? new PEMS.Persistence.Initialization.DatabaseInitializationOptions();
var databaseLogger = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger("DatabaseInitializer");
await PEMS.Persistence.Initialization.DatabaseInitializer.EnsureDatabaseExistsAsync(
    builder.Configuration.GetConnectionString("PEMSConnection"), databaseOptions, databaseLogger);

builder.Services.AddDbContext<PEMSDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PEMSConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }
    ));


// =========================
// Background Jobs (Hangfire)
// =========================
// جایگزین Task.Run بدون Persistence/Retry که برای پردازش AI استفاده می‌شد: حالا صف کارها روی
// همان دیتابیس SQL Server ذخیره می‌شود (اگر برنامه Restart شود، کار گم نمی‌شود)، Retry خودکار
// دارد، و یک Dashboard مدیریتی (/hangfire، فقط SystemAdmin) برای رصد وضعیت پردازش‌های AI دارد.

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("PEMSConnection")));

builder.Services.AddHangfireServer();


// =========================
// Caching (طبق Volume II-Bible: "Caching: Redis")
// =========================
// اگر Connection String واقعی Redis تنظیم شده باشد از Redis (توزیع‌شده، مناسب چندسروری)
// استفاده می‌شود؛ در غیر این صورت (پیش‌فرض) از IMemoryCache داخل همان Process - بدون نیاز به
// نصب/راه‌اندازی هیچ زیرساخت بیرونی. تغییر این تصمیم فقط همین‌جا انجام می‌شود، هیچ سرویس دیگری
// وابسته به این انتخاب نیست چون همه از طریق ICacheService کار می‌کنند.

var redisConnection = builder.Configuration.GetConnectionString("Redis");

builder.Services.AddMemoryCache();

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
    });

    builder.Services.AddScoped<ICacheService, RedisCacheService>();
}
else
{
    builder.Services.AddScoped<ICacheService, MemoryCacheService>();
}




// =========================
// ASP.NET Core Identity (Authentication / Authorization)
// =========================

var maxFailedAccessAttempts = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Authentication:Lockout:MaxFailedAccessAttempts");
var defaultLockoutMinutes = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Authentication:Lockout:DefaultLockoutMinutes");
var passwordRequiredLength = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Authentication:Password:RequiredLength");
var passwordRequireDigit = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Authentication:Password:RequireDigit");
var passwordRequireUppercase = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Authentication:Password:RequireUppercase");
var passwordRequireNonAlphanumeric = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Authentication:Password:RequireNonAlphanumeric");
var passwordHistoryCount = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Authentication:Password:HistoryCount");
builder.Services.Configure<PEMS.Web.Security.PasswordHistoryOptions>(o => o.Count = passwordHistoryCount);
var authCookieName = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Authentication:Cookie:Name");
var authCookieExpireHours = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredDouble(builder.Configuration, "Authentication:Cookie:ExpireHours");
var securityStampValidationMinutes = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredDouble(builder.Configuration, "Authentication:SecurityStampValidationMinutes");
var cookieSecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
var authCookieSlidingExpiration = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Authentication:Cookie:SlidingExpiration");
var hstsMaxAgeDays = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredDouble(builder.Configuration, "Hosting:HstsMaxAgeDays");
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(hstsMaxAgeDays));

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // سیاست رمز عبور - برای محیط سازمانی داخلی؛ در صورت نیاز سخت‌گیرانه‌تر شود
        options.Password.RequiredLength = passwordRequiredLength;
        options.Password.RequireDigit = passwordRequireDigit;
        options.Password.RequireUppercase = passwordRequireUppercase;
        options.Password.RequireNonAlphanumeric = passwordRequireNonAlphanumeric;

        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = maxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(defaultLockoutMinutes);
    })
    .AddEntityFrameworkStores<PEMSDbContext>()
    .AddDefaultTokenProviders()
    // SCRUM-412: a new password must differ from the current one and the last N; PemsUserManager records the replaced hash
    .AddPasswordValidator<PEMS.Web.Security.PasswordHistoryValidator>()
    .AddUserManager<PEMS.Web.Security.PemsUserManager>()
    // SCRUM-540: password-policy and other Identity messages follow the page language (Id_* resource keys)
    .AddErrorDescriber<PEMS.Web.Security.LocalizedIdentityErrorDescriber>();


// نقش‌های کاربر در Claim کوکی هستند؛ پیش‌فرض Identity فقط هر ۳۰ دقیقه آن‌ها را از دیتابیس تازه می‌کند و تغییر نقش/دسترسی
// در پنل مدیر تا آن زمان (یا خروج/ورود دوباره) در سایدبار همان کاربر دیده نمی‌شد. هر دقیقه تازه‌سازی می‌شود.
builder.Services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(securityStampValidationMinutes));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromHours(authCookieExpireHours);
    options.SlidingExpiration = authCookieSlidingExpiration;

    options.Cookie.Name = authCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});


// =========================
// طبق سند PEMS-SEC-PERMIT-AUTH-001: WebAuthn/Passkey Relying Party برای Step-up Authentication.
// ServerDomain/Origins از appsettings خوانده می‌شود - مقدار پیش‌فرض برای محیط توسعه‌ی محلی است؛
// در Production باید دامنه‌ی واقعی HTTPS تنظیم شود (WebAuthn بدون HTTPS/localhost کار نمی‌کند).
// =========================
builder.Services.AddFido2(options =>
{
    options.ServerDomain = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Fido2:ServerDomain");
    options.ServerName = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Fido2:ServerName");
    options.Origins = (builder.Configuration.GetSection("Fido2:Origins").Get<string[]>()
        ?? throw new InvalidOperationException("Missing required setting 'Fido2:Origins' (Fido2__Origins__0 in env/<name>.env).")).ToHashSet();
    options.TimestampDriftTolerance = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Fido2:TimestampDriftToleranceMs");
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddScoped<
    Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>,
    ApplicationUserClaimsPrincipalFactory>();




// =========================
// Application Services
// =========================

builder.Services.AddScoped<
    IEngineeringDocumentService,
    EngineeringDocumentService>();


builder.Services.AddScoped<
    IDocumentWorkflowService,
    DocumentWorkflowService>();

// طبق ممیزی Package Procurement Contract Finalization (§D، Phase 2.1)
builder.Services.AddScoped<
    PEMS.Application.Interfaces.IContractWorkflowService,
    PEMS.Persistence.Services.ContractWorkflowService>();

// طبق ممیزی Package Procurement Contract Finalization (§C/§D، Phase 3)
builder.Services.AddScoped<
    PEMS.Application.Interfaces.ITenderService,
    PEMS.Persistence.Services.TenderService>();

// طبق ممیزی Package Procurement Contract Finalization (§C/§E، Phase 1.1 + Phase 3)
builder.Services.AddScoped<
    PEMS.Application.Interfaces.IVendorQualificationService,
    PEMS.Persistence.Services.VendorQualificationService>();

// طبق گزارش سخت‌افزاری نهایی Package Procurement (Gap ۳)
builder.Services.AddScoped<
    PEMS.Application.Interfaces.IContractApprovalAuthorityRuleService,
    PEMS.Persistence.Services.ContractApprovalAuthorityRuleService>();


// ==============================================================================
// Workflow Platform (فاز A) - موتور عمومی گردش‌کار، طبق سند Baseline مستقل از هر ماژول.
// IWorkflowDbContext به همان PEMSDbContext در حال استفاده در این Request نگاشت می‌شود
// (نه یک DbContext/اتصال جدا) تا Instance/History همیشه در همان تراکنش رکورد کسب‌وکاری
// مربوطه ذخیره شوند.
// ==============================================================================
builder.Services.AddScoped<IWorkflowDbContext>(sp => sp.GetRequiredService<PEMSDbContext>());
builder.Services.AddScoped<IWorkflowRuntime, WorkflowRuntimeService>();
builder.Services.AddScoped<IWorkflowInstanceControl>(sp => (IWorkflowInstanceControl)sp.GetRequiredService<IWorkflowRuntime>());
// Enterprise Actor Resolution (مشترک بین گردش‌کارها) + اتصال به موتور گردش‌کار
builder.Services.AddScoped<PEMS.Application.ActorResolution.IEnterpriseActorResolver, PEMS.Persistence.Services.EnterpriseActorResolver>();
builder.Services.AddScoped<IWorkflowActorResolutionHook, PEMS.Persistence.Workflow.ActorResolutionWorkflowHook>();
builder.Services.AddScoped<IWorkflowBusinessContextProvider, PEMS.Persistence.Workflow.PartCatalogueWorkflowContextProvider>();

// Phase 1 Hardening: نسخه‌بندی/Binding/اعتبارسنجی/رجیستری/مدل خواندنی. IWorkflowRuntime بدون تغییر امضا؛
// پارامترهای اختیاری سازنده‌ی WorkflowRuntimeService از همین ثبت‌ها پر می‌شوند.
// legacy module action names live in PemsWorkflowContracts (one list for the app, the seeders and the tests)
builder.Services.AddSingleton<IWorkflowActionRegistry>(_ => PEMS.Persistence.Workflow.PemsWorkflowContracts.CreateActionRegistry());
// فیلدهای Fact ماژول‌ها (مثلاً Part Catalogue) به گرامر شرط‌ها اضافه می‌شود
builder.Services.AddSingleton<IWorkflowConditionEvaluator>(_ => new WorkflowConditionEvaluator(PEMS.Persistence.Workflow.PartCatalogueWorkflow.Fields));
builder.Services.AddSingleton<IWorkflowEventSink, NoOpWorkflowEventSink>();
builder.Services.AddScoped<IWorkflowReferenceCatalog, PemsWorkflowReferenceCatalog>();
builder.Services.AddScoped<IWorkflowAuthorizationHook, AccessControlWorkflowAuthorizationHook>();
// SCRUM-103: what each module's code needs from its definition; the validator rejects a version that breaks it
builder.Services.AddSingleton<IWorkflowModuleContractRegistry>(_ => PEMS.Persistence.Workflow.PemsWorkflowContracts.CreateRegistry());
builder.Services.AddScoped<IWorkflowVersionValidator, WorkflowVersionValidator>();
builder.Services.AddScoped<IWorkflowBindingService, WorkflowBindingService>();
builder.Services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IWorkflowAdministrationService, PEMS.Persistence.Services.WorkflowAdministrationService>();
builder.Services.AddScoped<IWorkflowReadModelService, WorkflowReadModelService>();

builder.Services.AddScoped<IVdrWorkflowService, VdrWorkflowService>();
builder.Services.AddScoped<ITechnicalQueryService, TechnicalQueryService>();
builder.Services.AddScoped<IMOCService, MOCService>();
builder.Services.AddScoped<IEngineeringCopilotService, EngineeringCopilotService>();
builder.Services.AddScoped<IWorkOrderReviewService, WorkOrderReviewService>();

// طبق سند معماری Employee/Team/Labor Cost - این سه سرویس ساخته شده بودن ولی جایی Register
// نشده بودن (باگ واقعی که موقع اولین درخواست به TeamController/WorkOrder Labor کرش می‌کرد)
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IWorkOrderLaborService, WorkOrderLaborService>();
builder.Services.AddScoped<IWorkOrderChildService, WorkOrderChildService>();

// طبق «PEMS Dynamic Maintenance Organization»
builder.Services.AddScoped<IOrganizationStructureService, OrganizationStructureService>();
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationValidationService, OrganizationValidationService>();
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationHierarchyService, OrganizationHierarchyService>();
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationImportService, OrganizationImportService>();
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationResolver, OrganizationResolver>();
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationConsistencyService, OrganizationConsistencyService>();

// طبق «PEMS Phase 6 — CMMS Operational Excellence & Maintenance Execution»: باید قبل از
// WorkOrderLifecycleService ثبت شود چون آن سرویس به IWorkOrderReleaseGateService وابسته است
// (ترتیب ثبت DI مهم نیست چون Scoped/Constructor Injection است، ولی همین‌جا برای خوانایی کنار هم آمدند)
builder.Services.AddScoped<IWorkOrderReleaseGateService, WorkOrderReleaseGateService>();
builder.Services.AddScoped<IWorkOrderLifecycleService, WorkOrderLifecycleService>();
builder.Services.AddScoped<IWorkOrderSectionGuard, WorkOrderSectionGuard>();
builder.Services.AddScoped<IPMGenerationService, PMGenerationService>();
builder.Services.AddScoped<IRcmOperationalBridgeService, RcmOperationalBridgeService>();
builder.Services.AddScoped<IStoreRequisitionLifecycleService, StoreRequisitionLifecycleService>();
// SCRUM-472: warehouse intake of work-order materials (My Work queue, linked store requisitions)
builder.Services.AddScoped<IWarehouseIntakeService, WarehouseIntakeService>();
// SCRUM-471: pre-procurement intake; the same instance is the planned-supply source of the replenishment run
builder.Services.AddScoped<PreProcurementService>();
builder.Services.AddScoped<IPreProcurementService>(sp => sp.GetRequiredService<PreProcurementService>());
builder.Services.AddScoped<IProcurementPipeline>(sp => sp.GetRequiredService<PreProcurementService>());
// SCRUM-470: PO lines, submission and approval by the PO authority table; Procurement__* settings from env
builder.Services.Configure<PEMS.Application.Interfaces.ProcurementOptions>(builder.Configuration.GetSection("Procurement"));
builder.Services.AddScoped<IPurchaseOrderApprovalService, PurchaseOrderApprovalService>();
// SCRUM-473: receiving against PO lines; the same class is the incoming-supply source of the net-demand calculation
builder.Services.AddScoped<PurchaseOrderReceivingService>();
builder.Services.AddScoped<IPurchaseOrderReceivingService>(sp => sp.GetRequiredService<PurchaseOrderReceivingService>());
builder.Services.AddScoped<PEMS.Application.Inventory.IIncomingSupplyProvider>(sp => sp.GetRequiredService<PurchaseOrderReceivingService>());
builder.Services.AddScoped<IIrnReceiptService, IrnReceiptService>();
builder.Services.AddScoped<IReliabilityService, ReliabilityService>();
builder.Services.AddScoped<IInspectionService, InspectionService>();
builder.Services.AddScoped<IKpiService, KpiService>();
builder.Services.AddScoped<IKpiTargetService, KpiTargetService>();
builder.Services.AddScoped<IExecutiveKpiService, ExecutiveKpiService>();
builder.Services.AddScoped<IPermitService, PermitService>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddScoped<IUserRoleAssignmentSynchronizer, UserRoleAssignmentSynchronizer>();
builder.Services.AddScoped<IDisciplineMasterService, DisciplineMasterService>();
// طبق سند «PEMS Phase 6 — User Module & Navigation Visibility Architecture»: Resolver عمداً
// Scoped و بدون Interface است (Web-only composition، نه یک سرویس Application/Persistence) -
// فقط توسط _SidebarMenu.cshtml و کنترلرهای Admin مربوط به Visibility مصرف می‌شود
builder.Services.AddScoped<PEMS.Application.Interfaces.INavigationVisibilityOverrideService, PEMS.Persistence.Services.NavigationVisibilityOverrideService>();
builder.Services.AddScoped<PEMS.Web.Security.NavigationVisibilityResolver>();
// طبق «PEMS — Enterprise User-Centric Authorization Workspace Consolidation»: تنها سرویس تازه‌ی این
// فاز - هم Admin/Authorization موجود، هم Admin/Users/Details تازه، از همین یک سرویس می‌خوانند
builder.Services.AddScoped<PEMS.Application.Interfaces.IUserAuthorizationSummaryService, PEMS.Persistence.Services.UserAuthorizationSummaryService>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IRolePermissionAdminService, PEMS.Persistence.Services.RolePermissionAdminService>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IPermissionMatrixEditor, PEMS.Persistence.Services.PermissionMatrixEditor>();
builder.Services.AddScoped<IRoleModuleActionService, RoleModuleActionService>();
builder.Services.AddScoped<IIsolationService, IsolationService>();
builder.Services.AddScoped<IGasTestService, GasTestService>();
builder.Services.AddScoped<ISimopsService, SimopsService>();
builder.Services.AddScoped<IShiftHandoverService, ShiftHandoverService>();
builder.Services.AddScoped<IExecutionGateService, ExecutionGateService>();
builder.Services.AddScoped<IWebAuthnService, WebAuthnService>();
builder.Services.AddScoped<IPermitAuthorityService, PermitAuthorityService>();

builder.Services.AddScoped<IEmployeeSkillService, EmployeeSkillService>();

builder.Services.AddScoped<ICrewService, CrewService>();
builder.Services.AddScoped<IEmployeeLaborRateService, EmployeeLaborRateService>();

// طبق «PEMS — Quick Setup & Domain Import Architecture Refactoring»
builder.Services.AddScoped<ISetupService, SetupService>();

builder.Services.AddScoped<IMaterialItemService, MaterialItemService>();
builder.Services.AddScoped<IMaterialStockService, MaterialStockService>();
builder.Services.AddScoped<IInventoryTransactionService, InventoryTransactionService>();
builder.Services.AddScoped<IMrsImportService, MrsImportService>();

// طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ (Production Core)
builder.Services.AddScoped<IProductionOrderService, ProductionOrderService>();
builder.Services.AddScoped<IProductionRunService, ProductionRunService>();

// طبق «PEMS Operations Phase 2A — Foundational Hardening»
builder.Services.AddScoped<IProductionTargetService, ProductionTargetService>();
builder.Services.AddScoped<IProductionDowntimeService, ProductionDowntimeService>();
builder.Services.AddScoped<IOperationsReportService, OperationsReportService>();

// ==============================================================================
// Global Intelligent Search Framework (فاز A) - طبق معماری: هر Provider جدید فقط با یک
// خط AddScoped اضافه ثبت می‌شود و خودکار توسط GlobalSearchService شناسایی می‌شود؛ خودِ
// GlobalSearchService هیچ تغییری برای موجودیت جدید نیاز ندارد.
// ==============================================================================
builder.Services.AddScoped<IGlobalSearchService, GlobalSearchService>();
// SCRUM-64: command palette (records through the provider security contract, permission-filtered actions)
builder.Services.AddSingleton<PEMS.Web.Navigation.CommandPaletteActions>();
builder.Services.AddScoped<PEMS.Web.Navigation.CommandPaletteService>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.EquipmentSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.EngineeringDocumentSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.VdrItemSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.LetterSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.VendorSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.ProjectSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.EmployeeSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.DepartmentSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.DisciplineSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.MaterialRequisitionSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.WorkOrderSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.ContractSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.DocumentRevisionSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.TechnicalQuerySearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.PartCatalogueRequestSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.MOCSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.SparePartSearchProvider>();

// طبق فاز F: Abstraction کاربر جاری برای Providerهایی که نیاز به فیلتر بر اساس کاربر
// دارند (مثل Notification) - بدون وابسته‌کردن PEMS.Persistence به PEMS.Web
builder.Services.AddScoped<PEMS.Application.Interfaces.ICurrentUserContext, PEMS.Web.Security.CurrentUserContext>();

builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.EquipmentTagSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.AttachmentSearchProvider>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.NotificationSearchProvider>();


builder.Services.AddScoped<IProjectService, ProjectService>();

builder.Services.AddScoped<ITransmittalService, TransmittalService>();

builder.Services.AddScoped<IMaterialRequisitionService, MaterialRequisitionService>();

builder.Services.AddScoped<IExcelExportService, ExcelExportService>();

builder.Services.AddScoped<IChatAssistantService, ChatAssistantService>();

// طبق «PEMS Bilingual Help & Knowledge System»
builder.Services.AddScoped<IHelpService, HelpService>();
builder.Services.AddScoped<IHelpAiExplainerService, HelpAiExplainerService>();
builder.Services.AddScoped<IHelpWorkflowDiagramService, HelpWorkflowDiagramService>();
builder.Services.AddScoped<IHelpVisualService, HelpVisualService>();
builder.Services.AddScoped<IHelpStandardReferenceService, HelpStandardReferenceService>();
builder.Services.AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.HelpArticleSearchProvider>();


builder.Services.AddScoped<IVendorService, VendorService>();

builder.Services.AddScoped<IContractService, ContractService>();

builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();

builder.Services.AddScoped<IVDRService, VDRService>();

builder.Services.AddScoped<IVDRImportService, VDRImportService>();

builder.Services.AddScoped<IMDRImportService, MDRImportService>();

builder.Services.AddScoped<IRoutingRuleService, RoutingRuleService>();

builder.Services.AddScoped<IIntakeService, IntakeService>();

builder.Services.AddScoped<ITableExtractionImportService, TableExtractionImportService>();

builder.Services.AddScoped<IRelationshipService, RelationshipService>();

builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

builder.Services.AddScoped<ISavedFilterService, SavedFilterService>();

builder.Services.AddScoped<IDocumentNumberingService, DocumentNumberingService>();

// طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۳: شماره‌گذاری خودکار ترانسمیتال (TV-X-UU-AAAA-NNNN)
builder.Services.AddScoped<ITransmittalNumberingService, TransmittalNumberingService>();

// طبق بخش ۴.۳.۱ همان سند: شماره‌گذاری رسمی مدرک فروشنده به فرمت CONTRACTOR
builder.Services.AddScoped<IVdrDocumentNumberingService, VdrDocumentNumberingService>();

// طبق بخش ۵.۲ همان سند: آپلود واقعی مدارک فروشنده روی FTP Server (بدون بخش ایمیل - طبق تصمیم کاربر، اپلیکیشن آفلاین است)
builder.Services.AddScoped<IFtpUploadService, FtpUploadService>();

// طبق بخش ۶-۷ همان سند: تحویل مستندات نهایی (MDB)
builder.Services.AddScoped<IManufacturerDataBookService, ManufacturerDataBookService>();

// طبق بخش M ممیزی سخت‌سازی مهندسی: چرخه‌ی Hold با تاریخچه‌ی کامل
builder.Services.AddScoped<IDocumentHoldService, DocumentHoldService>();

// طبق CMMS Production Baseline v1 بند P0-04: سرویس شماره‌گذاری مرکزی و اتمیک -
// جایگزین الگوهای Count()+1 پراکنده در WorkOrder/MaintenancePlan/Instrument/StoreRequisition.
builder.Services.AddScoped<INumberingService, NumberingService>();
// SCRUM-40: الگوهای شماره‌گذاری اسناد تدارکات (Admin > Numbering)
builder.Services.AddScoped<INumberingSchemeService, NumberingSchemeService>();

// SCRUM-429: ایمیل سازمانی (SMTP خود سازمان، MailKit) و ارسال دعوت‌نامه‌ی مناقصه؛ تنظیمات env Email__*، حساب فقط در secrets
builder.Services.Configure<PEMS.Infrastructure.Email.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddSingleton<IEmailSender, PEMS.Infrastructure.Email.SmtpEmailSender>();
builder.Services.AddScoped<ITenderInvitationService, TenderInvitationService>();

// Warehouse / Part Catalogue Request (درخواست کاتالوگ قطعه): تنظیمات + سرویس‌ها
builder.Services.AddSingleton(builder.Configuration.GetSection(PartCatalogueOptions.SectionName).Get<PartCatalogueOptions>() ?? new PartCatalogueOptions());
builder.Services.AddScoped<IPartCatalogueLookupService, PartCatalogueLookupService>();
builder.Services.AddScoped<IPartCatalogueDuplicateService, PartCatalogueDuplicateService>();
// Phase 2 (P1-P3): فقط‌خواندنی - موجودی قابل‌استفاده، جمع‌آوری تقاضا و نیاز خالص
builder.Services.AddScoped<IUsableStockService, UsableStockService>();
builder.Services.AddScoped<INetDemandCollector, NetDemandCollector>();
builder.Services.AddScoped<INetDemandService, NetDemandService>();
// Phase 2 / P5: توصیه‌های تأمین (اجرای دستی؛ فقط روی جدول‌های خودش می‌نویسد)
builder.Services.AddScoped<IReplenishmentRecommendationService, ReplenishmentRecommendationService>();
// دروازه‌ی موقت P5: پیش‌فرض غیرفعال تا اعمال Migration تأییدشده؛ فقط Features:ReplenishmentRecommendations:Enabled=true فعال می‌کند
builder.Services.AddSingleton<PEMS.Web.Security.IReplenishmentAvailability, PEMS.Web.Security.ReplenishmentAvailability>();
// سایدبار داده‌محور (پشت پرچم Features:DynamicSidebar:Enabled؛ پیش‌فرض سایدبار قدیمی)
builder.Services.AddSingleton<PEMS.Web.Navigation.IDynamicSidebarAvailability, PEMS.Web.Navigation.DynamicSidebarAvailability>();
builder.Services.AddScoped<INavigationMenuService, NavigationMenuService>();
builder.Services.AddScoped<INavigationCountService, NavigationCountService>();
builder.Services.AddScoped<PEMS.Web.Navigation.DynamicSidebarBuilder>();
builder.Services.AddScoped<PEMS.Web.Navigation.NavigationBadgeService>();
builder.Services.AddScoped<IPartCatalogueService, PartCatalogueService>();
builder.Services.AddScoped<IPartCatalogueReportService, PartCatalogueReportService>();   // SCRUM-478
builder.Services.AddScoped<IPartCatalogueAdminService, PartCatalogueAdminService>();
builder.Services.AddScoped<IPartCatalogueSettingsService, PartCatalogueSettingsService>();

// طبق CMMS Production Baseline v1 بند ۶ (CMMS-01: Asset Foundation)
builder.Services.AddScoped<IFunctionalLocationService, FunctionalLocationService>();

// طبق CMMS Production Baseline v1 بند ۱۰.۳ (CMMS-02: Maintenance Planning)
builder.Services.AddScoped<IJobPlanService, JobPlanService>();

// طبق CMMS Production Baseline v1 بند P0-05 (CMMS-03: Work Management)
// A3: تفکیک وظایف Work Request - پیش‌فرض روشن؛ برای خاموش کردن: "Cmms": { "WorkRequest": { "EnforceSegregationOfDuties": false } } (SystemAdmin همیشه فقط با ممیزی Override می‌کند)
builder.Services.AddSingleton(new PEMS.Application.Common.WorkRequestPolicyOptions
{
    EnforceSegregationOfDuties = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Cmms:WorkRequest:EnforceSegregationOfDuties")
});
builder.Services.AddScoped<IWorkRequestService, WorkRequestService>();

builder.Services.AddScoped<IUserDashboardService, UserDashboardService>();

builder.Services.AddScoped<INotificationService, NotificationService>();
// SCRUM-357: ممیزی + ابطال نشست + اعلان مشترک مسیرهای بازیابی حساب مدیر
builder.Services.AddScoped<IRecoveryEventService, RecoveryEventService>();
// SCRUM-354: محافظ آخرین مدیر فعال + ریست حساب مدیر توسط مدیر دیگر
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
// SCRUM-355: کدهای بازیابی یکبارمصرف حساب مدیر
builder.Services.AddScoped<IAdminRecoveryCodeService, AdminRecoveryCodeService>();

// SCRUM-356: break-glass روی سرور (پوشه‌ی فایل درخواست، پنجره‌ی ۱۵ دقیقه، شبکه‌ی مدیریتی، توکن اختیاری فروشنده)
var breakGlassFolder = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Security:BreakGlass:RequestFolder");
builder.Services.AddSingleton(new PEMS.Application.Common.BreakGlassOptions
{
    RequestFolder = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, breakGlassFolder)),
    WindowMinutes = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Security:BreakGlass:WindowMinutes"),
    // خالی = فقط localhost
    ManagementNetworks = PEMS.Application.Common.BreakGlassRules.ParseNetworks(builder.Configuration["Security:BreakGlass:ManagementNetworks"]),
    VendorRecoveryEnabled = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredBool(builder.Configuration, "Security:BreakGlass:VendorRecovery:Enabled"),
    VendorPublicKey = builder.Configuration["Security:BreakGlass:VendorRecovery:PublicKey"],
    MachineFingerprint = builder.Configuration["Security:BreakGlass:VendorRecovery:MachineFingerprint"]
});
builder.Services.AddScoped<IBreakGlassService, BreakGlassService>();

// SCRUM-355/356: محدودیت نرخ صفحه‌های بازیابی بدون ورود (برای هر IP، فقط POST)
var recoveryPermitLimit = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Security:Recovery:RateLimit:PermitLimit");
var recoveryWindow = TimeSpan.FromMinutes(PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredDouble(builder.Configuration, "Security:Recovery:RateLimit:WindowMinutes"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RecoveryRateLimiting.PolicyName, context => RecoveryRateLimiting.Partition(context, recoveryPermitLimit, recoveryWindow));
});
builder.Services.AddScoped<IMyWorkService, PEMS.Persistence.Services.MyWorkService>();

builder.Services.AddScoped<ILetterService, LetterService>();
builder.Services.AddScoped<ILetterTemplateService, LetterTemplateService>();
builder.Services.AddScoped<ILetterReminderService, LetterReminderService>();
builder.Services.AddScoped<ILetterComposeService, LetterComposeService>();
builder.Services.AddScoped<ILetterComplianceService, LetterComplianceService>();
builder.Services.AddScoped<IIncomingCorrespondenceService, IncomingCorrespondenceService>();
builder.Services.AddScoped<IExecutiveDashboardService, ExecutiveDashboardService>();
builder.Services.Configure<PEMS.Web.BackgroundJobs.LetterReminderOptions>(builder.Configuration.GetSection("LetterReminders"));
builder.Services.AddHostedService<PEMS.Web.BackgroundJobs.LetterReminderBackgroundService>();

builder.Services.AddScoped<ILetterReferralService, LetterReferralService>();

builder.Services.AddScoped<ITagService, TagService>();

// کارتابل نامه‌نگاری: پوشه/سطل/آرشیو شخصی، درخت برچسب، تفویض، قانون مسیریابی، مخاطبین، یادآور شخصی، گزارش (روی چارت سازمانی)
builder.Services.AddScoped<PEMS.Application.Organization.IOrganizationSubordinateResolver, PEMS.Persistence.Services.OrganizationSubordinateResolver>();
builder.Services.AddScoped<ILetterCartableService, LetterCartableService>();
builder.Services.AddScoped<ILetterTagTreeService, LetterTagTreeService>();
builder.Services.AddScoped<ILetterUserSettingsService, LetterUserSettingsService>();
builder.Services.AddScoped<ILetterDelegationService, LetterDelegationService>();
builder.Services.AddScoped<ILetterRoutingRuleService, LetterRoutingRuleService>();
builder.Services.AddScoped<ILetterContactService, LetterContactService>();
builder.Services.AddScoped<ILetterPersonalReminderService, LetterPersonalReminderService>();
builder.Services.AddScoped<ILetterReportService, LetterReportService>();
builder.Services.AddHostedService<PEMS.Web.BackgroundJobs.LetterPersonalReminderBackgroundService>();

builder.Services.AddScoped<ITaskService, TaskService>();

// Task Manager (فضا/Backlog/Board/Sprint روی همان TaskItem) - دسترسی سطح-شیء در TaskAccess
builder.Services.AddScoped<ITaskManagerService, PEMS.Persistence.Services.TaskManager.TaskManagerService>();
builder.Services.AddScoped<IWorkSpaceService, PEMS.Persistence.Services.TaskManager.WorkSpaceService>();

builder.Services.AddScoped<ICorrespondenceReportService, CorrespondenceReportService>();

builder.Services.AddScoped<IEngineeringReportService, EngineeringReportService>();

builder.Services.AddScoped<IVDRReportService, VDRReportService>();

builder.Services.AddScoped<ICmmsReportService, CmmsReportService>();

builder.Services.AddScoped<IProcurementReportService, ProcurementReportService>();

builder.Services.AddScoped<IMeetingService, MeetingService>();

builder.Services.AddScoped<IRichTextSanitizerService, RichTextSanitizerService>();

builder.Services.AddScoped<ILetterNumberingService, LetterNumberingService>();

builder.Services.AddScoped<IProcurementPackageService, ProcurementPackageService>();

builder.Services.AddScoped<IVendorRiskService, VendorRiskService>();

builder.Services.AddScoped<IDocumentCommentService, DocumentCommentService>();

builder.Services.AddScoped<IEquipmentImportService, EquipmentImportService>();
builder.Services.AddScoped<IFunctionalLocationImportService, FunctionalLocationImportService>();
builder.Services.AddScoped<IEquipmentFunctionalLocationAssignmentService, EquipmentFunctionalLocationAssignmentService>();
builder.Services.AddScoped<IMaintenanceHistoryImportService, MaintenanceHistoryImportService>();
builder.Services.AddScoped<IBarcodeService, BarcodeService>();
builder.Services.AddScoped<IBarcodeRenderingService, BarcodeRenderingService>();

builder.Services.AddScoped<IEquipmentPdfScanService, EquipmentPdfScanService>();

builder.Services.AddScoped<IEquipmentService, EquipmentService>();

builder.Services.AddScoped<IAssetService, AssetService>();

builder.Services.AddScoped<IInstrumentService, InstrumentService>();
builder.Services.AddScoped<IMechanicalEquipmentService, MechanicalEquipmentService>();
builder.Services.AddScoped<IConditionReadingImportService, ConditionReadingImportService>();

builder.Services.AddScoped<ISparePartService, SparePartService>();

builder.Services.AddScoped<IMaintenancePlanService, MaintenancePlanService>();

builder.Services.AddScoped<IMaintenanceStrategyService, MaintenanceStrategyService>();

// RCM کلاس‌محور: الگوی استراتژی + تخصیص به تگ + Plan از تخصیص + پوشش + کاتالوگ حالت خرابی ISO 14224
builder.Services.AddScoped<IMaintenanceStrategyCommandService, MaintenanceStrategyCommandService>();
builder.Services.AddScoped<IStrategyAssignmentService, StrategyAssignmentService>();
builder.Services.AddScoped<IMaintenancePlanCommandService, MaintenancePlanCommandService>();
builder.Services.AddScoped<IStrategyCoverageService, StrategyCoverageService>();
builder.Services.AddScoped<IFailureModeCatalogService, FailureModeCatalogService>();
builder.Services.AddScoped<IStrategyImportService, StrategyImportService>();

builder.Services.AddScoped<IDisciplineWorkspaceService, DisciplineWorkspaceService>();

builder.Services.AddScoped<IEquipmentClassificationService, EquipmentClassificationService>();

builder.Services.AddScoped<IMaintenanceIntelligenceService, MaintenanceIntelligenceService>();

builder.Services.AddScoped<IEquipmentClassCoverageService, EquipmentClassCoverageService>();

// طبق «PEMS Phase 4 — RCM/FMEA/FMECA Decision Intelligence»
builder.Services.AddScoped<IFMEAService, FMEAService>();
builder.Services.AddScoped<IRCMService, RCMService>();
builder.Services.AddScoped<IRcmFmeaReadinessService, RcmFmeaReadinessService>();

// طبق «PEMS Phase 5 — Final Reliability Intelligence, Failure Analytics & RAM Foundation»
builder.Services.AddScoped<IReliabilityAnalyticsService, ReliabilityAnalyticsService>();
builder.Services.AddScoped<IReliabilityDataQualityService, ReliabilityDataQualityService>();

// طبق «PEMS Phase 7 — Maintenance Intelligence & Reliability Operationalization» - نام عمداً
// IReliabilityIntelligenceService است (نه IMaintenanceIntelligenceService) چون آن نام از قبل
// توسط سرویس متفاوت فاز ۲/۳ (توصیه‌ی PMTemplate/JobPlan بر اساس EquipmentClass) اشغال شده بود.
builder.Services.AddScoped<IReliabilityIntelligenceService, ReliabilityIntelligenceService>();
builder.Services.AddScoped<IRcmFeedbackService, RcmFeedbackService>();

// طبق «PEMS Phase 7.5 — CMMS Operational Data Readiness & Master Data Activation»
builder.Services.AddScoped<IEquipmentReadinessService, EquipmentReadinessService>();

// طبق «PEMS Phase 7.6 — Engineering Criticality Assessment & Controlled Maintenance Activation»
builder.Services.AddScoped<ICriticalityAssessmentService, CriticalityAssessmentService>();

// طبق «PEMS Phase 8 — Operational Maintenance & Reliability Data Activation» بند ۲۱/۲۲
builder.Services.AddScoped<ICmmsOperationsService, CmmsOperationsService>();
builder.Services.AddScoped<ICmmsCalendarService, CmmsCalendarService>();

// طبق «PEMS Phase 9 — Enterprise Reliability, RAM & Predictive Maintenance Intelligence»
builder.Services.AddScoped<IRamWeibullService, RamWeibullService>();
builder.Services.AddScoped<IReliabilitySimulationService, ReliabilitySimulationService>();
builder.Services.AddScoped<IConditionMonitoringService, ConditionMonitoringService>();
builder.Services.AddScoped<IPredictiveMaintenanceService, PredictiveMaintenanceService>();
builder.Services.AddScoped<IAIReliabilityInsightService, AIReliabilityInsightService>();
builder.Services.AddScoped<IClassIntelligenceService, ClassIntelligenceService>();
builder.Services.AddScoped<IEquipmentIntelligenceService, EquipmentIntelligenceService>();

// طبق ممیزی امنیتی (P0 - Engineering File Security Containment): ریشه‌ی ذخیره‌سازی عمداً بیرون
// از wwwroot (خواهر آن، نه زیرشاخه‌اش) - این همان چیزی است که آسیب‌پذیری اصلی را می‌بندد؛
// UseStaticFiles فقط داخل wwwroot سرویس می‌دهد.
// SCRUM-202: one upload guard for every upload (allowlist, size limit from Uploads__*, content check, malware-scan hook).
var uploadOptions = new PEMS.Application.Uploads.UploadOptions
{
    MaxFileSizeMb = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Uploads:MaxFileSizeMb"),
    MaxRequestSizeMb = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Uploads:MaxRequestSizeMb"),
    MalwareScanMode = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Uploads:MalwareScan:Mode"),
    MalwareScanCommand = builder.Configuration["Uploads:MalwareScan:Command"] ?? "",
    MalwareScanArguments = builder.Configuration["Uploads:MalwareScan:Arguments"] ?? "",
    MalwareScanTimeoutSeconds = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredInt(builder.Configuration, "Uploads:MalwareScan:TimeoutSeconds"),
    MalwareScanQuarantineFolder = builder.Configuration["Uploads:MalwareScan:QuarantineFolder"] ?? "",
};
if (uploadOptions.MaxFileSizeMb <= 0 || uploadOptions.MaxRequestSizeMb < uploadOptions.MaxFileSizeMb)
    throw new InvalidOperationException("Setting 'Uploads:MaxFileSizeMb' must be positive and not larger than 'Uploads:MaxRequestSizeMb'.");
builder.Services.AddSingleton(uploadOptions);
if (string.Equals(uploadOptions.MalwareScanMode, PEMS.Application.Uploads.MalwareScanModes.Command, StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(uploadOptions.MalwareScanCommand))
        throw new InvalidOperationException("Setting 'Uploads:MalwareScan:Command' is required when 'Uploads:MalwareScan:Mode' is Command.");
    builder.Services.AddSingleton<PEMS.Application.Uploads.IMalwareScanner, PEMS.Infrastructure.FileStorage.CommandLineMalwareScanner>();
}
else if (string.Equals(uploadOptions.MalwareScanMode, PEMS.Application.Uploads.MalwareScanModes.None, StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<PEMS.Application.Uploads.IMalwareScanner, PEMS.Application.Uploads.NoMalwareScanner>();
else
    throw new InvalidOperationException("Setting 'Uploads:MalwareScan:Mode' must be None or Command.");
builder.Services.AddSingleton<PEMS.Application.Uploads.IUploadGuard, PEMS.Application.Uploads.UploadGuard>();
// The request limits follow the setting, so a file the guard would accept is never cut off earlier by the server
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = uploadOptions.MaxRequestBytes);
builder.Services.AddSingleton<PEMS.UI.Components.IUploadLimits>(new PEMS.Web.Configuration.UploadLimits(uploadOptions.MaxFileSizeMb));
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o => o.MaxRequestBodySize = uploadOptions.MaxRequestBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = uploadOptions.MaxRequestBytes);

builder.Services.AddSingleton<PEMS.Application.Interfaces.IFileStorageService>(
    _ => new PEMS.Infrastructure.FileStorage.LocalFileStorageProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "filestore")));
// SCRUM-202: profile photos, signatures and editor images live in the file store too; wwwroot is only read for old files.
builder.Services.AddSingleton<PEMS.Application.Uploads.IProtectedImageStore>(sp => new PEMS.Application.Uploads.ProtectedImageStore(
    sp.GetRequiredService<PEMS.Application.Interfaces.IFileStorageService>(),
    builder.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")));

// طبق «PEMS — Enterprise File Storage Management»: زیرساخت جدید و کاملاً افزودنی چند-Provider -
// IFileStorageService بالا دست‌نخورده باقی می‌ماند؛ این‌ها موازی آن ثبت می‌شوند (بند ۲۲).
builder.Services.AddDataProtection();
builder.Services.AddSingleton<PEMS.Application.Interfaces.ICredentialProtector, PEMS.Infrastructure.FileStorage.CredentialProtector>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IStorageProviderClientFactory, PEMS.Persistence.Services.StorageProviderClientFactory>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IStorageRouteResolver, PEMS.Persistence.Services.StorageRouteResolver>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IEnterpriseFileStorageService, PEMS.Persistence.Services.EnterpriseFileStorageService>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IStorageProviderService, PEMS.Persistence.Services.StorageProviderService>();
builder.Services.AddScoped<PEMS.Application.Interfaces.IStorageRouteService, PEMS.Persistence.Services.StorageRouteService>();

builder.Services.AddScoped<IPermissionService, PermissionService>();

builder.Services.AddScoped<IEngineeringDocumentIntelligenceService, EngineeringDocumentIntelligenceService>();




// =========================
// Persistence Repositories
// =========================

builder.Services.AddScoped<
    IEngineeringDocumentRepository,
    EngineeringDocumentRepository>();




// =========================
// AI Services
// =========================

builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("OCR"));

builder.Services.AddScoped<IOcrService, TesseractOcrService>();

builder.Services.AddScoped<ITextExtractor, PdfTextExtractor>();
builder.Services.AddScoped<ITextExtractor, WordTextExtractor>();
builder.Services.AddScoped<ITextExtractor, ExcelTextExtractor>();
builder.Services.AddScoped<ITextExtractor, ImageTextExtractor>();

builder.Services.AddScoped<IDocumentTextExtractorFactory, DocumentTextExtractorFactory>();

builder.Services.AddScoped<IDocumentAIService, DocumentAIService>();

builder.Services.AddScoped<IDocumentAIIngestionService, DocumentAIIngestionService>();

builder.Services.AddScoped<IDocumentSearchAIService, DocumentSearchAIService>();


var ollamaUrl = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(builder.Configuration, "Ollama:Url");
if (!ollamaUrl.EndsWith('/')) ollamaUrl += "/";

var ollamaTimeoutMinutes = PEMS.Web.Configuration.PemsConfigurationExtensions.RequiredDouble(builder.Configuration, "Ollama:TimeoutMinutes");

builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri(ollamaUrl);
    client.Timeout = TimeSpan.FromMinutes(ollamaTimeoutMinutes);
});




var app = builder.Build();
// SCRUM-572: read and check the definition files at start-up, so an invalid file stops the start here and not on the first request that needs it
_ = app.Services.GetRequiredService<PEMS.Application.Definitions.IDefinitionFiles>();




// =========================
// Seed Roles / Default Admin User
// =========================

// Apply pending EF migrations (+ full-text search objects) before any seeder touches the schema.
await PEMS.Persistence.Initialization.DatabaseInitializer.MigrateAsync(app.Services, databaseOptions, databaseLogger);

using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);

    // SCRUM-579: the definitions that ship with the product go into the definition tables (System origin); the engines read the database only. Idempotent: install and upgrade are the same code.
    await PEMS.Persistence.Seed.SystemDefinitionSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS Phase 6B Closure Validation» بند ۳: مکانیزم Development-only برای بازنشانی
    // رمز کاربر admin به مقدار پیش‌فرض مستندشده - فقط وقتی IsDevelopment() و
    // PEMS_DEV_RESET_ADMIN_PASSWORD=true هر دو برقرار باشند (پیش‌فرض خاموش). جزئیات/دروازه‌های
    // ایمنی در DevAdminPasswordReset مستند شده.
    await DevAdminPasswordReset.ResetIfRequestedAsync(
        scope.ServiceProvider, app.Environment, app.Logger);

    // دپارتمان‌های پیش‌فرض
    await PEMS.Persistence.Seed.DepartmentSeeder.SeedAsync(scope.ServiceProvider);

    // قالب‌های آماده‌ی نامه‌ی داخلی (افزودنی و Idempotent)
    await PEMS.Persistence.Seed.LetterTemplateSeeder.SeedAsync(scope.ServiceProvider);

    // طرف‌های مکاتبه از روی وندورها (Idempotent). اگر مایگریشن هنوز اعمال نشده باشد، برنامه به‌خاطر آن بالا نیامدنی نیست.
    try
    {
        await scope.ServiceProvider.GetRequiredService<IIncomingCorrespondenceService>().SeedPartiesFromVendorsAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Correspondent party seeding from vendors was skipped (is the latest migration applied?)");
    }

    // دیسیپلین‌های مهندسی پیش‌فرض - بدون این Seed، پس از پاک‌سازی دیتابیس Workspace دیسیپلین‌ها
    // خالی می‌شوند و UI مربوط به default items/discipline list هیچ آیتمی نشان نمی‌دهد.
    await PEMS.Persistence.Seed.DisciplineSeeder.SeedAsync(scope.ServiceProvider);

    var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
    await permissionService.SeedDefaultsAsync();

    // طبق CMMS-10a ADR-02: پایه‌ی مجوزدهی عمومی Role/Module/Action - فقط برای Permit
    var roleModuleActionService = scope.ServiceProvider.GetRequiredService<IRoleModuleActionService>();
    await roleModuleActionService.SeedDefaultsAsync();

    // Role Catalog: نقش‌های پایه + متادیتا (فقط افزودنی، Idempotent) - باید قبل از SeedFromLegacyAsync
    // اجرا شود تا RolePermission های نقش‌های تازه‌ساخته‌شده به Grant همگام شوند.
    var roleCatalogResult = await PEMS.Persistence.Seed.RoleCatalogSeeder.SeedAsync(scope.ServiceProvider);
    foreach (var (custom, baseline) in roleCatalogResult.PossibleDuplicates)
        app.Logger.LogWarning("Role Catalog: custom role '{Custom}' looks like baseline role '{Baseline}' (not merged).", custom, baseline);

    // P0-09: هر نقشی که روی ماژول‌های خرید/انبار مجوز دارد View هم بگیرد (فقط افزایشی) - قبل از SeedFromLegacyAsync
    await PEMS.Persistence.Seed.ProcurementReadViewSeeder.SeedAsync(scope.ServiceProvider);

    // طبق سند RBAC-ABAC Authorization Architecture (فاز ۱): داده‌ی RolePermission/
    // RoleModuleAction/AspNetUserRoles بالا را به مدل عمومی جدید (Permission/RolePermissionGrant/
    // RoleAssignment) کپی می‌کند - باید بعد از دو Seed بالا اجرا شود
    await PEMS.Persistence.Seed.AccessControlSeeder.SeedFromLegacyAsync(scope.ServiceProvider);

    // طبق سند RBAC-ABAC Authorization Architecture (فاز ۳): دو مجوز جدید معادل آرایه‌های
    // Hard-Code شده‌ی UnrestrictedRoles/ClientResponseRoles که این فاز جایگزین می‌کند
    await PEMS.Persistence.Seed.AccessControlSeeder.SeedPhase3PermissionsAsync(scope.ServiceProvider);

    // طبق فاز ۵ (Governance): سیاست‌های SoD پیش‌فرض برای چند مسیر حساس ساخته می‌شوند تا
    // خودتاییدی و Close/Approve در همان رکورد تا حداقل از سطح داده‌ای جلوگیری شود.
    await PEMS.Persistence.Seed.AccessControlSeeder.SeedPhase5GovernanceAsync(scope.ServiceProvider);

    // Warehouse / Part Catalogue: داده‌های مرجع و گام‌های مسیر (Idempotent، فقط-افزودنی)
    await PEMS.Persistence.Seed.PartCatalogueSeeder.SeedAsync(scope.ServiceProvider);
    // گردش‌کار تأیید کاتالوگ قطعه روی PEMS.WorkflowPlatform + مسئولیت‌های ساختاری (بدون کارمند/Position جعلی)
    await PEMS.Persistence.Seed.PartCatalogueWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    var routingRuleService = scope.ServiceProvider.GetRequiredService<IRoutingRuleService>();
    await routingRuleService.SeedDefaultsAsync();

    // فاز C - Workflow Platform: تعریف گردش‌کار «تایید مدرک مهندسی» را Seed می‌کند
    // (Idempotent) و مدارک از‌قبل‌درجریان را که هنوز Instance ندارند منتقل می‌کند.
    await PEMS.Persistence.Seed.EngineeringDocumentWorkflowSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.EngineeringDocumentWorkflowBackfillService.BackfillAsync(scope.ServiceProvider);

    // گردش‌کار بازبینی VDR (طبق استاندارد جهانی مکاتبات وندور)
    await PEMS.Persistence.Seed.VdrWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // فاز ۱ نقشه‌راه معماری: TQ/TSR/RFI مستقل
    await PEMS.Persistence.Seed.TechnicalQueryWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // فاز ۲ نقشه‌راه معماری: MOC مستقل
    await PEMS.Persistence.Seed.MOCWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // بازنگری CMMS: نقش‌های پیش‌فرض تعمیرونگهداری
    await PEMS.Persistence.Seed.MaintenanceRoleSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.MaintenanceGroupSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.SkillSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.QualificationSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.CertificationSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS — Enterprise Maintenance Master Data & Standards Implementation»: تاکسونومی
    // مرجع نگهداری - ترتیب مهم است (Type/StrategyType/EquipmentClass/StandardReference همه
    // قبل از Activity، چون Activity به کدهای آن‌ها ارجاع می‌دهد)
    await PEMS.Persistence.Seed.MaintenanceTypeSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.MaintenanceStrategyTypeSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.EquipmentClassSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.StandardReferenceSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.ConditionPriorityMappingSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.MaintenanceActivitySeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS Phase 2 — Equipment Classification + ISO 14224 Failure Taxonomy + PM/Job Plan
    // Intelligence»: تاکسونومی خرابی (Cause/Mechanism قبل از پیوند Cause↔Mechanism؛ Mode آخر از
    // همه چون هم به EquipmentClass هم به Effect/Consequence/Cause ارجاع می‌دهد)، سپس PMTemplate
    // (به EquipmentClass/StrategyType/MaintenanceType/StandardReference نیاز دارد - همه قبلاً
    // Seed شده‌اند) و در نهایت Backfill غیرمخرب طبقه‌بندی Equipment های موجود.
    await PEMS.Persistence.Seed.FailureCauseSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.FailureMechanismSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.FailureEffectSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.FailureConsequenceSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.FailureCauseMechanismSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.FailureModeSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.PMTemplateSeeder.SeedAsync(scope.ServiceProvider);

    // طبق بند ۹: Backfill قطعی/غیرمخرب - فقط Equipment هایی که هنوز EquipmentClassId=Null دارند
    {
        var classificationService = scope.ServiceProvider.GetRequiredService<PEMS.Application.Interfaces.IEquipmentClassificationService>();
        await classificationService.BackfillAsync();
    }

    // طبق درخواست: گردش‌کار بازبینی چند-دیسیپلینه WorkOrder
    await PEMS.Persistence.Seed.WorkOrderReviewWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // Phase 2 (Workflow Platform): گردش‌کار Work Request روی پلتفرم مرکزی + Binding صریح (Idempotent؛ Instance های موجود دست نمی‌خورند)
    await PEMS.Persistence.Seed.WorkRequestWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // Phase 3B (Work Order): پایه‌ی حاکمیت (نقش‌ها/مجوزها/دامنه‌ی سازمانی، یک‌بار) سپس تعریف چرخه‌عمر + Binding های صریح (Idempotent).
    // Work Order های Legacy و Instance های موجود دست نمی‌خورند.
    await PEMS.Persistence.Seed.WorkOrderGovernanceSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.WorkOrderLifecycleWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS — Work Order Governance Remediation» F1: ممیزی زنده نشان داد هیچ Department ای با
    // OrganizationDomain='Operations' وجود ندارد - بدون این، گذار Operations Issue/Release برای هیچ
    // کاربری قابل اجرا نیست. Idempotent؛ هیچ Employee واقعی تخصیص داده نمی‌شود.
    await PEMS.Persistence.Seed.OperationsOrganizationSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS — Work Order Governance Remediation» F2: نسخه‌ی دوم گردش‌کار Work Order که Issue/
    // Release را از Start Work جدا می‌کند (Instance های در حال اجرا روی نسخه‌ی قبلی دست‌نخورده می‌مانند).
    await PEMS.Persistence.Seed.WorkOrderLifecycleWorkflowV2Seeder.SeedAsync(scope.ServiceProvider);

    // H28: مجوز جدای بازگشایی Work Order بسته‌شده (Idempotent، فقط افزایشی)
    await PEMS.Persistence.Seed.WorkOrderReopenPermissionSeeder.SeedAsync(scope.ServiceProvider);

    await PEMS.Persistence.Seed.WorkflowManagementSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS Bilingual Help & Knowledge System» - کتابخانه‌ی اولیه‌ی محتوای راهنما (ایدمپوتنت)
    await PEMS.Persistence.Seed.HelpContentSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS Help System — Phase 3» - محتوای بصری راهنما (نمودارهای گردش‌کار زنده + دستی، ایدمپوتنت)
    await PEMS.Persistence.Seed.HelpVisualSeeder.SeedAsync(scope.ServiceProvider);

    // طبق «PEMS Help Phase 4 — Increment 3A» - لایه‌ی دانش مهندسی (Concept/DisciplineGuide/StandardReference، ایدمپوتنت)
    await PEMS.Persistence.Seed.HelpKnowledgeSeeder.SeedAsync(scope.ServiceProvider);

    // بخش QC/QA - دیسیپلین‌های استاندارد بازرسی
    await PEMS.Persistence.Seed.QCDisciplineSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.NcrApprovalWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // طبق ممیزی Material Management (Gap B) + Step 3: زنجیره‌ی تایید Disposition دریافت مادی (IRN)
    await PEMS.Persistence.Seed.IrnDispositionWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // RCM: گردش‌کار تایید استراتژی نگهداری (Draft → InReview → Approved) روی همان موتور مرکزی
    await PEMS.Persistence.Seed.MaintenanceStrategyWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // طبق ممیزی Package Procurement Contract Finalization §D: گردش‌کار نهایی‌سازی قرارداد
    await PEMS.Persistence.Seed.ContractWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // طبق CMMS-10b: زنجیره‌ی تایید Permit to Work
    await PEMS.Persistence.Seed.PermitApprovalWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // SCRUM-472: warehouse intake of work-order materials (task in the warehouse role queue of My Work)
    await PEMS.Persistence.Seed.WarehouseIntakeWorkflowSeeder.SeedAsync(scope.ServiceProvider);
    // SCRUM-471: pre-procurement review (task in the procurement role queue of My Work)
    await PEMS.Persistence.Seed.PreProcurementWorkflowSeeder.SeedAsync(scope.ServiceProvider);
    // SCRUM-470: purchase-order approval by the PO authority table
    await PEMS.Persistence.Seed.PurchaseOrderApprovalWorkflowSeeder.SeedAsync(scope.ServiceProvider);
    // SCRUM-473: Hold resolution of goods receipts (task in the Warehouse Manager queue of My Work)
    await PEMS.Persistence.Seed.IrnHoldResolutionWorkflowSeeder.SeedAsync(scope.ServiceProvider);

    // SCRUM-103: explicit binding for every module workflow (after all workflow seeders), so Workflow Management can edit them
    await PEMS.Persistence.Seed.WorkflowBindingBackfillSeeder.SeedAsync(scope.ServiceProvider);

    // طبق CMMS-10d: حد پذیرش پارامترهای گاز + قاعده‌ی مدت اعتبار تست گاز
    await PEMS.Persistence.Seed.GasTestSeeder.SeedAsync(scope.ServiceProvider);

    // طبق CMMS-10e: قاعده‌ی شدت تعارض SIMOPS بر اساس جفت PermitType
    await PEMS.Persistence.Seed.SimopsSeeder.SeedAsync(scope.ServiceProvider);

    // طبق CMMS-10f: کنترل‌های الزامی اجرا بر اساس نوع Permit
    await PEMS.Persistence.Seed.ExecutionGateSeeder.SeedAsync(scope.ServiceProvider);

    // طبق سند Inventory & Material Management: انبارها از پیشوندهای واقعی MRS، دسته‌بندی و واحد پایه
    await PEMS.Persistence.Seed.WarehouseSeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.MaterialCategorySeeder.SeedAsync(scope.ServiceProvider);
    await PEMS.Persistence.Seed.MaterialUOMSeeder.SeedAsync(scope.ServiceProvider);

    // طبق CMMS Production Baseline v1 بند P0-04: شمارنده‌ی WorkOrder را از روی شماره‌های قدیمی
    // (پیش از NumberingService) Seed می‌کند تا شماره‌ی بعدی با آن‌ها برخورد نکند.
    await PEMS.Persistence.Seed.NumberingBackfillSeeder.SeedAsync(scope.ServiceProvider);
    // SCRUM-40: الگوی فعال پیش‌فرض برای هر سند تدارکاتی که هنوز الگو ندارد (الگوی ویرایش‌شده‌ی مدیر دست نمی‌خورد)
    await PEMS.Persistence.Seed.NumberingSchemeSeeder.SeedAsync(scope.ServiceProvider);

    // سایدبار داده‌محور: همگام‌سازی افزایشی کاتالوگ کد (فقط وقتی پرچم فعال است؛ جدول تا اعمال Migration وجود ندارد)
    if (scope.ServiceProvider.GetRequiredService<PEMS.Web.Navigation.IDynamicSidebarAvailability>().IsEnabled)
    {
        // SCRUM-590: the items shipped as menu definitions join the code defaults; one key is owned by one of them (the code default wins), the same ownership rule applies to both
        var defaults = PEMS.Web.Navigation.NavigationMenuDefaults.All;
        var fed = PEMS.Web.Navigation.MenuDefinitions.From(await scope.ServiceProvider.GetRequiredService<PEMS.Application.Definitions.IDefinitionStore>().ListPublishedAsync("menu"))
            .Where(f => !defaults.Any(d => string.Equals(d.Key, f.Key, StringComparison.OrdinalIgnoreCase))).ToList();
        await scope.ServiceProvider.GetRequiredService<INavigationMenuService>().SyncDefaultsAsync(defaults.Concat(fed).ToList(), default, PEMS.Web.Navigation.NavigationMenuDefaults.RetiredKeys);
    }
}




// =========================
// HTTP Pipeline
// =========================

// ==============================================================================
// طبق «PEMS UAT Deployment» بند ۱۴: هر Host واقعی روی اینترنت (Azure Container Apps/App
// Service/Render/...) درخواست HTTPS را روی یک Reverse Proxy خاتمه می‌دهد و به برنامه با
// پروتکل HTTP داخلی Forward می‌کند - بدون این Middleware، UseHttpsRedirection/UseHsts/کوکی
// امن اشتباهاً فکر می‌کنند درخواست HTTP واقعی است (حلقه‌ی Redirect یا کوکی رد شده).
//
// این‌جا عمداً یک Middleware دستی کوچک است، نه Microsoft.AspNetCore.HttpOverrides.
// UseForwardedHeaders() استاندارد - یک تعارض Assembly از پیش موجود در گراف وابستگی‌های این
// پروژه (همان‌طور که قبلاً در PEMSDbContextFactory با IConfigurationBuilder دیده شد) باعث
// خطای کامپایل CS0234 روی خودِ نوع ForwardedHeadersOptions می‌شد؛ این نسخه‌ی دستی هیچ
// وابستگی‌ای به آن سطح از API ندارد و همان رفتار عملی لازم (تشخیص درست Scheme از
// X-Forwarded-Proto) را تضمین می‌کند. باید همیشه اولین Middleware باشد.
// ==============================================================================
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && proto.Count > 0)
    {
        context.Request.Scheme = proto[0]!;
    }

    await next();
});

// SCRUM-464: one trace id on the response (X-Trace-Id), on every log line and on the error pages; before the exception handler so the error pages carry it.
PEMS.Web.Diagnostics.PemsTrace.UsePemsTraceId(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

// صفحه‌ی خطای دوستانه برای 4xx/5xx بدون بدنه (NotFound() و ...)؛ کد HTTP اصلی حفظ می‌شود و پاسخ‌های دارای بدنه
// (مثلاً BadRequest(new { error })) دست‌نخورده می‌مانند.
// Not for /api: API errors are problem+json bodies (SCRUM-435).
app.UseWhen(ctx => !PEMS.Web.Api.ApiServiceCollectionExtensions.IsApiRequest(ctx.Request.Path),
    branch => branch.UseStatusCodePagesWithReExecute("/Home/StatusPage", "?code={0}"));



app.UseHttpsRedirection();

// طبق ممیزی امنیتی (P0 - Engineering File Security Containment): بند صریح "Direct /uploads URL
// → unavailable" - این یک قفل دفاع-در-عمق است، مستقل از این‌که آیا همه‌ی فایل‌های فیزیکی از
// wwwroot/uploads به ریشه‌ی امن جدید منتقل شده‌اند یا نه. حتی اگر یک فایل به اشتباه در آینده
// دوباره زیر یکی از این پوشه‌ها نوشته شود، این Middleware (قبل از UseStaticFiles) هر درخواست را
// با ۴۰۴ رد می‌کند - UseStaticFiles اصلاً به آن نمی‌رسد.
//
// عمداً فقط پوشه‌های سند کسب‌وکاری/مهندسی (که به IFileStorageService مهاجرت کردند) مسدود
// می‌شوند - نه کل /uploads: wwwroot/uploads/profile-photos و /signatures متعلق به MyProfile
// هستند (عکس/امضای شخصی، نه سند مهندسی محرمانه) و عمداً خارج از محدوده‌ی این وظیفه‌ی امنیتی
// مشخص باقی مانده‌اند (طبق گزارش نهایی) - مسدودکردن کورکورانه‌ی کل /uploads قابلیت نمایش
// عکس/امضای پروفایل را که امروز کار می‌کند می‌شکند.
var blockedUploadFolders = new[]
{
    "/uploads/documents", "/uploads/engineering-comments", "/uploads/vdr", "/uploads/moc",
    "/uploads/technical-queries", "/uploads/letters", "/uploads/mrs-import", "/uploads/intake"
};
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    // SCRUM-202: old letter editor images (/uploads/letters/inline) are served by the protected Files/Image action
    if (blockedUploadFolders.Any(f => path.StartsWithSegments(f, StringComparison.OrdinalIgnoreCase))
        && !path.StartsWithSegments("/uploads/letters/inline", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

// SCRUM-202: nothing under /uploads is ever served as a static file. The few old image URLs that remain
// (/uploads/profile-photos, /uploads/signatures, /uploads/letters/inline) go to the Files/Image action, which needs a login.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseStaticFiles());


// Bilingual Platform: زبان از یک Cookie خوانده می‌شود (پیش‌فرض انگلیسی)؛ سوییچر زبان در
// _Layout.cshtml یک کوکی می‌سازد و صفحه را Reload می‌کند.
//
// مهم: تقویم پیش‌فرض CultureInfo("fa") در دات‌نت، تقویم شمسی (PersianCalendar) است - نه
// میلادی. یعنی بدون این override، به محض سوییچ به فارسی، هر DateTime.ToString()/Parse()ای
// که صریحاً CultureInfo.InvariantCulture را مشخص نکرده باشد (که در سراسر پروژه معمول است،
// مثل Timestamp های AuditLog یا نام فایل‌های Export) خاموش به تقویم شمسی تبدیل می‌شود، چون
// همه‌ی تاریخ‌های دیتابیس میلادی ذخیره شده‌اند. فارسی فقط باید متن/جهت RTL را عوض کند، تقویم
// را نه؛ پس تقویم روی همان CultureInfo("fa") به‌صراحت میلادی نگه داشته می‌شود.
var supportedCultureInfos = (app.Configuration.GetSection("Localization:SupportedCultures").Get<string[]>()
        ?? throw new InvalidOperationException("Missing required setting 'Localization:SupportedCultures' (Localization__SupportedCultures__0 in env/<name>.env)."))
    .Select(name =>
    {
        var culture = new System.Globalization.CultureInfo(name);
        if (culture.TwoLetterISOLanguageName == "fa")
            culture.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
        return culture;
    })
    .ToList();
var defaultCultureName = PEMS.Web.Configuration.PemsConfigurationExtensions.Required(app.Configuration, "Localization:DefaultCulture");
var defaultCulture = supportedCultureInfos.FirstOrDefault(c => c.Name == defaultCultureName)
    ?? throw new InvalidOperationException($"Localization:DefaultCulture '{defaultCultureName}' is not in Localization:SupportedCultures.");

var localizationOptions = new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(defaultCulture, defaultCulture),
    SupportedCultures = supportedCultureInfos,
    SupportedUICultures = supportedCultureInfos
};

localizationOptions.RequestCultureProviders.Insert(0,
    new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());

app.UseRequestLocalization(localizationOptions);

// SCRUM-540: older views still contain literal UI text.  Translate only exact, known resource
// keys at the final HTML boundary so neither culture leaks legacy UI wording while those views
// are migrated to explicit Localizer calls.
app.UseMiddleware<PEMS.Web.Localization.LegacyHtmlLocalizationMiddleware>();


app.UseRouting();

// SCRUM-355/356: فقط endpointهایی که [EnableRateLimiting] دارند (صفحه‌های بازیابی) محدود می‌شوند
app.UseRateLimiter();


// ترتیب مهم است: احراز هویت باید قبل از احراز دسترسی اجرا شود
app.UseAuthentication();

app.UseAuthorization();
PEMS.Web.Definitions.RouteTableExtensions.UseRouteTable(app);   // SCRUM-768: URLs of deleted controllers resolve through the route definitions

// Licence gate (verification point V2): added only when Licensing:Enforce=true.
app.UsePemsLicensing();


// Audit Log: قبل از رسیدن به هر Controller، اطلاعات کاربر جاری را روی PEMSDbContext (که Scoped است)
// می‌گذاریم تا SaveChanges/SaveChangesAsync بتواند آن‌ها را در AuditLog ثبت کند.
app.Use(async (context, next) =>
{
    var dbContext = context.RequestServices.GetRequiredService<PEMS.Persistence.Context.PEMSDbContext>();
    var currentUser = context.RequestServices.GetRequiredService<ICurrentUserService>();

    dbContext.CurrentUserId = currentUser.UserId;
    dbContext.CurrentUserName = currentUser.UserName;

    await next();
});



app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter() }
});


// آدرس‌های قدیمی انبار/مواد → Area «Warehouse» (باید قبل از Routeهای کنترلری ثبت شود)
app.MapWarehouseLegacyRedirects();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
);


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.MapHub<PEMS.Web.Hubs.NotificationHub>("/hubs/notifications");

// OpenAPI 3.1 document, generated from the code and served from this server only (no CDN); signed-in users only.
app.MapOpenApi("/api/v1/openapi.json").RequireAuthorization();

// ==============================================================================
// طبق بند ۲۰: نقطه‌ی Health Check سبک - بدون نیاز به احراز هویت (AuthorizeFilter سراسری فقط
// روی MVC Controllers اعمال می‌شود، نه روی این Endpoint حداقلی Minimal API) و بدون هیچ جزئیات
// حساس (Connection String/نسخه/زیرساخت داخلی) - فقط "Healthy"/"Unhealthy" ساده.
// عمداً از AddHealthChecks()/AddDbContextCheck استاندارد استفاده نشده - همون تعارض Assembly
// که در بالا (UseForwardedHeaders) توضیح داده شد باعث خطای کامپایل روی AddDbContextCheck هم
// می‌شد؛ این‌جا مستقیماً یک Query سبک روی همون PEMSDbContext زده می‌شود.
// ==============================================================================
app.MapGet("/health", async (PEMS.Persistence.Context.PEMSDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Text("Healthy");
    }
    catch
    {
        return Results.Text("Unhealthy", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});



// Anonymous, details-free schema state for deployment pipelines/monitors: 200 only when every shipped
// migration is applied; 503 otherwise (body is just the state name).
app.MapGet("/health/database", async (PEMS.Application.Interfaces.IDatabaseUpdateService updates, PEMS.Web.Definitions.OwnershipPreflightReport ownership) =>
{
    try
    {
        var status = await updates.GetStatusAsync();
        var upToDate = status.State == PEMS.Application.DTOs.DatabaseSetupState.UpToDate;
        // SCRUM-797: an update whose shipped forms would collide with a customer's application field stops before it writes; the form, field and reason are listed here (product form names only, no customer data)
        var conflicts = await ownership.GetAsync();
        var body = conflicts.Count == 0 ? status.State.ToString() : status.State + "\r\n" + string.Join("\r\n", conflicts.Select(c => c.Line));
        return Results.Text(body, statusCode: upToDate && conflicts.Count == 0 ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Text("Unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

// طبق ممیزی امنیتی (P0 - تست‌های امنیتی WebApplicationFactory): کلاس Program تولیدشده از
// Top-Level Statements پیش‌فرض internal است - این Partial Class آن را از پروژه‌ی تست هم قابل‌ارجاع
// می‌کند، بدون تغییر هیچ رفتار اجرایی برنامه
public partial class Program { }
