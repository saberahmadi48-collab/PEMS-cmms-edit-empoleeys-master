using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;
using PEMS.Web.Models;
using PEMS.WorkflowPlatform.Interfaces;
using System.Diagnostics;

namespace PEMS.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly PEMSDbContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly PEMS.Application.Interfaces.IUserDashboardService _dashboardService;
        private readonly PEMS.Web.Security.ICurrentUserService _currentUser;
        private readonly PEMS.Application.Interfaces.IRoleModuleActionService _roleModuleActionService;
        private readonly PEMS.Application.Interfaces.IPermissionService _permissionService;
        private readonly PEMS.Application.Interfaces.ISetupService _setupService;
        private readonly PEMS.Application.Interfaces.IAccessControlService _accessControlService;
        private readonly PEMS.Application.Interfaces.IMyWorkService _myWork;


        public HomeController(PEMSDbContext context, ILogger<HomeController> logger,
            PEMS.Application.Interfaces.IUserDashboardService dashboardService,
            PEMS.Web.Security.ICurrentUserService currentUser,
            PEMS.Application.Interfaces.IRoleModuleActionService roleModuleActionService,
            PEMS.Application.Interfaces.IPermissionService permissionService,
            PEMS.Application.Interfaces.ISetupService setupService,
            PEMS.Application.Interfaces.IAccessControlService accessControlService,
            PEMS.Application.Interfaces.IMyWorkService myWork)
        {
            _context = context;
            _logger = logger;
            _dashboardService = dashboardService;
            _currentUser = currentUser;
            _roleModuleActionService = roleModuleActionService;
            _permissionService = permissionService;
            _setupService = setupService;
            _accessControlService = accessControlService;
            _myWork = myWork;
        }


        public async Task<IActionResult> Index()
        {
            // اگر Migration جدید هنوز روی دیتابیس اعمال نشده باشد، برخی جداول ممکن است وجود نداشته باشند.
            // به‌جای کرش کردن کل Dashboard، نام جدول‌های مشکل‌دار اینجا جمع‌آوری و به کاربر هشدار داده می‌شود.
            var failedTables = new List<string>();

            var model = new DashboardViewModel
            {
                ProjectsCount = await SafeCountAsync(() => _context.Projects.CountAsync(x => x.IsActive), "Projects", failedTables),

                DocumentsCount = await SafeCountAsync(() => _context.EngineeringDocuments.CountAsync(), "EngineeringDocuments", failedTables),

                PendingApprovalCount = await SafeCountAsync(() => _context.EngineeringDocuments
                    .CountAsync(DashboardDefinitions.PendingApproval), "EngineeringDocuments", failedTables),

                // D20: تعریف‌ها در DashboardDefinitions (تنها منبع)؛ Rejected = رد واقعی، نه «بازگشته با کامنت»
                RejectedCount = await SafeCountAsync(() => _context.EngineeringDocuments
                    .CountAsync(DashboardDefinitions.Rejected), "EngineeringDocuments", failedTables),

                ReturnedWithCommentsCount = await SafeCountAsync(() => _context.EngineeringDocuments
                    .CountAsync(DashboardDefinitions.ReturnedWithComments), "EngineeringDocuments", failedTables),

                ActiveContractsCount = await SafeCountAsync(() => _context.Contracts
                    .CountAsync(DashboardDefinitions.ActiveContract), "Contracts", failedTables),

                OpenRevisionsCount = await SafeCountAsync(() => _context.DocumentRevisions
                    .CountAsync(x => x.IsActive && x.Status != "Approved"), "DocumentRevisions", failedTables),

                OpenWorkOrdersCount = await SafeCountAsync(() => _context.WorkOrders
                    .CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Completed"), "WorkOrders", failedTables),

                AIAlertsCount = await SafeCountAsync(() => _context.DocumentAttachments
                    .CountAsync(x => x.AIStatus == "Failed"), "DocumentAttachments", failedTables),

                EquipmentCount = await SafeCountAsync(() => _context.Equipments.CountAsync(), "Equipments", failedTables),

                OpenMRCount = await SafeCountAsync(() => _context.MaterialRequisitions
                    .CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Cancelled"), "MaterialRequisitions", failedTables),

                TransmittalsCount = await SafeCountAsync(() => _context.Transmittals.CountAsync(x => x.IsActive), "Transmittals", failedTables),
            };


            try
            {
                model.DocumentsByStatus = await _context.EngineeringDocuments
                    .GroupBy(x => x.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Status, x => x.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خواندن آمار وضعیت مدارک برای Dashboard ناموفق بود");
                model.DocumentsByStatus = new Dictionary<string, int>();
                failedTables.Add("EngineeringDocuments (status breakdown)");
            }


            if (failedTables.Count > 0)
            {
                ViewBag.DashboardWarning =
                    "برخی از جداول جدید هنوز در دیتابیس ایجاد نشده‌اند (Migration اعمال نشده است): " +
                    string.Join(", ", failedTables.Distinct()) +
                    ". لطفاً این دستورات را اجرا کنید: " +
                    "dotnet ef migrations add AddEngineeringAndAIModules --project PEMS.Persistence --startup-project PEMS.Web " +
                    "سپس dotnet ef database update --project PEMS.Persistence --startup-project PEMS.Web";
            }


            // ===== Personalized Dashboard Widgets (طبق بازبینی معماری، بند ۲) =====
            // این بخش کاملاً افزودنی است و به منطق بالای همین متد کاری ندارد.
            if (_currentUser.UserId.HasValue)
            {
                var widgetLayout = await _dashboardService.GetLayoutAsync(_currentUser.UserId.Value);
                ViewBag.WidgetLayout = widgetLayout.Where(x => x.IsVisible).OrderBy(x => x.DisplayOrder).ToList();

                var widgetData = new Dictionary<string, object>();

                foreach (var w in (List<PEMS.Application.DTOs.UserDashboardWidgetDto>)ViewBag.WidgetLayout)
                {
                    try
                    {
                        widgetData[w.WidgetKey] = w.WidgetKey switch
                        {
                            "PendingDocuments" => await _context.EngineeringDocuments.CountAsync(x => x.IsActive && (x.Status == "Submitted" || x.Status == "Checked")),
                            "OverdueLetters" => await _context.LetterReferrals.CountAsync(x => x.Status != "Completed" && x.DueDate.HasValue && x.DueDate.Value < DateTime.Now),
                            "RecentDocuments" => await _context.EngineeringDocuments.Where(x => x.IsActive).OrderByDescending(x => x.CreatedDate).Take(5)
                                .Select(x => x.DocumentNo + " - " + x.Title).ToListAsync(),
                            "VDRSummary" => await _context.VDRItems.CountAsync(x => x.IsActive && x.WorkflowStatus != "Closed"),
                            "IntakeSummary" => await _context.DocumentAttachments.CountAsync(x => x.IntakeStage != null && x.IntakeStage != "Routed" && x.IntakeStage != "Rejected"),
                            "MyReferrals" => await _context.LetterReferrals
                                .Where(r => r.Status != "Completed" && _context.Users.Any(u => u.Id == _currentUser.UserId && u.EmployeeId == r.ReferredToEmployeeId))
                                .CountAsync(),
                            _ => (object)0
                        };
                    }
                    catch
                    {
                        widgetData[w.WidgetKey] = 0;
                    }
                }

                ViewBag.WidgetData = widgetData;
            }
            else
            {
                ViewBag.WidgetLayout = new List<PEMS.Application.DTOs.UserDashboardWidgetDto>();
                ViewBag.WidgetData = new Dictionary<string, object>();
            }


            // ===== My Work (طبق PEMS-UI-04): "چه چیزی نیاز به اقدام من دارد" - افزودنی محض،
            // به منطق بالای همین متد کاری ندارد. هر شکست این‌جا نباید کل Dashboard را بشکند
            // (همون الگوی SafeCountAsync). =====
            try
            {
                if (_currentUser.UserId.HasValue)
                {
                    var employeeId = await _context.Users.Where(u => u.Id == _currentUser.UserId.Value)
                        .Select(u => u.EmployeeId).FirstOrDefaultAsync();

                    if (employeeId.HasValue)
                    {
                        var summary = new MyWorkSummary();

                        // کارتابل مشترک گردش‌کار (همان منبعی که موتور هنگام اقدام اعمال می‌کند) - یک‌بار خوانده می‌شود و هم برای Permit
                        // هم برای «سایر تأییدها» استفاده می‌شود؛ شمارش Permit عیناً مثل قبل است (Module == "Permit").
                        PEMS.Application.DTOs.MyApprovalsSummaryDto approvals;
                        try { approvals = await _myWork.GetSummaryAsync(employeeId.Value, _currentUser.Roles); }
                        catch (Exception ex)
                        {
                            // شکست کارتابل نباید بقیه‌ی دسته‌های My Work (Work Order/Referral/مدارک) را از بین ببرد
                            _logger.LogWarning(ex, "خواندن کارتابل گردش‌کار برای My Work ناموفق بود");
                            approvals = new PEMS.Application.DTOs.MyApprovalsSummaryDto();
                        }

                        var canApprovePermits = await _roleModuleActionService.HasActionAsync(_currentUser.Roles, "Permit", "Approve")
                            || await _roleModuleActionService.HasActionAsync(_currentUser.Roles, "Permit", "Reject");
                        if (canApprovePermits)
                        {
                            summary.ShowPermitApprovals = true;
                            summary.PendingPermitApprovals = approvals.ByModule.GetValueOrDefault("Permit");
                        }

                        if (await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "View"))
                        {
                            summary.ShowWorkOrders = true;
                            summary.MyWorkOrdersCount = await _context.WorkOrders.CountAsync(x =>
                                x.IsActive && x.AssignedEmployeeId == employeeId.Value && x.Status != "Closed" && x.Status != "Completed");
                        }

                        summary.ShowReferrals = true;
                        summary.MyReferralsCount = await _context.LetterReferrals.CountAsync(r =>
                            r.Status != "Completed" && r.ReferredToEmployeeId == employeeId.Value);

                        // طبق «PEMS Phase 2A — EDMS Foundation» بخش ۳: عیناً همون قانون
                        // دیسیپلین EngineeringDocumentsController.ScopeToOwnDisciplineIfRestrictedAsync -
                        // فقط دو Query هدفمند (نه بارگذاری/فیلتر همه‌ی مدارک در حافظه)
                        var canViewAllDisciplines = (await _accessControlService.AuthorizeForRolesAsync(
                            _currentUser.Roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;

                        summary.ShowDocumentReviews = true;
                        if (canViewAllDisciplines)
                        {
                            summary.PendingDocumentReviewsCount = await _context.EngineeringDocuments.CountAsync(x =>
                                x.IsActive && (x.Status == "Submitted" || x.Status == "Checked"));
                        }
                        else
                        {
                            var myDisciplineId = await _context.Employees
                                .Where(e => e.EmployeeId == employeeId.Value)
                                .Select(e => (int?)e.DisciplineId)
                                .FirstOrDefaultAsync();

                            // طبق رفتار دقیق فعلی: کارمند بدون دیسیپلین محدود به مدارک
                            // بدون‌دیسیپلین می‌ماند - نه نامحدود، نه صفر
                            summary.PendingDocumentReviewsCount = await _context.EngineeringDocuments.CountAsync(x =>
                                x.IsActive && x.Status == "IDC Review" && (x.DisciplineId == myDisciplineId || x.DisciplineId == null));
                        }

                        // سایر ماژول‌های دارای گردش‌کار (کاتالوگ قطعات، MOC، پرسش فنی، VDR، قرارداد، NCR، IRN، دستور کار ...) که قبلاً در My Work دیده نمی‌شدند.
                        // Permit و مدارک مهندسی کارت اختصاصی خودشان را دارند و اینجا دوباره شمرده نمی‌شوند.
                        var otherModules = approvals.ByModule.Where(kv => kv.Key != "Permit" && kv.Key != "EngineeringDocument").ToList();
                        summary.OtherApprovalsCount = otherModules.Sum(kv => kv.Value);
                        summary.ShowOtherApprovals = summary.OtherApprovalsCount > 0;

                        ViewBag.MyWork = summary;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "محاسبه‌ی بخش My Work برای Dashboard ناموفق بود");
                ViewBag.MyWork = null;
            }


            // ===== Setup Status Card (طبق «PEMS — Quick Setup & Domain Import Architecture
            // Refactoring» بند ۱۴): افزودنی محض، به منطق بالای همین متد کاری ندارد. فقط برای
            // کاربرانی نشان داده می‌شود که مجوز Setup/View دارند - نه هر کاربر احرازهویت‌شده‌ای،
            // چون این اطلاعات (وضعیت آمادگی کل سیستم) برای نقش‌های عملیاتی معنادار نیست. =====
            try
            {
                if (await _permissionService.HasPermissionAsync(_currentUser.Roles, "Setup", "View"))
                {
                    ViewBag.SetupStatus = await _setupService.GetStatusAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "محاسبه‌ی وضعیت Setup برای Dashboard ناموفق بود");
                ViewBag.SetupStatus = null;
            }


            // ===== ساخت لینک‌های سریع برای KPI کارت‌ها =====
            model.KpiCards = new List<KpiCardViewModel>
            {
                new KpiCardViewModel { Title = "Active Projects", Count = model.ProjectsCount, IconClass = "bi-kanban", ColorClass = "bg-icon-blue", Url = Url.Action("Index", "Projects") },
                new KpiCardViewModel { Title = "Engineering Documents", Count = model.DocumentsCount, IconClass = "bi-file-earmark-text", ColorClass = "bg-icon-slate", Url = Url.Action("Index", "EngineeringDocuments") },
                new KpiCardViewModel { Title = "Pending Approval", Count = model.PendingApprovalCount, IconClass = "bi-hourglass-split", ColorClass = "bg-icon-orange", Url = Url.Action("Index", "EngineeringDocuments", new { status = "Submitted,Checked" }),
                    Hint = "Documents currently waiting for action in the internal workflow (status Submitted or Checked)." },
                new KpiCardViewModel { Title = "Rejected Documents", Count = model.RejectedCount, IconClass = "bi-x-circle", ColorClass = "bg-icon-red", Url = Url.Action("Index", "EngineeringDocuments", new { status = "Rejected" }),
                    Hint = "Truly rejected: status Rejected with client response RE (or an internal rejection). The linked list shows every Rejected-status document, including those returned with comments." },
                new KpiCardViewModel { Title = "Returned With Comments (CO/AC)", Count = model.ReturnedWithCommentsCount, IconClass = "bi-chat-left-text", ColorClass = "bg-icon-orange", Url = Url.Action("Index", "EngineeringDocuments", new { status = "Rejected" }),
                    Hint = "Client response CO (commented) or AC (approved with comments): the document must be revised and resubmitted, but it was not rejected." },
                new KpiCardViewModel { Title = "Open Revisions", Count = model.OpenRevisionsCount, IconClass = "bi-clock-history", ColorClass = "bg-icon-purple", Url = Url.Action("Index", "DocumentRevisions") },
                new KpiCardViewModel { Title = "Open MR / MRQ", Count = model.OpenMRCount, IconClass = "bi-cart-check", ColorClass = "bg-icon-teal", Url = Url.Action("Index", "MaterialRequisition") },
                
                // D19: دیگر Placeholder نیست؛ قراردادهای در اجرا (Effective + Active میراثی)
                new KpiCardViewModel { Title = "Active Contracts", Count = model.ActiveContractsCount, IconClass = "bi-file-earmark-ruled", ColorClass = "bg-icon-slate", Url = Url.Action("Index", "Contract"),
                    Hint = "Contracts in execution: status Effective (or the legacy Active status)." },

                new KpiCardViewModel { Title = "Open Work Orders (CMMS)", Count = model.OpenWorkOrdersCount, IconClass = "bi-clipboard-check", ColorClass = "bg-icon-teal", Url = Url.Action("Index", "WorkOrder") },
                new KpiCardViewModel { Title = "AI Alerts (Failed Analysis)", Count = model.AIAlertsCount, IconClass = "bi-robot", ColorClass = "bg-icon-red", Url = Url.Action("Index", "Intake") },
                new KpiCardViewModel { Title = "Registered Equipment", Count = model.EquipmentCount, IconClass = "bi-gear", ColorClass = "bg-icon-green", Url = Url.Action("Index", "Equipment") },
                new KpiCardViewModel { Title = "Transmittals", Count = model.TransmittalsCount, IconClass = "bi-send", ColorClass = "bg-icon-purple", Url = Url.Action("Index", "Transmittal") }
            };


            // D19: «Discipline Performance» دیگر Placeholder نیست - نمای مدارک به‌تفکیک دیسیپلین (بزرگ‌ترین‌ها)
            try
            {
                var rows = await _context.EngineeringDocuments.Where(x => x.DisciplineId != null)
                    .GroupBy(x => x.DisciplineId)
                    .Select(g => new { DisciplineId = g.Key, Total = g.Count(), Approved = g.Count(x => x.Status == "Approved"),
                        Pending = g.Count(x => x.Status == "Submitted" || x.Status == "Checked") })
                    .OrderByDescending(x => x.Total).Take(8).ToListAsync();
                var ids = rows.Select(r => r.DisciplineId).ToList();
                var names = await _context.Disciplines.Where(d => ids.Contains((int?)d.DisciplineId)).ToDictionaryAsync(d => d.DisciplineId, d => d.Name);
                ViewBag.DisciplinePerformance = rows.Select(r => new DisciplinePerformanceRow(
                    r.DisciplineId.HasValue && names.TryGetValue(r.DisciplineId.Value, out var n) ? n : "#" + r.DisciplineId, r.Total, r.Approved, r.Total - r.Approved, r.Pending)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "محاسبه‌ی عملکرد دیسیپلین‌ها برای Dashboard ناموفق بود");
                ViewBag.DisciplinePerformance = new List<DisciplinePerformanceRow>();
            }

            return View(model);
        }


        // اجرای امن یک کوئری شمارش: اگر جدول مربوطه هنوز در دیتابیس وجود نداشته باشد
        // (مثلاً چون Migration جدید هنوز اعمال نشده)، به‌جای کرش کل صفحه، صفر برمی‌گرداند
        // و نام جدول را برای نمایش هشدار در Dashboard ثبت می‌کند.
        private async Task<int> SafeCountAsync(Func<Task<int>> query, string tableNameForLog, List<string> failedTables)
        {
            try
            {
                return await query();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "خواندن آمار جدول {Table} برای Dashboard ناموفق بود (احتمالاً Migration اعمال نشده)", tableNameForLog);
                failedTables.Add(tableNameForLog);
                return 0;
            }
        }


        public IActionResult Privacy()
        {
            return View();
        }


        // صفحه‌ی دوستانه برای پاسخ‌های 4xx/5xx بدون بدنه (مثلاً NotFound()): Program.cs آن را با
        // UseStatusCodePagesWithReExecute صدا می‌زند و کد HTTP اصلی حفظ می‌شود. بدون احراز هویت هم باید باز شود
        // (وگرنه 404 برای کاربر ناشناس به Login منحرف می‌شد).
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult StatusPage(int code = 500)
        {
            if (code < 400 || code > 599) code = 500;
            return View(new StatusPageViewModel { Code = code, RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}