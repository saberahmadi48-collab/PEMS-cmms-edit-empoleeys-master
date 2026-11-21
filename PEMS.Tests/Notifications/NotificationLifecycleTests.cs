using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Hubs;

namespace PEMS.Tests.Notifications;

/// <summary>
/// چرخه‌ی عمر اعلان (Unread → Read → Dismissed/Resolved)، مالکیت کاربر، هویت SignalR و Hook های
/// تولیدکننده (Task/Letter). دیتابیس InMemory؛ پخش زنده با Fake ضبط‌کننده.
/// </summary>
public class NotificationLifecycleTests
{
    private sealed class RecordingBroadcaster : INotificationBroadcaster
    {
        public List<(int UserId, NotificationDto N, int Unread)> Sent { get; } = new();
        public List<(int UserId, int Unread)> Counts { get; } = new();

        public Task BroadcastNotificationAsync(int userId, NotificationDto notification, int unreadCount)
        { Sent.Add((userId, notification, unreadCount)); return Task.CompletedTask; }

        public Task BroadcastUnreadCountAsync(int userId, int unreadCount)
        { Counts.Add((userId, unreadCount)); return Task.CompletedTask; }
    }

    private static (NotificationService Svc, PEMSDbContext Ctx, RecordingBroadcaster Bc) Create(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"Notif_{name}");
        var bc = new RecordingBroadcaster();
        return (new NotificationService(ctx, bc), ctx, bc);
    }

    // ---------------- چرخه‌ی عمر ----------------

    [Fact]
    public async Task Create_PersistsUnread_AndBroadcastsOnlyToRecipientWithAuthoritativeCount()
    {
        var (svc, ctx, bc) = Create(nameof(Create_PersistsUnread_AndBroadcastsOnlyToRecipientWithAuthoritativeCount));

        await svc.CreateAsync(1, "t1", "m", "/x", "Letter", 5);
        await svc.CreateAsync(1, "t2", "m");

        var n = await ctx.Notifications.FirstAsync(x => x.Title == "t1");
        Assert.False(n.IsRead);
        Assert.Null(n.DismissedDate);
        Assert.Equal(2, bc.Sent.Count);
        Assert.All(bc.Sent, s => Assert.Equal(1, s.UserId));
        Assert.Equal(new[] { 1, 2 }, bc.Sent.Select(s => s.Unread).ToArray());
        Assert.NotEqual(0, bc.Sent[0].N.NotificationId);
        Assert.Equal("Letter", bc.Sent[0].N.RelatedType);
    }

    [Fact]
    public async Task MarkRead_ThenCounts_Decrease_AndStayInActiveList()
    {
        var (svc, _, _) = Create(nameof(MarkRead_ThenCounts_Decrease_AndStayInActiveList));
        await svc.CreateAsync(1, "a", "m");
        var id = (await svc.GetForUserAsync(1)).Single().NotificationId;

        Assert.True(await svc.MarkAsReadAsync(1, id));

        Assert.Equal(0, await svc.GetUnreadCountAsync(1));
        Assert.True((await svc.GetForUserAsync(1)).Single().IsRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dismiss_UnreadOrRead_RemovesFromActive_KeepsRecord(bool readFirst)
    {
        var (svc, ctx, _) = Create($"{nameof(Dismiss_UnreadOrRead_RemovesFromActive_KeepsRecord)}_{readFirst}");
        await svc.CreateAsync(1, "a", "m");
        var id = (await svc.GetForUserAsync(1)).Single().NotificationId;
        if (readFirst) await svc.MarkAsReadAsync(1, id);

        Assert.True(await svc.DismissAsync(1, id));

        Assert.Empty(await svc.GetForUserAsync(1));
        Assert.Equal(0, await svc.GetUnreadCountAsync(1));
        Assert.Equal(1, await ctx.Notifications.CountAsync());           // soft: هرگز حذف فیزیکی
        var inbox = await svc.GetInboxAsync(1, "dismissed", 1, 25);
        Assert.Single(inbox.Items);
        Assert.Empty((await svc.GetInboxAsync(1, "all", 1, 25)).Items);
    }

    [Fact]
    public async Task MarkAll_AffectsOnlyCallerNotifications()
    {
        var (svc, _, _) = Create(nameof(MarkAll_AffectsOnlyCallerNotifications));
        await svc.CreateAsync(1, "a", "m");
        await svc.CreateAsync(2, "b", "m");

        await svc.MarkAllAsReadAsync(1);

        Assert.Equal(0, await svc.GetUnreadCountAsync(1));
        Assert.Equal(1, await svc.GetUnreadCountAsync(2));
    }

    [Fact]
    public async Task DismissAll_AffectsOnlyCallerNotifications()
    {
        var (svc, _, _) = Create(nameof(DismissAll_AffectsOnlyCallerNotifications));
        await svc.CreateAsync(1, "a", "m");
        await svc.CreateAsync(2, "b", "m");

        await svc.DismissAllAsync(1);

        Assert.Empty(await svc.GetForUserAsync(1));
        Assert.Single(await svc.GetForUserAsync(2));
    }

    [Fact]
    public async Task InboxFilters_SeparateUnreadReadAndDismissed()
    {
        var (svc, _, _) = Create(nameof(InboxFilters_SeparateUnreadReadAndDismissed));
        await svc.CreateAsync(1, "unread", "m");
        await svc.CreateAsync(1, "read", "m");
        await svc.CreateAsync(1, "dismissed", "m");
        var all = await svc.GetForUserAsync(1);
        await svc.MarkAsReadAsync(1, all.Single(x => x.Title == "read").NotificationId);
        await svc.DismissAsync(1, all.Single(x => x.Title == "dismissed").NotificationId);

        Assert.Equal(new[] { "unread" }, (await svc.GetInboxAsync(1, "unread", 1, 25)).Items.Select(x => x.Title).ToArray());
        Assert.Equal(new[] { "read" }, (await svc.GetInboxAsync(1, "read", 1, 25)).Items.Select(x => x.Title).ToArray());
        Assert.Equal(new[] { "dismissed" }, (await svc.GetInboxAsync(1, "dismissed", 1, 25)).Items.Select(x => x.Title).ToArray());
        Assert.Equal(2, (await svc.GetInboxAsync(1, "all", 1, 25)).TotalCount);
    }

    [Fact]
    public async Task Inbox_IsPaged_NotUnbounded()
    {
        var (svc, _, _) = Create(nameof(Inbox_IsPaged_NotUnbounded));
        for (var i = 0; i < 30; i++) await svc.CreateAsync(1, $"n{i}", "m");

        var page = await svc.GetInboxAsync(1, "all", 1, 20);

        Assert.Equal(20, page.Items.Count);
        Assert.Equal(30, page.TotalCount);
    }

    // ---------------- مالکیت (Cross-user) ----------------

    [Fact]
    public async Task CrossUser_MarkRead_Dismiss_Open_AreRejected_AndNothingChanges()
    {
        var (svc, ctx, _) = Create(nameof(CrossUser_MarkRead_Dismiss_Open_AreRejected_AndNothingChanges));
        await svc.CreateAsync(2, "B's", "m", "/secret");
        var bId = (await svc.GetForUserAsync(2)).Single().NotificationId;

        Assert.False(await svc.MarkAsReadAsync(1, bId));
        Assert.False(await svc.DismissAsync(1, bId));
        Assert.Null(await svc.OpenAsync(1, bId));

        var n = await ctx.Notifications.SingleAsync();
        Assert.False(n.IsRead);
        Assert.Null(n.DismissedDate);
        Assert.Empty(await svc.GetForUserAsync(1));
    }

    // ---------------- ناوبری (Open) ----------------

    [Fact]
    public async Task Open_MarksReadAndReturnsLink()
    {
        var (svc, _, _) = Create(nameof(Open_MarksReadAndReturnsLink));
        await svc.CreateAsync(1, "a", "m", "/Letter/Details/9");
        var id = (await svc.GetForUserAsync(1)).Single().NotificationId;

        var link = await svc.OpenAsync(1, id);

        Assert.Equal("/Letter/Details/9", link);
        Assert.Equal(0, await svc.GetUnreadCountAsync(1));
        Assert.Equal("/Letter/Details/9", await svc.OpenAsync(1, id)); // اعلان خوانده‌شده هم ناوبری می‌کند
    }

    private static NotificationController NewController(NotificationService svc, int? userId)
    {
        var c = new NotificationController(svc, new FakeCurrentUserService { UserId = userId })
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        c.Url = new LocalOnlyUrlHelper(c.ControllerContext);
        return c;
    }

    private sealed class LocalOnlyUrlHelper : IUrlHelper
    {
        public LocalOnlyUrlHelper(ActionContext ctx) { ActionContext = ctx; }
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext a) => "/";
        public string? RouteUrl(UrlRouteContext r) => "/";
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => "/";
        public bool IsLocalUrl(string? url) =>
            !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
    }

    [Fact]
    public async Task Controller_Open_FollowsLinkAndMarksRead()
    {
        var (svc, _, _) = Create(nameof(Controller_Open_FollowsLinkAndMarksRead));
        await svc.CreateAsync(1, "a", "m", "/Task/Index");
        var id = (await svc.GetForUserAsync(1)).Single().NotificationId;

        var result = await NewController(svc, 1).Open(id);

        Assert.Equal("/Task/Index", Assert.IsType<LocalRedirectResult>(result).Url);
        Assert.Equal(0, await svc.GetUnreadCountAsync(1));
    }

    [Fact]
    public async Task Controller_Open_ExternalUrl_IsNotFollowed()
    {
        var (svc, _, _) = Create(nameof(Controller_Open_ExternalUrl_IsNotFollowed));
        await svc.CreateAsync(1, "a", "m", "https://evil.example/x");
        var id = (await svc.GetForUserAsync(1)).Single().NotificationId;

        var result = await NewController(svc, 1).Open(id);

        Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task Controller_CrossUser_MarkReadAndDismiss_ReturnNotFound()
    {
        var (svc, ctx, _) = Create(nameof(Controller_CrossUser_MarkReadAndDismiss_ReturnNotFound));
        await svc.CreateAsync(2, "B's", "m");
        var bId = (await svc.GetForUserAsync(2)).Single().NotificationId;
        var attacker = NewController(svc, 1);

        Assert.IsType<NotFoundResult>(await attacker.MarkRead(bId));
        Assert.IsType<NotFoundResult>(await attacker.Dismiss(bId));
        Assert.False((await ctx.Notifications.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task Controller_Anonymous_IsUnauthorized()
    {
        var (svc, _, _) = Create(nameof(Controller_Anonymous_IsUnauthorized));
        var c = NewController(svc, null);

        Assert.IsType<UnauthorizedResult>(await c.MarkRead(1));
        Assert.IsType<UnauthorizedResult>(await c.Dismiss(1));
        Assert.IsType<UnauthorizedResult>(await c.MarkAllRead());
        Assert.IsType<UnauthorizedResult>(await c.UnreadCount());
    }

    [Fact]
    public async Task Controller_UnreadCount_IsAuthoritativeAfterReconnect()
    {
        var (svc, _, _) = Create(nameof(Controller_UnreadCount_IsAuthoritativeAfterReconnect));
        await svc.CreateAsync(1, "missed while disconnected", "m");   // اعلانی که حین قطع بودن SignalR رسید
        await svc.CreateAsync(1, "second", "m");

        var json = Assert.IsType<JsonResult>(await NewController(svc, 1).UnreadCount());

        Assert.Equal(2, (int)json.Value!.GetType().GetProperty("unread")!.GetValue(json.Value)!);
    }

    // ---------------- Resolve / Hook تولیدکننده‌ها ----------------

    [Fact]
    public async Task MarkRelatedRead_DoesNotDismiss_AndIsScopedToUser()
    {
        var (svc, _, _) = Create(nameof(MarkRelatedRead_DoesNotDismiss_AndIsScopedToUser));
        await svc.CreateAsync(1, "a", "m", null, "Letter", 7);
        await svc.CreateAsync(2, "b", "m", null, "Letter", 7);

        await svc.MarkRelatedReadAsync(1, "Letter", 7);

        Assert.True((await svc.GetForUserAsync(1)).Single().IsRead);   // هنوز در صندوق فعال
        Assert.Equal(1, await svc.GetUnreadCountAsync(2));
    }

    [Fact]
    public async Task TaskCompleted_ResolvesRelatedNotification_ButOpeningDoesNot()
    {
        var (svc, ctx, _) = Create(nameof(TaskCompleted_ResolvesRelatedNotification_ButOpeningDoesNot));
        ctx.TaskItems.Add(new TaskItem { Title = "t", Status = "Open", Priority = "Normal" });
        await ctx.SaveChangesAsync();
        var taskId = ctx.TaskItems.Single().TaskItemId;
        await svc.CreateAsync(1, "task", "m", "/Task/Index", "TaskItem", taskId);
        var tasks = new TaskService(ctx, svc);

        await tasks.UpdateStatusAsync(taskId, "InProgress");
        Assert.Single(await svc.GetForUserAsync(1));                   // در جریان بودن ⇒ Resolve نمی‌شود

        await tasks.UpdateStatusAsync(taskId, "Completed", "done");
        Assert.Empty(await svc.GetForUserAsync(1));
        Assert.Single((await svc.GetInboxAsync(1, "dismissed", 1, 25)).Items);
    }

    [Fact]
    public async Task TaskCreate_ProducesNotificationForAssignee()
    {
        var (svc, ctx, bc) = Create(nameof(TaskCreate_ProducesNotificationForAssignee));
        ctx.Users.Add(new ApplicationUser { Id = 11, UserName = "u11", EmployeeId = 100 });
        await ctx.SaveChangesAsync();

        await new TaskService(ctx, svc).CreateAsync("do it", null, 100, null, "Normal", null, null, null, null);

        Assert.Single(bc.Sent);
        Assert.Equal(11, bc.Sent[0].UserId);
        Assert.Equal("TaskItem", bc.Sent[0].N.RelatedType);
    }

    [Fact]
    public async Task LetterReferral_Create_Read_Complete_Lifecycle()
    {
        var (svc, ctx, bc) = Create(nameof(LetterReferral_Create_Read_Complete_Lifecycle));
        ctx.Users.Add(new ApplicationUser { Id = 21, UserName = "u21", EmployeeId = 200 });
        ctx.Letters.Add(new Letter { LetterNumber = "L-1", Subject = "s", LetterType = "Internal", Status = "Registered" });
        await ctx.SaveChangesAsync();
        var letterId = ctx.Letters.Single().LetterId;
        var referrals = new LetterReferralService(ctx, svc);

        var referralId = await referrals.CreateAsync(letterId, 200, null, "act", null, "boss");
        Assert.Equal(21, bc.Sent.Single().UserId);
        Assert.Equal(1, await svc.GetUnreadCountAsync(21));

        await referrals.MarkReadAsync(referralId, 21);                  // گیرنده نامه را باز کرد
        Assert.Equal(0, await svc.GetUnreadCountAsync(21));
        Assert.Single(await svc.GetForUserAsync(21));                   // Read، نه Dismissed

        await referrals.CompleteAsync(referralId, "ok");                // ارجاع انجام شد
        Assert.Empty(await svc.GetForUserAsync(21));
    }

    // ---------------- SignalR ----------------

    private sealed class FakeCaller : HubCallerContext
    {
        private readonly ClaimsPrincipal? _user;
        public FakeCaller(string connectionId, ClaimsPrincipal? user) { ConnectionId = connectionId; _user = user; }
        public override string ConnectionId { get; }
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<(string ConnectionId, string Group)> Added { get; } = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        { Added.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Principal(string? id) =>
        id == null ? new ClaimsPrincipal(new ClaimsIdentity())
                   : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));

    [Fact]
    public void Hub_RequiresAuthentication()
    {
        Assert.NotNull(typeof(NotificationHub).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
    }

    [Fact]
    public void Hub_JoinUserGroup_AcceptsNoClientSuppliedUserId()
    {
        Assert.Empty(typeof(NotificationHub).GetMethod(nameof(NotificationHub.JoinUserGroup))!.GetParameters());
    }

    [Fact]
    public async Task Hub_OnConnected_JoinsOnlyGroupOfAuthenticatedIdentity()
    {
        var groups = new RecordingGroups();
        var hub = new NotificationHub { Context = new FakeCaller("conn-A", Principal("1")), Groups = groups };

        await hub.OnConnectedAsync();

        Assert.Equal(new[] { ("conn-A", "user-1") }, groups.Added.ToArray());
        Assert.DoesNotContain(groups.Added, a => a.Group == "user-2");
    }

    [Fact]
    public async Task Hub_WithoutIdentityClaim_JoinsNoGroup()
    {
        var groups = new RecordingGroups();
        var hub = new NotificationHub { Context = new FakeCaller("conn-X", Principal(null)), Groups = groups };

        await hub.OnConnectedAsync();

        Assert.Empty(groups.Added);
    }

    private sealed class RecordingProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Calls { get; } = new();
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
        { Calls.Add((method, args)); return Task.CompletedTask; }
    }

    private sealed class FakeClients : IHubClients
    {
        public Dictionary<string, RecordingProxy> Groups { get; } = new();
        public IClientProxy Group(string groupName)
        {
            if (!Groups.TryGetValue(groupName, out var p)) Groups[groupName] = p = new RecordingProxy();
            return p;
        }
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> c) => throw new NotSupportedException();
        public IClientProxy Client(string c) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> c) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string g, IReadOnlyList<string> c) => throw new NotSupportedException();
        public IClientProxy Groups_(IReadOnlyList<string> g) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> g) => throw new NotSupportedException();
        public IClientProxy User(string u) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> u) => throw new NotSupportedException();
    }

    private sealed class FakeHubContext : IHubContext<NotificationHub>
    {
        public FakeClients FakeClients { get; } = new();
        public IHubClients Clients => FakeClients;
        public IGroupManager Groups => new RecordingGroups();
    }

    [Fact]
    public async Task Broadcaster_SendsOnlyToRecipientGroup_WithNotificationIdAndCount()
    {
        var ctx = new FakeHubContext();
        var b = new SignalRNotificationBroadcaster(ctx);

        await b.BroadcastNotificationAsync(1, new NotificationDto { NotificationId = 42, Title = "t", RelatedType = "Letter", RelatedId = 3 }, 5);
        await b.BroadcastUnreadCountAsync(1, 4);

        Assert.Equal(new[] { "user-1" }, ctx.FakeClients.Groups.Keys.ToArray());
        var calls = ctx.FakeClients.Groups["user-1"].Calls;
        Assert.Equal("ReceiveNotification", calls[0].Method);
        Assert.Equal(5, calls[0].Args[1]);
        Assert.Contains("NotificationId = 42", calls[0].Args[0]!.ToString());
        Assert.Equal(("UnreadCountChanged", 4), (calls[1].Method, calls[1].Args[0]));
    }
}
