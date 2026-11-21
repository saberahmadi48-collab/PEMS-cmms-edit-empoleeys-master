using PEMS.Persistence.Services;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Phase 6D: AssignExecutorAsync قبلاً فقط Employee را در برابر ResponsibleDisciplineId چک می‌کرد؛
/// شاخه‌ی teamId هیچ چکی نداشت (هر تیم فعالی از هر دیسیپلینی قابل تخصیص بود). همین‌طور
/// AddLaborEntryAsync اصلاً هیچ چک دیسیپلینی‌ای نداشت. این تست‌ها رفع این دو خلأ را پوشش می‌دهند.
/// طبق «PEMS — Work Order Governance Remediation» F3: از این پس AddLaborEntryAsync یک actor هم
/// می‌گیرد (کاربر واقعاً واردشده) و فقط در وضعیت InProgress مجاز است - تست‌های زیر با Executor به
/// عنوان actor (که خودش هم واجد شرایط دیسیپلین Mech است) به‌روزرسانی شدند تا فقط همان چیزی که
/// قصد تست دارند (دیسیپلین کارمند/تیمِ هدف) را بسنجند، نه چک‌های تازه‌ی F3 را.
/// </summary>
public class WorkOrderDisciplineScopeTests : WorkOrderGovernanceTestBase
{
    [Fact]
    public async Task AssignExecutorAsync_rejects_team_from_a_different_discipline_than_ResponsibleDisciplineId()
    {
        var id = await CreateAsync();
        await Do(id, "Approve", Creator);
        var (planned, planErr) = await Svc.SetPlanningAsync(id, Mech, Planner);
        Assert.True(planned, planErr);
        await Do(id, "Approve", Planner);

        var elecTeamId = AddTeam(Elec, "ElecCrew");
        var (ok, err) = await Svc.AssignExecutorAsync(id, elecTeamId, null, DisciplineHead);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(err));

        var wo = await Wo(id);
        Assert.Null(wo.AssignedTeamId);
    }

    [Fact]
    public async Task AssignExecutorAsync_accepts_team_from_the_same_discipline_as_ResponsibleDisciplineId()
    {
        var id = await CreateAsync();
        await Do(id, "Approve", Creator);
        var (planned, planErr) = await Svc.SetPlanningAsync(id, Mech, Planner);
        Assert.True(planned, planErr);
        await Do(id, "Approve", Planner);

        var mechTeamId = AddTeam(Mech, "MechCrew");
        var (ok, err) = await Svc.AssignExecutorAsync(id, mechTeamId, null, DisciplineHead);
        Assert.True(ok, err);

        var wo = await Wo(id);
        Assert.Equal(mechTeamId, wo.AssignedTeamId);
    }

    [Fact]
    public async Task AddLaborEntryAsync_rejects_employee_from_a_different_discipline_than_ResponsibleDisciplineId()
    {
        var id = await CreateAsync();
        await RunToInProgressAsync(id);

        var labor = new WorkOrderLaborService(Ctx, AccessControl);
        var ex = await Assert.ThrowsAsync<Exception>(() =>
            labor.AddLaborEntryAsync(id, ElecExecutor.EmployeeId!.Value, null, DateTime.Today, 4, 0, null, Executor));
        Assert.Contains("دیسیپلین", ex.Message);
    }

    [Fact]
    public async Task AddLaborEntryAsync_rejects_team_from_a_different_discipline_than_ResponsibleDisciplineId()
    {
        var id = await CreateAsync();
        await RunToInProgressAsync(id);

        var elecTeamId = AddTeam(Elec, "ElecCrew2");
        var labor = new WorkOrderLaborService(Ctx, AccessControl);
        var ex = await Assert.ThrowsAsync<Exception>(() =>
            labor.AddLaborEntryAsync(id, Executor.EmployeeId!.Value, elecTeamId, DateTime.Today, 4, 0, null, Executor));
        Assert.Contains("دیسیپلین", ex.Message);
    }

    [Fact]
    public async Task AddLaborEntryAsync_accepts_employee_and_team_from_the_same_discipline()
    {
        var id = await CreateAsync();
        await RunToInProgressAsync(id);

        var mechTeamId = AddTeam(Mech, "MechCrew2");
        Ctx.EmployeeLaborRates.Add(new PEMS.Domain.Entities.EmployeeLaborRate { EmployeeId = Executor.EmployeeId!.Value, RateType = "Normal", Rate = 100, EffectiveFrom = DateTime.Today.AddYears(-1), IsActive = true });
        await Ctx.SaveChangesAsync();
        var labor = new WorkOrderLaborService(Ctx, AccessControl);
        var laborId = await labor.AddLaborEntryAsync(id, Executor.EmployeeId!.Value, mechTeamId, DateTime.Today, 4, 0, null, Executor);
        Assert.True(laborId > 0);
    }

    // طبق F3: قبل از Start Work (یعنی هنوز InProgress نشده)، ثبت ساعت کار واقعی رد می‌شود - حالا
    // این اولین دلیل رد است (نه فقط دیسیپلین)، چون بدون رسیدن به InProgress اصلاً امکان ندارد
    // ResponsibleDisciplineId خالی بماند (در Plan تنظیم می‌شود، قبل از InProgress).
    [Fact]
    public async Task AddLaborEntryAsync_rejects_before_execution_status_is_reached()
    {
        var id = await CreateAsync();

        var labor = new WorkOrderLaborService(Ctx, AccessControl);
        var ex = await Assert.ThrowsAsync<Exception>(() =>
            labor.AddLaborEntryAsync(id, Executor.EmployeeId!.Value, null, DateTime.Today, 4, 0, null, Executor));
        Assert.Contains("در حال اجرا", ex.Message);
    }
}
