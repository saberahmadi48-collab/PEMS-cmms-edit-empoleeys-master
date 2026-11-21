using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Work Request Concurrency Gate: دو درخواست هم‌زمان روی یک Work Request، روی SQL Server واقعی و با DbContext های مستقل.
/// دیتابیس اختصاصی و موقت (PEMS_CONC_xxx) ساخته و در پایان حذف می‌شود؛ PEMSDB دست نمی‌خورد.
/// دو حالت مسابقه: (۱) اجباری - هر دو عملیات پس از اتمام خواندن/اعتبارسنجی و درست قبل از اولین SaveChanges روی یک Barrier
/// منتظر می‌مانند (تعامل بدترین حالت، بدون Sleep)؛ (۲) آزاد - فقط Gate شروع مشترک، بدون دخالت در میانه‌ی کار.
/// </summary>
public class WorkRequestConcurrencyGateTests : IAsyncLifetime
{
    private const int Iterations = 50;
    private readonly string _dbName = "PEMS_CONC_" + Guid.NewGuid().ToString("N")[..12];
    private string _connection = "";

    private sealed class FakeNumbering : INumberingService
    {
        private int _n;
        public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null)
            => Task.FromResult($"{documentType}-C{Interlocked.Increment(ref _n):D5}");
    }

    /// <summary>Barrier فقط برای «اولین» SaveChanges هر Context؛ با Timeout تا در صورت خطا Deadlock نشود.</summary>
    private sealed class BarrierInterceptor : SaveChangesInterceptor
    {
        private readonly Barrier _barrier;
        private int _armed = 1;
        public BarrierInterceptor(Barrier barrier) { _barrier = barrier; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
                if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Race barrier timed out: the two operations did not reach the critical point together.");
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    private DbContextOptions<PEMSDbContext> Options(IInterceptor? interceptor = null)
    {
        var b = new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(_connection);
        if (interceptor != null) b.AddInterceptors(interceptor);
        return b.Options;
    }

    private static readonly string[] Requester = { "ConcRequester" };
    private static readonly string[] Reviewer = { "ConcReviewer" };

    public async Task InitializeAsync()
    {
        try { await InitializeCoreAsync(); }
        catch { await DisposeAsync(); throw; }   // xUnit لا يستدعي DisposeAsync عند خطأ Initialize
    }

    private async Task InitializeCoreAsync()
    {
        var csb = new SqlConnectionStringBuilder(PemsDbContextFactory.ConnectionString) { InitialCatalog = _dbName };
        _connection = csb.ConnectionString;

        await using var ctx = new PEMSDbContext(Options());
        await ctx.Database.MigrateAsync();

        ctx.Departments.Add(new Department { Name = "Maintenance" });
        ctx.Disciplines.Add(new Discipline { Name = "Mechanical", Code = "MECH" });
        ctx.Projects.Add(new Project { ProjectCode = "CONC", ProjectName = "Concurrency" });
        await ctx.SaveChangesAsync();
        var dept = await ctx.Departments.FirstAsync();
        var disc = await ctx.Disciplines.FirstAsync();
        var prj = await ctx.Projects.FirstAsync();
        ctx.Employees.Add(new Employee { FirstName = "Conc", LastName = "One", DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true });
        ctx.Employees.Add(new Employee { FirstName = "Conc", LastName = "Two", DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true });
        // A3 (SoD پیش‌فرض روشن): ثبت‌کننده‌ی درخواست باید با بازبین/تأییدکننده متفاوت باشد؛ کارمند سوم فقط درخواست‌دهنده است
        ctx.Employees.Add(new Employee { FirstName = "Conc", LastName = "Requester", DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "CONC-101", Name = "Pump", DisciplineId = disc.DisciplineId, ProjectId = prj.ProjectId, IsActive = true });
        await ctx.SaveChangesAsync();

        Permission Perm(string a) => ctx.Permissions.Local.FirstOrDefault(p => p.Name == "WorkRequest." + a)
            ?? ctx.Permissions.Add(new Permission { Name = "WorkRequest." + a, ResourceName = "WorkRequest", ActionName = a, IsActive = true, CreatedDate = DateTime.Now }).Entity;
        var create = await ctx.Permissions.FirstOrDefaultAsync(p => p.Name == "WorkRequest.Create") ?? Perm("Create");
        var approve = await ctx.Permissions.FirstOrDefaultAsync(p => p.Name == "WorkRequest.Approve") ?? Perm("Approve");
        await ctx.SaveChangesAsync();

        async Task Role(string name, params Permission[] grants)
        {
            var r = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() };
            ctx.Roles.Add(r);
            await ctx.SaveChangesAsync();
            foreach (var g in grants) ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = r.Id, PermissionId = g.PermissionId, IsActive = true });
            await ctx.SaveChangesAsync();
        }
        await Role("ConcRequester", create);
        await Role("ConcReviewer", create, approve);

        // تعریف گردش‌کار و Binding از همان Seeder واقعی
        var sp = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await WorkRequestWorkflowSeeder.SeedAsync(sp);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(_connection) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_dbName}') IS NOT NULL BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private static WorkRequestService NewService(PEMSDbContext ctx) => new(ctx, new FakeNumbering(), null!);
    private static WorkRequestActor Actor(int emp, string[] roles) => new(emp, roles);

    private async Task<int> NewDraftAsync()
    {
        await using var ctx = new PEMSDbContext(Options());
        var emp = await ctx.Employees.OrderByDescending(e => e.EmployeeId).Select(e => e.EmployeeId).FirstAsync();   // درخواست‌دهنده = کارمند سوم
        var eq = await ctx.Equipments.Select(e => e.EquipmentId).FirstAsync();
        var (ok, err, id) = await NewService(ctx).CreateAsync("Concurrent submit", "d", emp, eq, null, null, null, "Medium", null);
        Assert.True(ok, err);
        return id!.Value;
    }

    private async Task<int> EmployeeAsync(int index)
    {
        await using var ctx = new PEMSDbContext(Options());
        return await ctx.Employees.OrderBy(e => e.EmployeeId).Skip(index).Select(e => e.EmployeeId).FirstAsync();
    }

    private record Outcome(bool Success, string? Error, Exception? Thrown);

    /// <summary>دو عملیات کاملاً مستقل (Context/Service جدا)، شروع هم‌زمان با Gate؛ در حالت forced روی Barrier قبل از Save هم‌گام می‌شوند.</summary>
    private async Task<Outcome[]> RaceAsync(Func<WorkRequestService, Task<(bool, string?)>> op, bool forced)
    {
        var barrier = forced ? new Barrier(2) : null;
        var gate = new TaskCompletionSource();
        using var ready = new CountdownEvent(2);     // هر دو عملیات آماده‌ی Gate شده‌اند (بدون Sleep)
        async Task<Outcome> One()
        {
            await using var ctx = new PEMSDbContext(Options(barrier == null ? null : new BarrierInterceptor(barrier)));
            var svc = NewService(ctx);
            ready.Signal();
            await gate.Task;
            try { var (ok, err) = await op(svc); return new Outcome(ok, err, null); }
            catch (Exception ex) { return new Outcome(false, null, ex); }
        }
        var t1 = Task.Run(One); var t2 = Task.Run(One);
        await Task.Run(() => ready.Wait());
        gate.SetResult();
        return await Task.WhenAll(t1, t2);
    }

    private record Snapshot(int Instances, int StartRows, int Histories, string Status, int BusinessDraftToSubmitted, int BusinessRows, int? CurrentStage, string InstanceStatus);

    private async Task<Snapshot> SnapshotAsync(int wrId)
    {
        await using var ctx = new PEMSDbContext(Options());
        var key = $"WorkRequest|WorkRequest|{wrId}";
        var instances = await ctx.WorkflowInstances.Where(i => i.EntityKey == key).ToListAsync();
        var ids = instances.Select(i => i.WorkflowInstanceId).ToList();
        var hist = await ctx.WorkflowHistories.Where(h => ids.Contains(h.WorkflowInstanceId)).ToListAsync();
        var wr = await ctx.WorkRequests.AsNoTracking().FirstAsync(w => w.WorkRequestId == wrId);
        var biz = await ctx.WorkRequestStatusHistories.Where(h => h.WorkRequestId == wrId).ToListAsync();
        return new Snapshot(instances.Count, hist.Count(h => h.ActionType == "Start"), hist.Count, wr.Status,
            biz.Count(b => b.FromStatus == "Draft" && b.ToStatus == "Submitted"), biz.Count,
            instances.FirstOrDefault()?.CurrentStageId, instances.FirstOrDefault()?.Status ?? "");
    }

    private static void AssertSingleStart(Snapshot s, Outcome[] r, int iteration, string mode)
    {
        var msg = $"[{mode} #{iteration}] ";
        Assert.True(s.Instances == 1, msg + $"instances={s.Instances}");
        Assert.True(s.StartRows == 1 && s.Histories == 1, msg + $"startRows={s.StartRows} histories={s.Histories}");
        Assert.True(s.Status == "Submitted", msg + "wr status " + s.Status);
        Assert.True(s.BusinessDraftToSubmitted == 1 && s.BusinessRows == 1, msg + $"business Draft->Submitted={s.BusinessDraftToSubmitted} rows={s.BusinessRows}");
        Assert.Equal("Running", s.InstanceStatus);
        Assert.True(r.Count(x => x.Success) == 1, msg + "successes=" + r.Count(x => x.Success));
        foreach (var o in r)
        {
            Assert.Null(o.Thrown);   // هیچ استثنای خامی به فراخوان نشت نمی‌کند
            if (!o.Success)
            {
                Assert.False(string.IsNullOrWhiteSpace(o.Error));
                Assert.DoesNotContain("SqlException", o.Error!);
                Assert.DoesNotContain("DbUpdate", o.Error!);
                Assert.DoesNotContain("Microsoft.", o.Error!);
            }
        }
    }

    [Fact]
    public async Task Forced_interleaving_two_submits_create_exactly_one_workflow_start_50_times()
    {
        var actor1 = Actor(await EmployeeAsync(0), Requester);
        var actor2 = Actor(await EmployeeAsync(1), Requester);
        var failures = new List<string>();
        var errors = new HashSet<string?>();
        for (var i = 1; i <= Iterations; i++)
        {
            var wr = await NewDraftAsync();
            var n = 0;
            var r = await RaceAsync(svc => svc.SubmitAsync(wr, Interlocked.Increment(ref n) == 1 ? actor1 : actor2), forced: true);
            var s = await SnapshotAsync(wr);
            try { AssertSingleStart(s, r, i, "forced"); } catch (Exception ex) { failures.Add(ex.Message.Split('\n')[0]); }
            foreach (var o in r.Where(x => !x.Success)) errors.Add(o.Error);
        }
        Assert.True(failures.Count == 0, $"{failures.Count}/{Iterations} iterations violated the invariant: " + string.Join(" | ", failures.Take(5)));
        Console.WriteLine($"FORCED iterations={Iterations} invariantOK={Iterations - failures.Count} loserMessages=[{string.Join(" ; ", errors)}]");
    }

    [Fact]
    public async Task Free_running_two_submits_create_exactly_one_workflow_start_50_times()
    {
        var actor1 = Actor(await EmployeeAsync(0), Requester);
        var actor2 = Actor(await EmployeeAsync(1), Requester);
        var failures = new List<string>();
        for (var i = 1; i <= Iterations; i++)
        {
            var wr = await NewDraftAsync();
            var n = 0;
            var r = await RaceAsync(svc => svc.SubmitAsync(wr, Interlocked.Increment(ref n) == 1 ? actor1 : actor2), forced: false);
            var s = await SnapshotAsync(wr);
            try { AssertSingleStart(s, r, i, "free"); } catch (Exception ex) { failures.Add(ex.Message.Split('\n')[0]); }
        }
        Assert.True(failures.Count == 0, $"{failures.Count}/{Iterations} iterations violated the invariant: " + string.Join(" | ", failures.Take(5)));
        Console.WriteLine($"FREE iterations={Iterations} invariantOK={Iterations - failures.Count}");
    }

    [Fact]
    public async Task Same_actor_double_click_forced_race_is_idempotent()
    {
        var actor = Actor(await EmployeeAsync(0), Requester);
        for (var i = 1; i <= 20; i++)
        {
            var wr = await NewDraftAsync();
            var r = await RaceAsync(svc => svc.SubmitAsync(wr, actor), forced: true);
            AssertSingleStart(await SnapshotAsync(wr), r, i, "double-click");
        }
    }

    [Fact]
    public async Task Repeated_sequential_submit_is_rejected_without_a_second_instance()
    {
        var actor = Actor(await EmployeeAsync(0), Requester);
        var wr = await NewDraftAsync();
        await using var ctx = new PEMSDbContext(Options());
        var svc = NewService(ctx);
        Assert.True((await svc.SubmitAsync(wr, actor)).Success);
        var (ok, err) = await svc.SubmitAsync(wr, actor);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(err));
        // درخواست مجدد از یک Context تازه (Retry از پروکسی/مرورگر)
        await using var ctx2 = new PEMSDbContext(Options());
        (ok, err) = await NewService(ctx2).SubmitAsync(wr, actor);
        Assert.False(ok);
        var s = await SnapshotAsync(wr);
        Assert.Equal(1, s.Instances); Assert.Equal(1, s.Histories); Assert.Equal("Submitted", s.Status); Assert.Equal(1, s.BusinessRows);
    }

    [Fact]
    public async Task Forced_race_of_two_identical_workflow_actions_advances_exactly_once()
    {
        var actorA = Actor(await EmployeeAsync(0), Requester);
        var actorB = Actor(await EmployeeAsync(1), Requester);
        for (var i = 1; i <= 20; i++)
        {
            var wr = await NewDraftAsync();
            await using (var c = new PEMSDbContext(Options()))
                Assert.True((await NewService(c).SubmitAsync(wr, actorA)).Success);

            var n = 0;
            var r = await RaceAsync(svc => svc.ExecuteWorkflowActionAsync(wr, "Approve", null, Interlocked.Increment(ref n) == 1 ? actorA : actorB), forced: true);

            await using var ctx = new PEMSDbContext(Options());
            var key = $"WorkRequest|WorkRequest|{wr}";
            var inst = await ctx.WorkflowInstances.SingleAsync(x => x.EntityKey == key);
            var approves = await ctx.WorkflowHistories.CountAsync(h => h.WorkflowInstanceId == inst.WorkflowInstanceId && h.ActionType == "Approve");
            var stage = await ctx.WorkflowStages.Where(s => s.WorkflowStageId == inst.CurrentStageId).Select(s => s.Name).SingleAsync();
            var wrRow = await ctx.WorkRequests.AsNoTracking().FirstAsync(w => w.WorkRequestId == wr);
            var review = await ctx.WorkRequestStatusHistories.CountAsync(h => h.WorkRequestId == wr && h.ToStatus == "UnderReview");
            Assert.True(r.Count(x => x.Success) == 1, $"#{i} successes={r.Count(x => x.Success)}");
            Assert.True(approves == 1 && review == 1, $"#{i} approves={approves} review={review}");
            Assert.Equal("Under Review", stage);
            Assert.Equal("UnderReview", wrRow.Status);
            Assert.All(r, o => Assert.Null(o.Thrown));
        }
    }

    [Fact]
    public async Task Database_index_alone_prevents_duplicate_running_instances_even_when_the_work_request_row_is_not_touched()
    {
        // Runtime مستقیم (بدون به‌روزرسانی ردیف Work Request => RowVersion هیچ نقشی ندارد). فقط Index یکتای دیتابیس می‌ماند.
        // قبل از افزودن Index، همین آزمون در ۲۰ از ۲۰ تکرار دو Instance ساخت (اندازه‌گیری Gate).
        for (var i = 1; i <= 20; i++)
        {
            var wr = await NewDraftAsync();
            var barrier = new Barrier(2);
            var results = new System.Collections.Concurrent.ConcurrentBag<Exception?>();
            async Task One()
            {
                await using var ctx = new PEMSDbContext(Options(new BarrierInterceptor(barrier)));
                var rt = new PEMS.WorkflowPlatform.Services.WorkflowRuntimeService(ctx);
                try { await rt.StartInstanceAsync("WorkRequest", "WorkRequest", wr, "Standard", null, 1); results.Add(null); }
                catch (Exception ex) { results.Add(ex); }
            }
            await Task.WhenAll(Task.Run(One), Task.Run(One));

            var s = await SnapshotAsync(wr);
            Assert.True(s.Instances == 1, $"#{i} instances={s.Instances}");
            Assert.True(s.Histories == 1, $"#{i} histories={s.Histories}");
            Assert.Equal(1, results.Count(r => r == null));
            var loser = results.Single(r => r != null)!;
            Assert.IsType<PEMS.WorkflowPlatform.Interfaces.WorkflowConcurrencyConflictException>(loser);                 // خطای قطعی، نه DbUpdateException/SqlException خام
            Assert.DoesNotContain("SqlException", loser.GetType().Name);
        }
    }
}
