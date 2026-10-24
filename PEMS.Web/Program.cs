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
using Hangfire.Dashboard;
using Serilog;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;


// Structured Logging (طبق تکنولوژی‌های تعیین‌شده در Bible - Volume II): جایگزین Logging پیش‌فرض
// برای عیب‌یابی بهتر در Production - خروجی هم روی Console هم فایل روزانه
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/pems-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();



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
})
.AddViewLocalization(Microsoft.AspNetCore.Mvc.Razor.LanguageViewLocationExpanderFormat.Suffix)
.AddDataAnnotationsLocalization();




// =========================
// Database
// =========================

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

var authLockoutSection = builder.Configuration.GetSection("Authentication:Lockout");
var maxFailedAccessAttempts = authLockoutSection.GetValue<int?>("MaxFailedAccessAttempts") ?? 5;
var defaultLockoutMinutes = authLockoutSection.GetValue<int?>("DefaultLockoutMinutes") ?? 10;
var cookieSecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // سیاست رمز عبور - برای محیط سازمانی داخلی؛ در صورت نیاز سخت‌گیرانه‌تر شود
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = maxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(defaultLockoutMinutes);
    })
    .AddEntityFrameworkStores<PEMSDbContext>()
    .AddDefaultTokenProviders();


// نقش‌های کاربر در Claim کوکی هستند؛ پیش‌فرض Identity فقط هر ۳۰ دقیقه آن‌ها را از دیتابیس تازه می‌کند و تغییر نقش/دسترسی
// در پنل مدیر تا آن زمان (یا خروج/ورود دوباره) در سایدبار همان کاربر دیده نمی‌شد. هر دقیقه تازه‌سازی می‌شود.
builder.Services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Cookie.Name = "PEMS.Auth";
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
    options.ServerDomain = builder.Configuration["Fido2:ServerDomain"] ?? "localhost";
    options.ServerName = builder.Configuration["Fido2:ServerName"] ?? "PEMS";
    options.Origins = (builder.Configuration.GetSection("Fido2:Origins").Get<string[]>() ?? new[] { "http://localhost:5089" }).ToHashSet();
    options.TimestampDriftTolerance = 300000;
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
builder.Services.AddSingleton<IWorkflowActionRegistry>(_ =>
{
    var registry = new WorkflowActionRegistry();
    // اکشن‌های Legacy ماژول‌های موجود (فقط نام؛ بدون اجرای کد) تا Definition های Seed شده معتبر بمانند
    var legacy = new (string Module, string[] Actions)[]
    {
        ("EngineeringDocument", new[] { "Issue" }),
        ("MOC", new[] { "Assess", "Close" }),
        ("TechnicalQuery", new[] { "Confirm", "Reopen", "Respond" }),
        ("VendorDocument", new[] { "AC", "AP", "CO", "NC", "RE", "Issue" }),
    };
    foreach (var (module, actions) in legacy)
        foreach (var a in actions)
            registry.RegisterModuleAction(module, a, WorkflowActionKind.Outcome, "Legacy module action");
    PEMS.Persistence.Workflow.WorkOrderWorkflow.Register(registry);   // Phase 3B: اکشن‌های ماژول WorkOrder (Hold, Comment)
    return registry;
});
// فیلدهای Fact ماژول‌ها (مثلاً Part Catalogue) به گرامر شرط‌ها اضافه می‌شود
builder.Services.AddSingleton<IWorkflowConditionEvaluator>(_ => new WorkflowConditionEvaluator(PEMS.Persistence.Workflow.PartCatalogueWorkflow.Fields));
builder.Services.AddSingleton<IWorkflowEventSink, NoOpWorkflowEventSink>();
builder.Services.AddScoped<IWorkflowReferenceCatalog, PemsWorkflowReferenceCatalog>();
builder.Services.AddScoped<IWorkflowAuthorizationHook, AccessControlWorkflowAuthorizationHook>();
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
    EnforceSegregationOfDuties = bool.TryParse(builder.Configuration["Cmms:WorkRequest:EnforceSegregationOfDuties"], out var enforceSod) ? enforceSod : true
});
builder.Services.AddScoped<IWorkRequestService, WorkRequestService>();

builder.Services.AddScoped<IUserDashboardService, UserDashboardService>();

builder.Services.AddScoped<INotificationService, NotificationService>();
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
builder.Services.AddSingleton<PEMS.Application.Interfaces.IFileStorageService>(
    _ => new PEMS.Infrastructure.FileStorage.LocalFileStorageProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "filestore")));

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


var ollamaUrl = builder.Configuration["Ollama:Url"] ?? "http://localhost:11434/";
if (!ollamaUrl.EndsWith('/')) ollamaUrl += "/";

var ollamaTimeoutMinutes = builder.Configuration.GetValue<double?>("Ollama:TimeoutMinutes") ?? 15;

builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri(ollamaUrl);
    client.Timeout = TimeSpan.FromMinutes(ollamaTimeoutMinutes);
});




var app = builder.Build();




// =========================
// Seed Roles / Default Admin User
// =========================

using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);

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

    // سایدبار داده‌محور: همگام‌سازی افزایشی کاتالوگ کد (فقط وقتی پرچم فعال است؛ جدول تا اعمال Migration وجود ندارد)
    if (scope.ServiceProvider.GetRequiredService<PEMS.Web.Navigation.IDynamicSidebarAvailability>().IsEnabled)
        await scope.ServiceProvider.GetRequiredService<INavigationMenuService>().SyncDefaultsAsync(PEMS.Web.Navigation.NavigationMenuDefaults.All, default, PEMS.Web.Navigation.NavigationMenuDefaults.RetiredKeys);
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

// صفحه‌ی خطای دوستانه برای 4xx/5xx بدون بدنه (NotFound() و ...)؛ کد HTTP اصلی حفظ می‌شود و پاسخ‌های دارای بدنه
// (مثلاً BadRequest(new { error })) دست‌نخورده می‌مانند.
app.UseStatusCodePagesWithReExecute("/Home/StatusPage", "?code={0}");



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
    if (blockedUploadFolders.Any(f => path.StartsWithSegments(f, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles();


// Bilingual Platform: زبان از یک Cookie خوانده می‌شود (پیش‌فرض انگلیسی)؛ سوییچر زبان در
// _Layout.cshtml یک کوکی می‌سازد و صفحه را Reload می‌کند.
//
// مهم: تقویم پیش‌فرض CultureInfo("fa") در دات‌نت، تقویم شمسی (PersianCalendar) است - نه
// میلادی. یعنی بدون این override، به محض سوییچ به فارسی، هر DateTime.ToString()/Parse()ای
// که صریحاً CultureInfo.InvariantCulture را مشخص نکرده باشد (که در سراسر پروژه معمول است،
// مثل Timestamp های AuditLog یا نام فایل‌های Export) خاموش به تقویم شمسی تبدیل می‌شود، چون
// همه‌ی تاریخ‌های دیتابیس میلادی ذخیره شده‌اند. فارسی فقط باید متن/جهت RTL را عوض کند، تقویم
// را نه؛ پس تقویم روی همان CultureInfo("fa") به‌صراحت میلادی نگه داشته می‌شود.
var faCulture = new System.Globalization.CultureInfo("fa");
faCulture.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
var enCulture = new System.Globalization.CultureInfo("en");
var supportedCultureInfos = new List<System.Globalization.CultureInfo> { enCulture, faCulture };

var localizationOptions = new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(enCulture, enCulture),
    SupportedCultures = supportedCultureInfos,
    SupportedUICultures = supportedCultureInfos
};

localizationOptions.RequestCultureProviders.Insert(0,
    new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());

app.UseRequestLocalization(localizationOptions);


app.UseRouting();


// ترتیب مهم است: احراز هویت باید قبل از احراز دسترسی اجرا شود
app.UseAuthentication();

app.UseAuthorization();


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



app.Run();

// طبق ممیزی امنیتی (P0 - تست‌های امنیتی WebApplicationFactory): کلاس Program تولیدشده از
// Top-Level Statements پیش‌فرض internal است - این Partial Class آن را از پروژه‌ی تست هم قابل‌ارجاع
// می‌کند، بدون تغییر هیچ رفتار اجرایی برنامه
public partial class Program { }
