using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

public class PermissionService : IPermissionService
{
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _accessControlService;

    // ماژول‌های اصلی که فعلاً در Permission Matrix پوشش داده می‌شوند؛
    // ماژول‌های جدید بعداً به همین لیست اضافه می‌شوند
    private static readonly string[] Modules =
    {
        "EngineeringDocuments", "VDR", "Contracts", "Vendors",
        "MaterialRequisition", "Equipment", "DisciplineWorkspace", "DisciplineMaster", "Correspondence", "WorkOrder",
        "StoreRequisition", "WorkRequest", "Inspection", "Transmittal",
        "Task", "FunctionalLocation", "EquipmentCriticality", "JobPlan", "MaintenancePlan",
        "ProcurementPackage",
        "MDRImport", "EquipmentImport", "MrsImport", "FunctionalLocationImport",
        "EquipmentFunctionalLocationAssignment", "MaintenanceHistoryImport", "Barcode",
        "AIExtraction", "TableExtraction", "Relationship", "OCRProcessing",
        "Team", "Crew", "InventoryMasterData", "MaintenanceMasterData",
        "Setup",
        // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱: ماژول مستقل جدید -
        // نامش عمداً "Production" است نه "Warehouse" (که با InventoryMasterData/Inventory موجود
        // تداخل معنایی می‌داشت - طبق سوال باز AQ-7 سند معماری).
        "Production",
        // طبق ممیزی Material Management (Gap A) + Step 2: نام ماژول عمداً "IRN" (تمام‌بزرگ) -
        // هم‌الگو با اختصار موجود "VDR"، نه "Irn"/"IrnReceipt".
        "IRN",
        // طبق ممیزی Material Management Step 1 (یافته: MaterialItemController هیچ بررسی مجوزی
        // نداشت) + Step 3 بخش ۱۴: فقط چون این مسیر می‌تواند گیت QC/Disposition جدید IRN را دور
        // بزند (ReceiveStock مستقیماً OnHandQuantity را بدون هیچ بازرسی افزایش می‌دهد) - نه یک
        // بازطراحی امنیتی گسترده‌ی کل Controller.
        "MaterialItem",
        // طبق ممیزی Package Procurement Contract Finalization (§C): ماژول مستقل برای چرخه‌ی
        // Tender/Bid - جدا از "ProcurementPackage" (که سرصفحه‌ی بسته می‌ماند) و "Contracts"
        // (که نتیجه‌ی نهایی این چرخه است)
        "Tender",
        // طبق ممیزی §H (Phase 7.5): PurchaseOrder Entity/Service/DTO از قبل کامل بود ولی هیچ
        // Controller/View/مجوزی نداشت
        "PurchaseOrder",
        // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۶/۹:
        // بدون این دو خط، FindingController/ConditionReadingImportController فقط برای SystemAdmin
        // کار می‌کردند (از طریق Bypass عمومی AccessControlService) - هیچ نقش دیگری نمی‌توانست
        // این دو ماژول را از طریق Permission Matrix دریافت کند، چون اصلاً در این فهرست نبودند.
        "CMFinding",
        "ConditionReadingImport"
    };

    public PermissionService(PEMSDbContext context, IAccessControlService accessControlService)
    {
        _context = context;
        _accessControlService = accessControlService;
    }

    public async Task<List<RolePermissionDto>> GetMatrixAsync()
    {
        return await _context.RolePermissions
            .OrderBy(x => x.RoleName).ThenBy(x => x.ModuleName)
            .Select(x => new RolePermissionDto
            {
                RolePermissionId = x.RolePermissionId,
                RoleName = x.RoleName,
                ModuleName = x.ModuleName,
                CanView = x.CanView,
                CanCreate = x.CanCreate,
                CanEdit = x.CanEdit,
                CanDelete = x.CanDelete,
                CanApprove = x.CanApprove,
                CanManualNumber = x.CanManualNumber,
                CanFreeze = x.CanFreeze,
                CanUnfreeze = x.CanUnfreeze,
                CanHold = x.CanHold,
                CanReleaseHold = x.CanReleaseHold
            })
            .ToListAsync();
    }

    public async Task SetPermissionsAsync(string roleName, IEnumerable<ModulePermissionInput> permissions)
    {
        var inputs = permissions.ToList();
        var moduleNames = inputs.Select(p => p.ModuleName).ToList();

        var existingByModule = await _context.RolePermissions
            .Where(x => x.RoleName == roleName && moduleNames.Contains(x.ModuleName))
            .ToDictionaryAsync(x => x.ModuleName);

        foreach (var input in inputs)
        {
            if (!existingByModule.TryGetValue(input.ModuleName, out var existing))
            {
                existing = new RolePermission { RoleName = roleName, ModuleName = input.ModuleName, CreatedDate = DateTime.Now };
                _context.RolePermissions.Add(existing);
            }

            existing.CanView = input.CanView;
            existing.CanCreate = input.CanCreate;
            existing.CanEdit = input.CanEdit;
            existing.CanDelete = input.CanDelete;
            existing.CanApprove = input.CanApprove;
            existing.CanManualNumber = input.CanManualNumber;
            existing.CanFreeze = input.CanFreeze;
            existing.CanUnfreeze = input.CanUnfreeze;
            existing.CanHold = input.CanHold;
            existing.CanReleaseHold = input.CanReleaseHold;
        }

        await _context.SaveChangesAsync();

        // طبق «PEMS Authorization Architecture Completion» فاز ۴ بند ۱۳: بدون این، تغییر Checkbox
        // این‌جا فقط RolePermission (ورودی Legacy) را به‌روزرسانی می‌کرد و تا Restart بعدی برنامه
        // به RolePermissionGrant (منبعی که AccessControlService واقعاً می‌خواند) نمی‌رسید.
        await PEMS.Persistence.Seed.AccessControlSeeder.SyncRoleFromRolePermissionsAsync(_context, roleName);
    }

    public async Task<bool> HasPermissionAsync(IEnumerable<string> userRoles, string moduleName, string action)
    {
        // طبق سند RBAC-ABAC Authorization Architecture (فاز ۱): تصمیم واقعی اکنون از
        // AccessControlService (که از RolePermissionGrant/Permission - Seed‌شده از همین
        // RolePermission - می‌خواند) می‌آید، نه مستقیم از RolePermissions این‌جا. رفتار باید
        // عیناً یکسان بماند - اثبات‌شده با AccessControlParityTests. GetMatrixAsync/
        // SetPermissionsAsync/SeedDefaultsAsync دست‌نخورده روی RolePermissions کار می‌کنند؛
        // AccessControlSeeder آن‌ها را به RolePermissionGrant همگام نگه می‌دارد.
        var decision = await _accessControlService.AuthorizeForRolesAsync(userRoles, moduleName, action);
        return decision.Allowed;
    }

    public async Task SeedDefaultsAsync()
    {
        // طبق ممیزی: WorkOrder به‌عنوان ماژول جدید حتی روی دیتابیس‌هایی که از قبل Seed شدن
        // هم باید اضافه بشه - وگرنه با گارد «اگه هر ردیفی هست کلاً رد شو»، هیچ‌وقت اجرا نمی‌شه
        // و همه‌ی کاربران غیر-SystemAdmin از WorkOrder قفل می‌مونن.
        if (!await _context.RolePermissions.AnyAsync(x => x.ModuleName == "WorkOrder"))
        {
            var workOrderDefaults = new List<RolePermission>
            {
                new() { RoleName = SystemRoles.MaintenanceEngineer, ModuleName = "WorkOrder", CanView = true, CanCreate = true, CanEdit = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringSupervisor, ModuleName = "WorkOrder", CanView = true, CanCreate = true, CanEdit = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringManager, ModuleName = "WorkOrder", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.DisciplineEngineer, ModuleName = "WorkOrder", CanView = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.Viewer, ModuleName = "WorkOrder", CanView = true, CreatedDate = DateTime.Now }
            };

            await _context.RolePermissions.AddRangeAsync(workOrderDefaults);
            await _context.SaveChangesAsync();
        }

        // طبق CMMS Production Baseline v1 بند P0-02: StoreRequisition تا امروز هیچ Permission
        // نداشت (هر کاربر وارد شده می‌تونست Approve/Issue کنه). چون هنوز نقش اختصاصی
        // "انباردار" در سیستم تعریف نشده، از همون نقش‌هایی که به WorkOrder دسترسی دارن استفاده
        // می‌شه - می‌شه بعداً از پنل Admin/PermissionMatrix یک نقش اختصاصی ساخت و اینجا اصلاح کرد.
        if (!await _context.RolePermissions.AnyAsync(x => x.ModuleName == "StoreRequisition"))
        {
            var storeRequisitionDefaults = new List<RolePermission>
            {
                new() { RoleName = SystemRoles.MaintenanceEngineer, ModuleName = "StoreRequisition", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringSupervisor, ModuleName = "StoreRequisition", CanView = true, CanCreate = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringManager, ModuleName = "StoreRequisition", CanView = true, CanCreate = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.Viewer, ModuleName = "StoreRequisition", CanView = true, CreatedDate = DateTime.Now }
            };

            await _context.RolePermissions.AddRangeAsync(storeRequisitionDefaults);
            await _context.SaveChangesAsync();
        }

        // طبق CMMS Production Baseline v1 بند P0-05: WorkRequest دروازه‌ی رسمی قبل از ساخت
        // WorkOrder اجرایی است - هر Role ای که به WorkOrder دسترسی داره باید بتونه درخواست
        // بسازه/بررسی کنه هم.
        if (!await _context.RolePermissions.AnyAsync(x => x.ModuleName == "WorkRequest"))
        {
            var workRequestDefaults = new List<RolePermission>
            {
                new() { RoleName = SystemRoles.MaintenanceEngineer, ModuleName = "WorkRequest", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringSupervisor, ModuleName = "WorkRequest", CanView = true, CanCreate = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringManager, ModuleName = "WorkRequest", CanView = true, CanCreate = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.DisciplineEngineer, ModuleName = "WorkRequest", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.Viewer, ModuleName = "WorkRequest", CanView = true, CreatedDate = DateTime.Now }
            };

            await _context.RolePermissions.AddRangeAsync(workRequestDefaults);
            await _context.SaveChangesAsync();
        }

        if (!await _context.RolePermissions.AnyAsync(x => x.ModuleName == "Inspection"))
        {
            var inspectionDefaults = new List<RolePermission>
            {
                new() { RoleName = SystemRoles.MaintenanceEngineer, ModuleName = "Inspection", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.QAManager, ModuleName = "Inspection", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringSupervisor, ModuleName = "Inspection", CanView = true, CanCreate = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringManager, ModuleName = "Inspection", CanView = true, CanCreate = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.Viewer, ModuleName = "Inspection", CanView = true, CreatedDate = DateTime.Now }
            };

            await _context.RolePermissions.AddRangeAsync(inspectionDefaults);
            await _context.SaveChangesAsync();
        }

        // طبق بخش G (Transmittal Governance) ممیزی: ماژول جدید - همان الگوی WorkOrder/Inspection
        // بالا، حتی روی دیتابیس‌های از قبل Seed‌شده هم باید اضافه شود.
        if (!await _context.RolePermissions.AnyAsync(x => x.ModuleName == "Transmittal"))
        {
            var transmittalDefaults = new List<RolePermission>
            {
                // View + Create (Draft) + Delete (Cancel)
                new() { RoleName = SystemRoles.DocumentController, ModuleName = "Transmittal", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringManager, ModuleName = "Transmittal", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.EngineeringSupervisor, ModuleName = "Transmittal", CanView = true, CanCreate = true, CanEdit = true, CanApprove = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.DisciplineEngineer, ModuleName = "Transmittal", CanView = true, CanCreate = true, CreatedDate = DateTime.Now },
                new() { RoleName = SystemRoles.Viewer, ModuleName = "Transmittal", CanView = true, CreatedDate = DateTime.Now }
            };

            await _context.RolePermissions.AddRangeAsync(transmittalDefaults);
            await _context.SaveChangesAsync();
        }

        // ==============================================================================
        // طبق ممیزی فاز ۵ (Permission Seed Audit): این ۸ ماژول قبلاً پشت یک گارد سراسری
        // «اگه هر ردیفی هست کلاً رد شو» بودند که همیشه بعد از بلوک‌های WorkOrder/
        // StoreRequisition/WorkRequest/Inspection/Transmittal بالا اجرا می‌شد - یعنی چون آن
        // بلوک‌ها همیشه اول رکورد اضافه می‌کنند، AnyAsync() همیشه true می‌شد و این ۸ ماژول
        // هرگز Seed نمی‌شدند (نه فقط روی دیتابیس‌های قدیمی - حتی روی یک دیتابیس کاملاً خالی).
        // با تأیید مستقیم روی PEMSDB واقعی: RolePermissions فقط WorkOrder/StoreRequisition/
        // WorkRequest/Inspection/Transmittal را داشت، صفر ردیف برای این ۸ ماژول.
        // مقادیر پیش‌فرض زیر عیناً همانی هستند که در کد قبلاً نوشته شده بودند - این تغییر
        // Authorization جدیدی اختراع نمی‌کند، فقط همون گارد Idempotent هر-ماژول-جدا (دقیقاً
        // همون الگوی WorkOrder/StoreRequisition/... بالا) را روی کدی که قبلاً غیرقابل‌اجرا بود
        // اعمال می‌کند - Additive/Idempotent/Deterministic، بدون تغییر هیچ Grant موجود.
        // ==============================================================================

        static RolePermission New(string role, string module, bool view = true, bool create = false, bool edit = false, bool del = false, bool approve = false, bool manualNumber = false, bool freeze = false, bool unfreeze = false, bool hold = false, bool releaseHold = false) => new()
        {
            RoleName = role,
            ModuleName = module,
            CanView = view,
            CanCreate = create,
            CanEdit = edit,
            CanDelete = del,
            CanApprove = approve,
            CanManualNumber = manualNumber,
            CanFreeze = freeze,
            CanUnfreeze = unfreeze,
            CanHold = hold,
            CanReleaseHold = releaseHold,
            CreatedDate = DateTime.Now
        };

        // ==============================================================================
        // طبق ممیزی فاز ۵C/۶B (Permission Seed Hardening): RolePermissions یک ردیف به‌ازای
        // هر جفت (RoleName, ModuleName) است - همه‌ی Actionها (Create/Edit/Approve/Freeze/...)
        // روی همون یک ردیف به‌صورت ستون Boolean هستند، نه ردیف‌های جدا. گارد قدیمی («اگه هر
        // ردیفی برای این ماژول هست کلاً رد شو») در سطح کل ماژول Idempotent بود، اما اگر فقط یک
        // نقش (مثلاً از طریق پنل Admin/SetPermissionsAsync) از قبل ردیف داشته باشد، این گارد
        // بقیه‌ی نقش‌ها را هم برای همیشه Skip می‌کرد و هرگز ترمیم نمی‌شدند. SeedMissingRoles
        // وجود هر (RoleName, ModuleName) را مستقل بررسی می‌کند: نقش‌های موجود دست‌نخورده
        // می‌مانند (Overwrite نمی‌شوند - Grantهای دستی حفظ می‌شوند)، فقط نقش‌های غایب از
        // همین تعریف‌های Authored اضافه می‌شوند. این متد ابتدا فقط برای EngineeringDocuments
        // (فاز ۵C) نوشته شده بود؛ فاز ۶B همون الگو را روی ۷ ماژول باقی‌مانده هم اعمال می‌کند -
        // مقادیر پیش‌فرض دقیقاً همان‌هایی هستند که قبلاً در این فایل نوشته شده بودند.
        // ==============================================================================
        async Task SeedMissingRolesAsync(string moduleName, List<RolePermission> authoredDefaults)
        {
            var existingRoles = await _context.RolePermissions
                .Where(x => x.ModuleName == moduleName)
                .Select(x => x.RoleName)
                .ToListAsync();

            var missing = authoredDefaults.Where(d => !existingRoles.Contains(d.RoleName)).ToList();

            if (missing.Count > 0)
            {
                await _context.RolePermissions.AddRangeAsync(missing);
                await _context.SaveChangesAsync();
            }
        }

        await SeedMissingRolesAsync("EngineeringDocuments", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "EngineeringDocuments", create: true, edit: true, del: true, approve: true, manualNumber: true, freeze: true, unfreeze: true, hold: true, releaseHold: true),
            New(SystemRoles.EngineeringSupervisor, "EngineeringDocuments", create: true, edit: true, approve: true, hold: true, releaseHold: true),
            New(SystemRoles.DisciplineEngineer, "EngineeringDocuments", create: true, edit: true, hold: true),
            New(SystemRoles.DocumentController, "EngineeringDocuments", create: true, edit: true, manualNumber: true, freeze: true, unfreeze: true, hold: true, releaseHold: true),
            New(SystemRoles.Viewer, "EngineeringDocuments")
        });

        await SeedMissingRolesAsync("VDR", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "VDR", create: true, edit: true, del: true, approve: true),
            New(SystemRoles.DocumentController, "VDR", create: true, edit: true),
            New(SystemRoles.DisciplineEngineer, "VDR", edit: true, approve: true),
            // طبق بند ۱۰ ممیزی: Viewer روی MDR/EquipmentImport/MrsImport از قبل دید فقط‌خواندنی
            // دارد؛ برای این‌که لینک VDR Import هم زیر Administration > Import Center برای همون
            // نقش به یک صفحه‌ی 403 ختم نشود، همون سطح فقط‌خواندنی این‌جا هم اضافه شد.
            New(SystemRoles.Viewer, "VDR")
        });

        await SeedMissingRolesAsync("Contracts", new List<RolePermission>
        {
            New(SystemRoles.ContractManager, "Contracts", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "Contracts", approve: true),
            // طبق ممیزی Package Procurement Contract Finalization (§F، Phase 2.6): هر رشته‌ی
            // بازبینی چند-رشته‌ای فقط Edit می‌گیرد (نه Approve) - می‌تواند تصمیم رشته‌ی خودش را در
            // ContractDisciplineReview ثبت کند (کنترل دقیق‌تر داخل ContractWorkflowService.
            // RecordDisciplineReviewAsync انجام می‌شود: هر کاربر فقط رشته‌ی نقش خودش را می‌تواند
            // ثبت کند). Legal/Finance/HSE نقش‌های تازه‌ای هستند که پیش از این هیچ مجوزی نداشتند.
            New(SystemRoles.QAManager, "Contracts", edit: true),
            New(SystemRoles.Legal, "Contracts", edit: true),
            New(SystemRoles.Finance, "Contracts", edit: true),
            New(SystemRoles.HSE, "Contracts", edit: true)
        });

        await SeedMissingRolesAsync("Vendors", new List<RolePermission>
        {
            New(SystemRoles.ContractManager, "Vendors", create: true, edit: true)
        });

        await SeedMissingRolesAsync("MaterialRequisition", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "MaterialRequisition", create: true, edit: true, approve: true),
            New(SystemRoles.DisciplineEngineer, "MaterialRequisition", create: true, edit: true)
        });

        await SeedMissingRolesAsync("Equipment", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Equipment", create: true, edit: true)
        });

        await SeedMissingRolesAsync("DisciplineWorkspace", new List<RolePermission>
        {
            New(SystemRoles.EngineeringSupervisor, "DisciplineWorkspace", create: true, edit: true),
            New(SystemRoles.DisciplineEngineer, "DisciplineWorkspace", create: true, edit: true),
            // طبق «PEMS — P1 Organization & Master Data Authorization Hardening»: این ماژول از
            // فاز ۶B Seed شده بود اما DisciplineController هرگز واقعاً HasPermissionAsync را
            // صدا نمی‌زد - یعنی این دو نقش تنها نقش‌های "طراحی‌شده" بودند، نه نقش‌های واقعی که
            // امروز لینک سایدبار «Disciplines» را می‌بینند (MenuAccess.EngineeringManagement
            // شامل EngineeringManager/DocumentController/Viewer هم می‌شود). با سیم‌کشی‌کردن این
            // ماژول به Controller، این سه نقش هم اضافه شدند تا آن لینک به یک صفحه‌ی 403 ختم
            // نشود (بند ۲۱-C: «Sidebar visible + Permission denied = BAD»).
            New(SystemRoles.EngineeringManager, "DisciplineWorkspace", create: true, edit: true),
            New(SystemRoles.DocumentController, "DisciplineWorkspace"),
            New(SystemRoles.Viewer, "DisciplineWorkspace")
        });

        // Discipline Master Data (کد/نام/Alias/ترانسمیتال/فعال‌سازی) - مجوز مستقل از DisciplineWorkspace (Task سطح دیسیپلین)؛ نقش‌های Task-محور
        // (DisciplineEngineer/EngineeringSupervisor/...) نباید داده‌ی پایه را ویرایش کنند. فقط EngineeringManager؛ SystemAdmin بایپس دارد.
        await SeedMissingRolesAsync("DisciplineMaster", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "DisciplineMaster", create: true, edit: true)
        });

        // Phase 2 / P5 - توصیه‌های تأمین (تأیید مالک): فقط «مدیر انبار» View+Create+Edit (اجرای محاسبه و رد/بازگشایی). این الگو فقط ردیف نقشِ غایب را اضافه می‌کند و
        // تنظیم دستی مدیر را بازنویسی نمی‌کند. سه نقش دیگرِ تأییدشده (کارمند انبار / تدارکات / برنامه‌ریز نگهداری) تا روشن‌شدن شناسه‌ی دقیق نقش
        // عمداً اینجا نیستند - docs/procurement/P5_REPLENISHMENT_RECOMMENDATIONS.md بخش «Role assignments». هیچ مجوز Approve/Delete، خرید یا رزرو داده نمی‌شود.
        await SeedMissingRolesAsync("ReplenishmentRecommendation", new List<RolePermission>
        {
            New(RoleCatalog.WarehouseManager, "ReplenishmentRecommendation", create: true, edit: true)
        });

        // Correspondence (مکاتبات رسمی)
        await SeedMissingRolesAsync("Correspondence", new List<RolePermission>
        {
            New(SystemRoles.DocumentController, "Correspondence", create: true, edit: true, approve: true, manualNumber: true),
            New(SystemRoles.EngineeringManager, "Correspondence", create: true, edit: true, approve: true, manualNumber: true),
            New(SystemRoles.EngineeringSupervisor, "Correspondence", create: true, edit: true),
            New(SystemRoles.DisciplineEngineer, "Correspondence", create: true),
            New(SystemRoles.ContractManager, "Correspondence", create: true, edit: true)
        });

        // ==============================================================================
        // طبق ممیزی مستقل فاز ۶B Wave A (P0): Task/FunctionalLocation/EquipmentCriticality
        // قبلاً هیچ بررسی مجوزی نداشتند - یعنی هر کاربر احرازهویت‌شده‌ای (بدون توجه به نقش)
        // می‌توانست از آن‌ها استفاده کند. برای این‌که افزودن گیت مجوز، این رفتار فعلی را برای
        // نقش‌های واقعاً درگیر نشکند، همه‌ی نقش‌های غیر-SystemAdmin زیر دریافت کرده‌اند:
        //
        // Task: ابزار عمومی کارتابل شخصی («اقدام وظایف»/«پیگیری وظایف») است - نه یک ماژول
        // دامنه‌محور. GetMyActionItemsAsync/GetAssignedByMeAsync از قبل نتیجه را به همین کاربر
        // محدود می‌کنند (AssignedToEmployeeId/AssignedByApplicationUserId)، پس اعطای گسترده
        // اینجا هیچ داده‌ی بین-کاربری را افشا نمی‌کند. Edit لازم است چون UpdateStatus (خودِ
        // عملکرد اصلی صفحه) به آن گیت شده.
        await SeedMissingRolesAsync("Task", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "Task", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "Task", create: true, edit: true),
            New(SystemRoles.DisciplineEngineer, "Task", create: true, edit: true),
            New(SystemRoles.DocumentController, "Task", create: true, edit: true),
            New(SystemRoles.MaintenanceEngineer, "Task", create: true, edit: true),
            New(SystemRoles.ContractManager, "Task", create: true, edit: true),
            New(SystemRoles.QAManager, "Task", create: true, edit: true),
            New(SystemRoles.Viewer, "Task", create: true, edit: true)
        });

        // FunctionalLocation: داده‌ی مرجع/پایه (سلسله‌مراتب Plant/Area/Unit) که از صفحات
        // Equipment/Asset هم به‌عنوان Picker List خوانده می‌شود - View باید گسترده باشد؛
        // Create/Edit به نقش‌های مسئول ساختار دارایی محدود شده (طبق الگوی ماژول Equipment).
        await SeedMissingRolesAsync("FunctionalLocation", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "FunctionalLocation", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "FunctionalLocation", create: true, edit: true),
            New(SystemRoles.MaintenanceEngineer, "FunctionalLocation", create: true, edit: true),
            New(SystemRoles.DisciplineEngineer, "FunctionalLocation"),
            New(SystemRoles.Viewer, "FunctionalLocation")
        });

        // EquipmentCriticality: تحلیل بحرانی‌بودن (ISO 55000/RCM) - دقیقاً همون نقش‌های
        // ماژول Inspection (نزدیک‌ترین ماژول موجود از نظر دامنه: ارزیابی دوره‌ای/وضعیت تجهیز).
        await SeedMissingRolesAsync("EquipmentCriticality", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "EquipmentCriticality", create: true),
            New(SystemRoles.QAManager, "EquipmentCriticality", create: true),
            New(SystemRoles.EngineeringSupervisor, "EquipmentCriticality", create: true),
            New(SystemRoles.EngineeringManager, "EquipmentCriticality", create: true),
            New(SystemRoles.Viewer, "EquipmentCriticality")
        });

        // ==============================================================================
        // طبق ممیزی مستقل فاز ۶B Wave B1 (نهایی) بند P0: JobPlan/MaintenancePlan قبلاً هیچ
        // بررسی مجوزی نداشتند - همون کلاس نقصی که Task/FunctionalLocation/EquipmentCriticality
        // در Wave A داشتند. نقش‌ها دقیقاً همون‌هایی هستند که برای EquipmentCriticality/
        // Inspection انتخاب شدند - هر سه ماژول از نظر دامنه (اجرای نگهداری/RCM) یکسان‌اند.
        // ==============================================================================
        await SeedMissingRolesAsync("JobPlan", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "JobPlan", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "JobPlan", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "JobPlan", create: true, edit: true),
            New(SystemRoles.Viewer, "JobPlan")
        });

        await SeedMissingRolesAsync("MaintenancePlan", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MaintenancePlan", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "MaintenancePlan", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "MaintenancePlan", create: true, edit: true),
            New(SystemRoles.Viewer, "MaintenancePlan")
        });

        // ==============================================================================
        // ==============================================================================
        // طبق «PEMS Phase 7.6 — Engineering Criticality Assessment & Controlled Maintenance
        // Activation» بند ۱۱: ماژول مجزا از "EquipmentCriticality" موجود (که فقط یک ثبت
        // مستقیم بدون گردش‌کار است) - Create/Edit برای آماده‌سازی/ویرایش DRAFT (MaintenanceEngineer
        // هم مجاز)، Approve فقط برای Supervisor/Manager (تفکیک وظایف - سازنده نمی‌تواند
        // تایید‌کننده باشد).
        // ==============================================================================
        await SeedMissingRolesAsync("CriticalityAssessment", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "CriticalityAssessment", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "CriticalityAssessment", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "CriticalityAssessment", create: true, edit: true, approve: true),
            New(SystemRoles.Viewer, "CriticalityAssessment")
        });

        // طبق «PEMS Phase 7.5 — CMMS Operational Data Readiness» ممیزی: MaintenanceStrategyController
        // هیچ بررسی مجوزی نداشت - هر کاربر احرازهویت‌شده‌ای می‌توانست برای هر تجهیزی (حتی به‌صورت
        // Bulk، برای صدها تجهیز هم‌زمان) استراتژی نگهداری بسازد یا غیرفعال کند؛ دقیقاً همان کلاس
        // خلأ امنیتی که JobPlan/MaintenancePlan/Reliability/RCMFMEA در فازهای قبلی داشتند.
        // BulkCreate فقط برای Supervisor/Manager (Approve) است - چون بند ۴۳ صریحاً فعال‌سازی
        // انبوه/غیرکنترل‌شده را یک ریسک واقعی می‌داند؛ Create تکی برای MaintenanceEngineer هم
        // در دسترس است (آماده‌سازی مستند طبق بند ۱۱).
        // ==============================================================================
        await SeedMissingRolesAsync("MaintenanceStrategy", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MaintenanceStrategy", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "MaintenanceStrategy", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "MaintenanceStrategy", create: true, edit: true, approve: true),
            New(SystemRoles.Viewer, "MaintenanceStrategy")
        });

        // طبق «PEMS — Final Global Repository Audit» بند ۱۹: ProcurementPackageController
        // (شامل AddApproval - ثبت تصمیم Approve/Reject روی زنجیره‌ی MR/MRQ/Vendor Inquiry)
        // هیچ بررسی مجوزی نداشت - هر کاربر احرازهویت‌شده‌ای (صرف‌نظر از نقش) می‌توانست Package
        // بسازد، مرحله را جلو ببرد، و از همه مهم‌تر، تاییدیه‌ی خرید ثبت کند. نقش‌ها دقیقاً همون
        // نقش‌های زنجیره‌ی MaterialRequisition/Contracts هستند (نزدیک‌ترین ماژول‌های موجود از
        // نظر دامنه: خرید/تایید مالی).
        await SeedMissingRolesAsync("ProcurementPackage", new List<RolePermission>
        {
            New(SystemRoles.DisciplineEngineer, "ProcurementPackage", create: true, edit: true),
            New(SystemRoles.ContractManager, "ProcurementPackage", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "ProcurementPackage", approve: true),
            New(SystemRoles.Viewer, "ProcurementPackage")
        });

        // طبق ممیزی Package Procurement Contract Finalization (§C/§F، Phase 3): همون نقش‌های
        // زنجیره‌ی ProcurementPackage/Contracts - چون Tender ادامه‌ی مستقیم همان جریان است
        await SeedMissingRolesAsync("Tender", new List<RolePermission>
        {
            New(SystemRoles.DisciplineEngineer, "Tender", create: true, edit: true),
            New(SystemRoles.ContractManager, "Tender", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "Tender", edit: true),
            New(SystemRoles.QAManager, "Tender", edit: true),
            New(SystemRoles.Finance, "Tender", edit: true),
            New(SystemRoles.Viewer, "Tender")
        });

        // طبق ممیزی §H (Phase 7.5): همون نقش‌های Contracts - PurchaseOrder نتیجه‌ی مستقیم همون
        // مسئولیت است، نه یک حوزه‌ی مجوز جدا
        await SeedMissingRolesAsync("PurchaseOrder", new List<RolePermission>
        {
            New(SystemRoles.ContractManager, "PurchaseOrder", create: true, edit: true),
            New(SystemRoles.Viewer, "PurchaseOrder")
        });

        // ==============================================================================
        // طبق «PEMS — Global Engineering Management UI/UX, Navigation &amp; RBAC Hardening»
        // بند ۹-۱۲/۴۳: MDRImportController/EquipmentImportController/MrsImportController
        // هیچ بررسی مجوزی نداشتند - Commit (که داده‌ی معتبر پروژه‌وار می‌سازد/تغییر می‌دهد) در
        // هر سه برای هر کاربر احرازهویت‌شده‌ای باز بود. الگو دقیقاً همون چیزی است که از قبل
        // روی VDRImportController.Commit پیاده‌سازی شده (RequirePermission("VDR","Approve")) -
        // Upload/Review با "Create"/"View"، Commit با "Approve" (بالاترین ریسک، معادل تصمیم
        // نهایی)، Discard با "Delete" گیت می‌شوند.
        // ==============================================================================
        await SeedMissingRolesAsync("MDRImport", new List<RolePermission>
        {
            New(SystemRoles.DocumentController, "MDRImport", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "MDRImport", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "MDRImport")
        });

        await SeedMissingRolesAsync("EquipmentImport", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "EquipmentImport", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "EquipmentImport", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "EquipmentImport")
        });

        // طبق «PEMS CMMS — Equipment Functional Location Assignment & Import» بند «Authorization»:
        // همون نقش‌هایی که ایمپورت تجهیزات (بالا) و ایمپورت Functional Location (پایین) را
        // مجاز می‌کنند - نه یک سیاست مجوز جدید. ماژول جدا (نه استفاده‌ی مجدد از "EquipmentImport")
        // چون این پایپ‌لاین مفهوماً مستقل است (تخصیص رابطه‌ی موجود، نه ساخت رکورد Equipment جدید) -
        // دقیقاً همون قاعده‌ای که MDRImport/EquipmentImport/MrsImport/FunctionalLocationImport
        // هرکدام ماژول جدای خودشان را دارند، نه یک ماژول Import مشترک.
        await SeedMissingRolesAsync("EquipmentFunctionalLocationAssignment", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "EquipmentFunctionalLocationAssignment", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "EquipmentFunctionalLocationAssignment", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "EquipmentFunctionalLocationAssignment")
        });

        // طبق «PEMS CMMS — Data Migration & Controlled Data Onboarding»: همون نقش‌هایی که
        // بقیه‌ی Importهای CMMS (بالا) را مجاز می‌کنند - نه یک سیاست مجوز جدید. ماژول جدا (نه
        // استفاده‌ی مجدد از "EquipmentImport") چون این پایپ‌لاین مفهوماً مستقل است (رکورد
        // MaintenanceHistory، نه Equipment).
        await SeedMissingRolesAsync("MaintenanceHistoryImport", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MaintenanceHistoryImport", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "MaintenanceHistoryImport", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "MaintenanceHistoryImport")
        });

        // طبق «PEMS — Enterprise Barcode System» بند «Authorization»: همون ۵ عمل ثابت موجود
        // (View/Create/Edit/Delete/Approve) - نه ۷ مجوز جدید Barcode.* دلخواه. نگاشت:
        // View=مشاهده/Resolve، Create=Generate تکی/چاپ/تکرار چاپ، Approve=BulkGenerate (پرریسک‌ترین،
        // دقیقاً همون قاعده‌ی سایر Importها)، Delete=Disable/Revoke (توقف یک هویت فعال).
        await SeedMissingRolesAsync("Barcode", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Barcode", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "Barcode", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "Barcode")
        });

        await SeedMissingRolesAsync("MrsImport", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MrsImport", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "MrsImport", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "MrsImport")
        });

        // طبق ممیزی Material Management (Gap A) + Step 2: همون دو نقشی که به MrsImport
        // (نزدیک‌ترین ماژول موجود از نظر دامنه - دریافت مادی) اعتماد شده، بدون "approve" - چون
        // در Step 2 هیچ گیت QC/Disposition ای وجود ندارد که نیاز به یک ردیف تصمیم مجزا داشته
        // باشد (Submit اینجا دقیقاً هم‌رده‌ی گذار غیر-Approval-Gated «Draft→Submitted» در
        // StoreRequisitionController است، نه یک تصمیم پرریسک). Approve به‌صراحت به Step 3 (طراحی
        // گیت Disposition) موکول شده تا SoD از همون ابتدا درست طراحی شود - نه بعداً روی هم
        // نقشی که Create دارد اضافه شود (طبق درسِ یافته‌ی G9 در ممیزی Step 1).
        await SeedMissingRolesAsync("IRN", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "IRN", create: true),
            New(SystemRoles.EngineeringManager, "IRN", create: true),
            New(SystemRoles.Viewer, "IRN"),
            // طبق ممیزی Material Management (Gap B) + Step 3: طبق شواهد (هیچ نقش اختصاصی
            // Inspector/Warehouse در سیستم نیست؛ QAManager تنها نقش کیفیت-محور موجود است، همون
            // نقشی که NCR/Inspection از قبل به آن اعتماد کرده‌اند)، Edit (ثبت Disposition) و
            // Approve (تایید مستقل QA که مقدار Accepted را به موجودی Post می‌کند) فقط به
            // QAManager داده می‌شود - عمداً نه به EngineeringManager (که "Approve" عمومی مدیریتی
            // در بقیه‌ی ماژول‌ها دارد، نه تصمیم کیفیت). این هم‌پوشانی Edit+Approve روی همون یک نقش
            // دقیقاً همون محدودیت مستندشده‌ی موجود در NCR است (هیچ‌جای کدبیس فعلی قفل خود-تاییدی
            // ندارد) - نه یک نقص جدید، یک الگوی از‌پیش‌پذیرفته‌شده.
            New(SystemRoles.QAManager, "IRN", edit: true, approve: true)
        });

        // طبق ممیزی Material Management Step 1 (یافته امنیتی: MaterialItemController.ReceiveStock/
        // AdjustStock/TransferStock هیچ بررسی مجوزی نداشتند - هر کاربر واردشده می‌توانست مستقیماً
        // OnHandQuantity را تغییر دهد) + Step 3 بخش ۱۴: فقط ReceiveStock این‌جا گیت می‌شود، چون
        // این تنها مسیری است که می‌تواند گیت QC/Disposition جدید IRN را دور بزند (دریافت مستقیم
        // مادی بدون QC). AdjustStock/TransferStock اصلاح/جابجایی موجودی موجود هستند، نه دریافت
        // مادی جدید از بیرون - رفع آن‌ها به یک تغییر امنیتی جدا و کنترل‌شده موکول شده (طبق دستور
        // صریح: بدون بازطراحی امنیتی گسترده در این Step). نقش‌ها هم‌الگو با "InventoryMasterData"
        // موجود (نزدیک‌ترین ماژول قابل‌مقایسه از نظر ریسک مستقیم-موجودی).
        // به‌روزرسانی مستندات (Procurement P0 / F-04): توضیح بالا تاریخی است. اکنون MaterialItemController سمت سرور
        // Create را با MaterialItem.Create و Edit/ToggleActive/AdjustStock/TransferStock را با MaterialItem.Edit گیت می‌کند.
        // مجوز اختصاصی «اصلاح موجودی» و تخصیص آن به نقش‌های انبار (WH.*) تصمیم سیاستی باز است و این Seed تغییر نکرده است.
        await SeedMissingRolesAsync("MaterialItem", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MaterialItem", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "MaterialItem", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "MaterialItem", create: true, edit: true),
            New(SystemRoles.Viewer, "MaterialItem")
        });

        // ==============================================================================
        // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ بند AF (RBAC/SoD Matrix):
        // نقش جدیدی ساخته نشد (طبق قاعده‌ی REUSE > EXTEND > CREATE و دستور صریح بند ۱۹ سند فاز:
        // "audit the existing permission naming convention first"). نگاشت روی همون ۷ فیلد ثابت
        // موجود:
        //   View    = مشاهده Dashboard/سفارش/اجرا/بچ
        //   Create  = ایجاد سفارش/اجرا + ثبت Input/Output/Loss + Post به موجودی + Submit بچ برای QC
        //   Edit    = Plan سفارش (تایید برنامه)
        //   Delete  = Cancel سفارش/اجرا (طبق همون قیاس Barcode: Delete = متوقف‌کردن یک رکورد فعال،
        //             نه حذف فیزیکی)
        //   Approve = گیت‌های پرریسک: Release/Complete/Close سفارش + Release/Reject بچ (QC)
        //   Hold/ReleaseHold = تعلیق/ازسرگیری سفارش، اجرا، و Block/Unblock بچ
        // MaintenanceEngineer عمداً Approve ندارد - جداسازی وظایف (SoD) طبیعی بین «کسی که تولید/
        // ثبت می‌کند» و «کسی که رها/تایید کیفیت می‌کند» (QAManager/EngineeringManager)، دقیقاً هم‌سو
        // با الگوی SoDPolicy موجود (EngineeringDocuments.Create ↔ Approve).
        // ==============================================================================
        await SeedMissingRolesAsync("Production", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Production", create: true, edit: true, hold: true, releaseHold: true),
            New(SystemRoles.QAManager, "Production", approve: true),
            New(SystemRoles.EngineeringManager, "Production", create: true, edit: true, del: true, approve: true, hold: true, releaseHold: true),
            New(SystemRoles.Viewer, "Production")
        });

        // طبق «Functional Location Master Data Import»: همان الگوی EquipmentImport/MDRImport/
        // MrsImport - Upload/Review با Create، Commit (پرریسک‌ترین اکشن) با Approve، Discard با Delete.
        await SeedMissingRolesAsync("FunctionalLocationImport", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "FunctionalLocationImport", create: true, del: true, approve: true),
            New(SystemRoles.EngineeringManager, "FunctionalLocationImport", create: true, del: true, approve: true),
            New(SystemRoles.Viewer, "FunctionalLocationImport")
        });

        // ==============================================================================
        // طبق «PEMS — P0 AI Pipeline Endpoint Hardening»: AIExtractionController/
        // TableExtractionController/RelationshipController/OCRProcessingController هیچ‌کدام
        // بررسی مجوزی نداشتند - Approve/ConfirmRow/BulkConfirm/ConfirmSuggestedLink (که
        // پیشنهاد AI را به داده‌ی معتبر Equipment/Instrument/رابطه‌ی واقعی تبدیل می‌کنند) برای
        // هر کاربر احرازهویت‌شده‌ای باز بودند. EngineeringDocumentIntelligenceController به‌جای
        // ماژول جدید از ماژول موجود "EngineeringDocuments" استفاده می‌کند (همون موجودیت را در
        // مرحله‌ی بعدتر چرخه‌ی عمرش تغییر می‌دهد) - نیازی به Seed جدید ندارد.
        //
        // نقش‌ها بر اساس دامنه‌ی واقعی هر ماژول انتخاب شدند، نه یکسان‌سازی خودکار:
        // - AIExtraction: تگ/مشخصات تجهیز از مدارک - DisciplineEngineer فقط بازبینی می‌کند
        //   (Create)، تایید نهایی (Approve) با مالکان داده‌ی Equipment (MaintenanceEngineer) و
        //   مدیریت (EngineeringManager/EngineeringSupervisor) است - دقیقاً همون قاعده‌ی ماژول
        //   "Equipment" موجود.
        // - TableExtraction: تطبیق جدول I/O با Instrument - کار روزمره‌ی DisciplineEngineer
        //   (رشته‌ی ابزاردقیق) است، پس برخلاف AIExtraction این‌جا Approve هم می‌گیرد.
        // - Relationship: زنجیره‌ی روابط چند-موجودیتی (Equipment/Document/Vendor) - DocumentController
        //   سمت مدرک را Create می‌کند اما Approve نهایی نمی‌دهد (همون قاعده‌ی EngineeringDocuments).
        // - OCRProcessing: صرفاً کنترل صف Background Job (نه تصمیم مهندسی) - محدود به
        //   DocumentController/EngineeringManager، بدون Viewer (طبق ممیزی: این یک ابزار داخلی
        //   سیستمی است، نه یک قابلیت کسب‌وکاری قابل‌مشاهده برای نقش‌های فقط‌خواندنی).
        // ==============================================================================
        await SeedMissingRolesAsync("AIExtraction", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "AIExtraction", create: true, approve: true),
            New(SystemRoles.EngineeringSupervisor, "AIExtraction", create: true, approve: true),
            New(SystemRoles.DisciplineEngineer, "AIExtraction", create: true),
            New(SystemRoles.MaintenanceEngineer, "AIExtraction", create: true, approve: true),
            New(SystemRoles.Viewer, "AIExtraction")
        });

        await SeedMissingRolesAsync("TableExtraction", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "TableExtraction", create: true, approve: true),
            New(SystemRoles.EngineeringSupervisor, "TableExtraction", create: true, approve: true),
            New(SystemRoles.DisciplineEngineer, "TableExtraction", create: true, approve: true),
            New(SystemRoles.MaintenanceEngineer, "TableExtraction", create: true, approve: true),
            New(SystemRoles.Viewer, "TableExtraction")
        });

        await SeedMissingRolesAsync("Relationship", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "Relationship", create: true, approve: true),
            New(SystemRoles.EngineeringSupervisor, "Relationship", create: true, approve: true),
            New(SystemRoles.DisciplineEngineer, "Relationship", create: true, approve: true),
            New(SystemRoles.MaintenanceEngineer, "Relationship", create: true, approve: true),
            New(SystemRoles.DocumentController, "Relationship", create: true),
            New(SystemRoles.Viewer, "Relationship")
        });

        await SeedMissingRolesAsync("OCRProcessing", new List<RolePermission>
        {
            New(SystemRoles.DocumentController, "OCRProcessing", create: true),
            New(SystemRoles.EngineeringManager, "OCRProcessing", create: true)
        });

        // ==============================================================================
        // طبق «PEMS — P1 Organization & Master Data Authorization Hardening»: TeamController/
        // CrewController/InventoryMasterDataController/MaintenanceMasterDataController هیچ‌کدام
        // بررسی مجوزی نداشتند - CreateTeam/AddMember/RemoveMember/MoveMember (که عضویت واحد
        // سازمانی اجرایی را تغییر می‌دهند و روی تخصیص کار اثر می‌گذارند) و Create*/Toggle*Active
        // روی داده‌ی مرجع (Category/UOM/Warehouse/MaintenanceGroup/Skill/Qualification/
        // Certification) برای هر کاربر احرازهویت‌شده‌ای باز بودند.
        //
        // نقش‌ها بر اساس مالکیت واقعی CMMS انتخاب شدند - همون سه نقشی که سایدبار «CMMS
        // Integration»/«Inventory & Materials» از قبل نشانشان می‌دهد (MenuAccess.CmmsIntegration:
        // MaintenanceEngineer/EngineeringSupervisor/EngineeringManager)، نه SystemAdmin-فقط،
        // مطابق بند ۳۷-۳۸ («Master Data لزوماً Admin نیست»).
        // ==============================================================================
        await SeedMissingRolesAsync("Team", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Team", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "Team", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "Team", create: true, edit: true),
            // GetTeamMembers توسط فرم WorkOrder هم مصرف می‌شود - همون سطح View که ماژول
            // WorkOrder به این دو نقش می‌دهد، اینجا هم لازم است (وگرنه دیدن اعضای تیم در
            // بازبینی WorkOrder برایشان می‌شکند).
            New(SystemRoles.DisciplineEngineer, "Team"),
            New(SystemRoles.Viewer, "Team")
        });

        await SeedMissingRolesAsync("Crew", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Crew", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "Crew", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "Crew", create: true, edit: true),
            New(SystemRoles.Viewer, "Crew")
        });

        await SeedMissingRolesAsync("InventoryMasterData", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "InventoryMasterData", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "InventoryMasterData", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "InventoryMasterData", create: true, edit: true),
            New(SystemRoles.Viewer, "InventoryMasterData")
        });

        await SeedMissingRolesAsync("MaintenanceMasterData", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "MaintenanceMasterData", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "MaintenanceMasterData", create: true, edit: true),
            New(SystemRoles.EngineeringManager, "MaintenanceMasterData", create: true, edit: true),
            New(SystemRoles.Viewer, "MaintenanceMasterData")
        });

        // ==============================================================================
        // طبق «PEMS Phase 4 — RCM/FMEA/FMECA Decision Intelligence» بند ۳۷: ماژول مجوز مجزا
        // (نه بازاستفاده از MaintenanceMasterData) چون تایید نهایی یک تحلیل RCM/FMEA یک
        // مسئولیت مهندسی/حاکمیتی متمایز و پرمخاطره‌تر از CRUD ساده‌ی داده‌ی مرجع است - دقیقاً
        // همون تمایزی که پروژه از قبل بین WorkOrder/Inspection (دارای CanApprove) و سایر
        // ماژول‌های ساده‌تر قائل شده. MaintenanceEngineer فقط Draft می‌سازد/ویرایش می‌کند
        // (Editor طبق بند ۳۷)؛ فقط EngineeringSupervisor/EngineeringManager می‌توانند تایید
        // کنند (Reviewer/Approver).
        // ==============================================================================
        await SeedMissingRolesAsync("RCMFMEA", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "RCMFMEA", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "RCMFMEA", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "RCMFMEA", create: true, edit: true, approve: true),
            New(SystemRoles.Viewer, "RCMFMEA")
        });

        // ==============================================================================
        // طبق «PEMS Phase 5 — Final Reliability Intelligence» بند ۴۶: کنترلر ReliabilityController
        // از قبل (پیش از این فاز) هیچ بررسی مجوزی نداشت - همزمان با معرفی این ماژول، همان
        // کنترلر هم سخت‌سازی شد (طبق اصل ثابت این پروژه: «Hidden Navigation != Security»).
        // View برای مشاهده‌ی تحلیل؛ Edit/Approve برای پیکربندی روش‌شناسی آینده (بند ۴۷) - فعلاً
        // هیچ اکشن Mutation ای در این فاز وجود ندارد، ولی ساختار مجوز از الان آماده است.
        // ==============================================================================
        await SeedMissingRolesAsync("Reliability", new List<RolePermission>
        {
            New(SystemRoles.MaintenanceEngineer, "Reliability", create: true, edit: true),
            New(SystemRoles.EngineeringSupervisor, "Reliability", create: true, edit: true, approve: true),
            New(SystemRoles.EngineeringManager, "Reliability", create: true, edit: true, approve: true),
            New(SystemRoles.Viewer, "Reliability")
        });

        // ==============================================================================
        // طبق «PEMS — Quick Setup & Domain Import Architecture Refactoring» بند ۱۰: Quick Setup
        // یک قابلیت سیستمی/سرتاسری است (شامل وضعیت Permissions/AI Administration که هر دو
        // منحصر به SystemAdmin هستند) - نه یک ابزار عملیاتی تک‌دامنه. فقط "View" تعریف شد چون
        // Quick Setup خودش هیچ Mutation ای انجام نمی‌دهد (فقط وضعیت را می‌خواند و به Workflow
        // های دامنه‌ای موجود لینک می‌دهد که هرکدام مجوز خودشان را مستقل بررسی می‌کنند - طبق
        // اصل صریح سند: "QuickSetup.View + Equipment.Import هر دو لازم‌اند، نه فقط یکی").
        // EngineeringManager چون در تمام ماژول‌های دیگر PEMS اختیار نظارتی سرتاسری دارد.
        // ==============================================================================
        await SeedMissingRolesAsync("Setup", new List<RolePermission>
        {
            New(SystemRoles.EngineeringManager, "Setup")
        });
    }
}
