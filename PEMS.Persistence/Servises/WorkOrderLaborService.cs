using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.WorkflowPlatform.Interfaces;
using static PEMS.Persistence.Workflow.WorkOrderWorkflow;

namespace PEMS.Persistence.Services;

public class WorkOrderLaborService : IWorkOrderLaborService
{
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _access;

    public WorkOrderLaborService(PEMSDbContext context, IAccessControlService access)
    {
        _context = context;
        _access = access;
    }

    // طبق «PEMS — Work Order Governance Remediation» F3: ثبت ساعت کار واقعی، داده‌ی اجرا است - فقط
    // در وضعیت In Execution مجاز است. On Hold عمداً مجاز نیست (بند ۴ درخواست: بدون قاعده‌ی
    // کسب‌وکاری صریح برای زمان‌ثبت در حین توقف، پیش‌فرض رد است).
    public async Task<int> AddLaborEntryAsync(
        int workOrderId, int employeeId, int? teamId, DateTime workDate,
        decimal regularHours, decimal overtimeHours, string? notes, WorkOrderActor actor, bool isCorrection = false)
    {
        // Rule 6: ساعت کار نمی‌تواند منفی باشد
        if (regularHours < 0 || overtimeHours < 0)
            throw new Exception("ساعت کار نمی‌تواند منفی باشد.");

        if (regularHours == 0 && overtimeHours == 0)
            throw new Exception("حداقل یکی از ساعت عادی یا اضافه‌کاری باید بزرگ‌تر از صفر باشد.");

        var workOrder = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (workOrder == null) throw new Exception("WorkOrder یافت نشد.");

        // ---------- F3: وضعیت گردش‌کار ----------
        // H29: اصلاح پیش از تصویب نهایی (وضعیت Completed) فقط وقتی مجاز است که فراخوان دروازه‌ی بخش (مجوز اصلاح + دلیل + ممیزی) را گذرانده باشد؛
        // نرخ همچنان Snapshot لحظه‌ی ثبت است (GetEffectiveRatesAsync) و Closed هرگز مجاز نیست.
        var statusOk = workOrder.Status == "InProgress" || (isCorrection && workOrder.Status == "Completed");
        if (!statusOk)
            throw new Exception("ثبت ساعت کار واقعی فقط در مرحله‌ی «در حال اجرا» (یا اصلاح مجاز پیش از تصویب نهایی) مجاز است.");

        var employee = await _context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (employee == null) throw new Exception("کارمند یافت نشد.");

        // Rule 5: کارمند غیرفعال نباید Labor Entry جدید بگیرد
        if (!employee.IsActive)
            throw new Exception("این کارمند غیرفعال است.");

        // مطابق قاعده‌ی دیسیپلین در AssignExecutorAsync: ثبت ساعت کار فقط برای کارمند/گروه کاری
        // متعلق به دیسیپلین مسئولِ نگهداریِ همان WorkOrder مجاز است (بدون این چک، ساعت کار برای هر
        // کارمند/گروهِ هر دیسیپلینی روی هر WorkOrder ثبت می‌شد - ناسازگار با تخصیص مجری).
        if (!workOrder.ResponsibleDisciplineId.HasValue)
            throw new Exception("دیسیپلین مسئول نگهداری این WorkOrder هنوز تعیین نشده است.");

        if (employee.DisciplineId != workOrder.ResponsibleDisciplineId)
            throw new Exception("کارمند باید عضو دیسیپلین مسئول نگهداری این WorkOrder باشد.");

        // ---------- F3: actor (کاربر واقعاً واردشده) باید مجری واجد شرایط باشد - نه فقط WorkOrder.Edit ----------
        // طبق بند «A user with generic WorkOrder.Edit must NOT automatically gain the ability to
        // create Actual Labor»: این چک کاملاً مستقل از مجوز Edit سطح Controller است.
        if (!actor.EmployeeId.HasValue)
            throw new Exception("کارمند مرتبط با کاربر جاری مشخص نیست.");
        var (actorOrg, actorDisc) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (actorOrg != OrganizationDomains.Maintenance)
            throw new Exception("ثبت ساعت کار واقعی فقط برای سازمان نگهداری و تعمیرات مجاز است.");
        if (actorDisc != workOrder.ResponsibleDisciplineId)
            throw new Exception("شما عضو دیسیپلین مسئول نگهداری این Work Order نیستید.");
        var actorRoles = await ExpandRolesAsync(_access, actor.Roles);
        if (!actorRoles.Contains(ParticipantPrefix + Perm.Execute))
            throw new Exception("شما اختیار اجرای این Work Order را ندارید.");

        if (teamId.HasValue)
        {
            var teamDisciplineId = await _context.Teams.Where(t => t.TeamId == teamId.Value)
                .Select(t => (int?)t.MaintenanceGroup!.DisciplineId).FirstOrDefaultAsync();
            if (teamDisciplineId != workOrder.ResponsibleDisciplineId)
                throw new Exception("گروه کاری باید متعلق به دیسیپلین مسئول نگهداری این WorkOrder باشد.");
        }

        var (regularRate, overtimeRate) = await GetEffectiveRatesAsync(employeeId, workDate);
        // B8: ساعت بدون نرخ مؤثر، هزینه‌ی صفر بی‌صدا می‌ساخت؛ حالا ثبت رد می‌شود (نرخ Snapshot همچنان در رکورد ذخیره می‌شود)
        if (regularHours > 0 && regularRate <= 0) throw new LaborRateMissingException(employeeId, "Normal", workDate);
        if (overtimeHours > 0 && overtimeRate <= 0) throw new LaborRateMissingException(employeeId, "Overtime", workDate);

        // Rule 8: LaborCost همیشه در سرور محاسبه می‌شود، هرگز از ورودی Client گرفته نمی‌شود
        var laborCost = (regularHours * regularRate) + (overtimeHours * overtimeRate);

        var entry = new WorkOrderLabor
        {
            WorkOrderId = workOrderId,
            EmployeeId = employeeId,
            TeamId = teamId,
            WorkDate = workDate,
            RegularHours = regularHours,
            OvertimeHours = overtimeHours,
            // طبق بند ۱۴: Snapshot - نرخ همین الان ذخیره می‌شود و دیگر با تغییر نرخ آینده تغییر نمی‌کند
            Rate = regularRate,
            OvertimeRate = overtimeRate,
            LaborCost = laborCost,
            Notes = notes,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.WorkOrderLabors.AddAsync(entry);
        await _context.SaveChangesAsync();

        return entry.WorkOrderLaborId;
    }

    public async Task<(decimal RegularRate, decimal OvertimeRate)> GetEffectiveRatesAsync(int employeeId, DateTime asOfDate)
    {
        var rates = await _context.EmployeeLaborRates
            .Where(x => x.EmployeeId == employeeId && x.IsActive &&
                x.EffectiveFrom <= asOfDate && (x.EffectiveTo == null || x.EffectiveTo >= asOfDate))
            .ToListAsync();

        // Rule 7: نرخ نمی‌تواند منفی باشد - این‌جا فقط از داده‌ی از‌قبل‌معتبر می‌خوانیم،
        // اعتبارسنجی واقعی موقع ثبت نرخ (SetRateAsync در EmployeeController) انجام می‌شود
        var regular = rates.Where(x => x.RateType == "Normal").OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
        var overtime = rates.Where(x => x.RateType == "Overtime").OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();

        return (regular?.Rate ?? 0, overtime?.Rate ?? 0);
    }

    public async Task<decimal> GetTotalLaborCostAsync(int workOrderId)
    {
        return await _context.WorkOrderLabors
            .Where(x => x.WorkOrderId == workOrderId && x.IsActive)
            .SumAsync(x => x.LaborCost);
    }
}
