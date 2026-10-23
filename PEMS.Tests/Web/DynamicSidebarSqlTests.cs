using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Navigation;

namespace PEMS.Tests.Web;

/// <summary>
/// سایدبار داده‌محور روی SQL Server واقعی (دیتابیس یک‌بارمصرف PEMS_TEST_P03_*، همه‌ی Migrationها شامل AddNavigationMenuItems، در پایان Drop):
/// Migration اعمال می‌شود، همگام‌سازی کاتالوگ Idempotent است، کلید تکراری را ایندکس یکتا رد می‌کند و ویرایش هم‌زمان با RowVersion کهنه رد می‌شود.
/// </summary>
public class DynamicSidebarSqlTests
{
    [Fact]
    public async Task Migration_Sync_UniqueKey_AndConcurrency_OnRealSqlServer()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();

        await using (var ctx = db.NewContext())
        {
            var menu = new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions()));
            Assert.Equal(NavigationMenuDefaults.All.Count, await menu.SyncDefaultsAsync(NavigationMenuDefaults.All));
            Assert.Equal(0, await menu.SyncDefaultsAsync(NavigationMenuDefaults.All));
        }

        Assert.Equal(NavigationMenuDefaults.All.Count, await db.ScalarIntAsync("SELECT COUNT(*) FROM NavigationMenuItems"));

        // ایندکس یکتای Key در خودِ دیتابیس
        await using (var ctx = db.NewContext())
        {
            ctx.NavigationMenuItems.Add(new NavigationMenuItem { Key = "eng.vdr", Kind = NavigationMenuItemKinds.Link, TitleKey = "VDR", VisibilityRule = "Always" });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // ویرایش با RowVersion کهنه: رد می‌شود و ویرایش اول باقی می‌ماند
        string stale;
        await using (var ctx = db.NewContext())
            stale = (await new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions())).GetAllForAdminAsync()).Single(i => i.Key == "eng.vdr").RowVersion!;

        await using (var ctx = db.NewContext())
        {
            var first = await new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions())).UpdateAsync(
                new NavigationMenuItemEditInput { Key = "eng.vdr", ParentKey = "sec.eng", TitleEn = "First", IsActive = true, Icon = "bi-journal-check", RowVersion = stale });
            Assert.True(first.Success);
        }
        await using (var ctx = db.NewContext())
        {
            var second = await new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions())).UpdateAsync(
                new NavigationMenuItemEditInput { Key = "eng.vdr", ParentKey = "sec.eng", TitleEn = "Second", IsActive = true, Icon = "bi-journal-check", RowVersion = stale });
            Assert.Equal("Nav_Err_Concurrency", second.Error);
        }

        var title = await db.QueryAsync("SELECT TitleEn FROM NavigationMenuItems WHERE [Key] = 'eng.vdr'", r => r.GetString(0));
        Assert.Equal("First", Assert.Single(title));
    }
}
