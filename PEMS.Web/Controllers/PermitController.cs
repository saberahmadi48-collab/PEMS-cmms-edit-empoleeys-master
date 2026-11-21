using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق CMMS-10a Execution Baseline: Core Permit Lifecycle. مجوزدهی از طریق
/// IRoleModuleActionService (ADR-02) انجام می‌شود، نه IPermissionService موجود - چون Permit
/// عمل‌هایی فراتر از View/Create/Edit/Delete/Approve دارد (Issue/Suspend/Resume/Revoke...).
///
/// طبق CMMS-10b: گذار Submitted→UnderReview و کل زنجیره‌ی UnderReview→Approved/Rejected/Draft
/// دیگر از طریق AdvanceStatus عمومی انجام نمی‌شود - این‌جا SubmitForApproval زنجیره‌ی تایید
/// واقعی (IWorkflowRuntime) را شروع می‌کند، و RespondApproval/ReturnForRework نتیجه‌ی هر
/// مرحله را ثبت می‌کنند. بقیه‌ی گذارها (Draft→Submitted، Approved→Issued، Suspend/Resume/
/// Close/Cancel/Revoke) دقیقاً مثل CMMS-10a دست‌نخورده مانده‌اند.
/// </summary>
public class PermitController : Controller
{
    private readonly IPermitService _service;
    private readonly IRoleModuleActionService _roleModuleActionService;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IFunctionalLocationService _functionalLocationService;
    private readonly IWorkflowRuntime _runtime;
    private readonly IIsolationService _isolationService;
    private readonly IGasTestService _gasTestService;
    private readonly ISimopsService _simopsService;
    private readonly IExecutionGateService _executionGateService;
    private readonly IPermitAuthorityService _permitAuthorityService;

    private const string BusinessObjectType = PEMS.Persistence.Seed.PermitApprovalWorkflowSeeder.BusinessObjectType;

    private static readonly Dictionary<string, string> NextStatusMap = new()
    {
        ["Draft"] = "Submitted",
        ["Approved"] = "Issued",
        ["Issued"] = "Active",
        ["Suspended"] = "Active",
    };

    public PermitController(IPermitService service, IRoleModuleActionService roleModuleActionService,
        PEMSDbContext context, ICurrentUserService currentUser, IFunctionalLocationService functionalLocationService,
        IWorkflowRuntime runtime, IIsolationService isolationService, IGasTestService gasTestService,
        ISimopsService simopsService, IExecutionGateService executionGateService,
        IPermitAuthorityService permitAuthorityService)
    {
        _service = service;
        _roleModuleActionService = roleModuleActionService;
        _context = context;
        _currentUser = currentUser;
        _functionalLocationService = functionalLocationService;
        _runtime = runtime;
        _isolationService = isolationService;
        _gasTestService = gasTestService;
        _simopsService = simopsService;
        _executionGateService = executionGateService;
        _permitAuthorityService = permitAuthorityService;
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _roleModuleActionService.HasActionAsync(_currentUser.Roles, "Permit", "View"))
            return Forbid();

        var permit = await _service.GetDetailsAsync(id);
        if (permit == null) return NotFound();

        ViewData["Title"] = "Permit Details";
        ViewData["Section"] = "CMMS Integration";
        ViewData["HelpKey"] = "Permit.Details";

        ViewBag.NextStatus = NextStatusMap.TryGetValue(permit.Status, out var next) ? next : null;

        // طبق CMMS-10b: نمایش پیشرفت زنجیره‌ی تایید واقعی (اگر شروع شده باشد)
        var instance = await _runtime.GetInstanceAsync(BusinessObjectType, id);
        ViewBag.ApprovalInstance = instance;
        ViewBag.ApprovalHistory = instance != null ? await _runtime.GetHistoryAsync(instance.WorkflowInstanceId) : null;
        ViewBag.CurrentUserRoles = _currentUser.Roles;

        // طبق CMMS-10c: نمایش نقاط ایزوله‌سازی این Permit
        ViewBag.IsolationPoints = await _isolationService.GetForPermitAsync(id);

        // طبق CMMS-10d: نمایش تست‌های گاز این Permit
        ViewBag.GasTests = await _gasTestService.GetForPermitAsync(id);

        // طبق CMMS-10e: نمایش Candidate Overlap و تعارض‌های ثبت‌شده‌ی SIMOPS این Permit
        ViewBag.SimopsCandidates = await _simopsService.GetCandidatesForPermitAsync(id);
        ViewBag.SimopsCandidateWorkOrders = await _simopsService.GetCandidateWorkOrdersForPermitAsync(id);
        ViewBag.SimopsConflicts = await _simopsService.GetConflictsForPermitAsync(id);

        // طبق CMMS-10e: نمایش Backlink اطلاعاتی به هر تحویل شیفتی که این Permit در آن Snapshot شده
        ViewBag.HandoverBacklinks = await _context.ShiftHandoverItems
            .Include(x => x.ShiftHandover)
            .Where(x => x.ReferenceType == "Permit" && x.ReferenceId == id)
            .Select(x => new { x.ShiftHandoverId, x.ShiftHandover!.Status, x.ShiftHandover.PreparedDate })
            .Distinct()
            .ToListAsync();

        // طبق CMMS-10f: ارزیابی Execution Gate فقط وقتی Status مربوطه است محاسبه می‌شود
        if (permit.Status is "Issued" or "Suspended")
        {
            ViewBag.ExecutionGate = await _executionGateService.EvaluateAsync(id);
        }
        ViewBag.ExecutionGateDecisions = await _executionGateService.GetDecisionsForPermitAsync(id);
        ViewBag.CanEmergencyOverride = await _roleModuleActionService.HasActionAsync(_currentUser.Roles, "ExecutionGate", "EmergencyOverride");

        // طبق PEMS-UI-01 (UI01-002): موتور Authority/Competency (PEMS-SEC-PERMIT-AUTH-001) از قبل
        // برای هر اقدام حساس محاسبه می‌شود، اما فقط Execution Gate آن را قبل از کلیک نمایش
        // می‌داد. این‌جا همون EvaluateApproverEligibilityAsync موجود - بدون هیچ تغییری در خودش -
        // فقط برای نمایش، و فقط وقتی دکمه‌ی مربوطه واقعاً رندر می‌شود، صدا زده می‌شود.
        var userId = _currentUser.UserId ?? 0;
        var employeeIdForEligibility = await GetCurrentEmployeeIdAsync();
        var rolesList = _currentUser.Roles.ToList();

        if (instance is { Status: "Running" } && instance.PendingStages.Any(p =>
                p.AssignedToRole != null && p.AssignedToRole.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(rolesList.Contains)))
        {
            ViewBag.ApproveEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Permit", "Approve");
            ViewBag.RejectEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Permit", "Reject");
        }

        if (permit.Status == "Active")
        {
            ViewBag.CloseEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Permit", "Close");
        }

        var isolationPointsForEligibility = (List<PEMS.Application.DTOs.IsolationPointListDto>)ViewBag.IsolationPoints;
        if (isolationPointsForEligibility.Any(p => p.Status == "Isolated"))
        {
            ViewBag.LotoVerifyEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "LOTO", "Verify");
        }

        var simopsConflictsForEligibility = (List<PEMS.Application.DTOs.SimopsConflictListDto>)ViewBag.SimopsConflicts;
        if (simopsConflictsForEligibility.Any(c => c.Status == "Potential"))
        {
            ViewBag.SimopsConfirmEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Simops", "Confirm");
        }
        if (simopsConflictsForEligibility.Any(c => c.Status == "Confirmed"))
        {
            ViewBag.SimopsResolveEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Simops", "Resolve");
        }
        if (simopsConflictsForEligibility.Any(c => c.Status is "Potential" or "Confirmed"))
        {
            ViewBag.SimopsAcceptRiskEligibility = await _permitAuthorityService.EvaluateApproverEligibilityAsync(userId, employeeIdForEligibility, rolesList, id, "Simops", "AcceptRisk");
        }

        return View(permit);
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }
}
