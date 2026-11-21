using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Exceptions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Engineering;

/// <summary>
/// طبق پذیرش ممیزی G/M/F (بند ۴ - "Real SQL Server Concurrency / Race Validation"، بند B از
/// "Remaining Acceptance Gaps"): این تست روی یک دیتابیس SQL Server کاملاً ایزوله اجرا می‌شود
/// (نه PEMSDB مشترک Dev) - ساخته‌شده از روی همان تاریخچه‌ی Migration واقعی پروژه، پس همان ایندکس
/// Unique واقعی (TransmittalConfiguration) که تولید هم دارد را دارد. اگر SQL Server در دسترس
/// نباشد، تست با پیام واضح Fail می‌شود، نه Skip خاموش.
/// </summary>
public class TransmittalNumberingConcurrencyTests
{
    private const string IsolatedDbName = "PEMSDB_ConcurrencyTest";

    [Fact]
    public async Task CreateAsync_TenConcurrentRequestsSameNumber_ExactlyOneWinnerNoDuplicatePersisted()
    {
        var transmittalNo = $"CONC-{Guid.NewGuid():N}"[..16];
        int projectId;

        // ==== Seed: یک Project واقعی در دیتابیس ایزوله (نه در PEMSDB) ====
        await using (var seedCtx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(IsolatedDbName))
        {
            var project = new Project
            {
                ProjectName = "Concurrency Test Project",
                ProjectCode = $"CT-{Guid.NewGuid():N}"[..10]
            };
            seedCtx.Projects.Add(project);
            await seedCtx.SaveChangesAsync();
            projectId = project.ProjectId;
        }

        // ==== Race: ۱۰ درخواست هم‌زمان، همون شماره‌ی ترانسمیتال، هر کدام DbContext/Service مستقل ====
        const int concurrentRequests = 10;

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(IsolatedDbName);
            var svc = new TransmittalService(ctx);

            try
            {
                var id = await svc.CreateAsync(
                    transmittalNo, projectId, "Eng", "Client", "IFA", null,
                    new List<int>(), false, new List<TransmittalRecipientInput>());

                return (Won: true, Id: (int?)id, Error: (string?)null);
            }
            catch (ConflictException ex) when (ex.Code == "transmittal-code-duplicate")
            {
                // یا رد شده توسط چک برنامه‌ای (AnyAsync)، یا توسط ایندکس Unique دیتابیس
                // (ترجمه‌شده توسط IsUniqueConstraintViolation) - هر دو یعنی "بازنده"
                return (Won: false, Id: (int?)null, Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);

        // ==== Assertion 1: دقیقاً یک برنده، بقیه رد شده ====
        var winners = results.Where(r => r.Won).ToList();
        var losers = results.Where(r => !r.Won).ToList();

        Assert.Single(winners);
        Assert.Equal(concurrentRequests - 1, losers.Count);
        Assert.All(losers, l => Assert.False(string.IsNullOrWhiteSpace(l.Error)));

        // ==== Assertion 2: صحت دیتابیس - دقیقاً یک ردیف با این شماره واقعاً Persist شده ====
        await using (var verifyCtx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(IsolatedDbName))
        {
            var persistedCount = await verifyCtx.Transmittals.CountAsync(x => x.TransmittalNo == transmittalNo);
            Assert.Equal(1, persistedCount);

            var persisted = await verifyCtx.Transmittals.FirstAsync(x => x.TransmittalNo == transmittalNo);
            Assert.Equal(winners[0].Id, persisted.TransmittalId);

            // ==== Assertion 3: رفتار تراکنشی - هیچ ردیف یتیم (TransmittalItem/Recipient بدون
            // Transmittal والد) از تلاش‌های ناموفق باقی نمانده - SaveChangesAsync اتمیک است ====
            var orphanItemCount = await verifyCtx.Set<TransmittalItem>()
                .CountAsync(i => !verifyCtx.Transmittals.Any(t => t.TransmittalId == i.TransmittalId));
            Assert.Equal(0, orphanItemCount);
        }

        // ==== Cleanup: پاک‌سازی داده‌ی این اجرای تست تا دیتابیس ایزوله برای اجرای بعدی تمیز بماند
        // (طبق بند ۴ - "verify no test contamination") ====
        await using (var cleanupCtx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(IsolatedDbName))
        {
            var toDelete = await cleanupCtx.Transmittals.Where(x => x.TransmittalNo == transmittalNo).ToListAsync();
            cleanupCtx.Transmittals.RemoveRange(toDelete);

            var project = await cleanupCtx.Projects.FindAsync(projectId);
            if (project != null) cleanupCtx.Projects.Remove(project);

            await cleanupCtx.SaveChangesAsync();

            // ==== Assertion 4: بعد از Cleanup، دیتابیس ایزوله واقعاً تمیز است ====
            Assert.Equal(0, await cleanupCtx.Transmittals.CountAsync(x => x.TransmittalNo == transmittalNo));
        }
    }
}
