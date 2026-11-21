using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cartable;

/// <summary>
/// سرویس‌های کارتابل روی InMemory با دنیای LetterTestWorld (کاربر 101..107 ↔ کارمند 1..7).
/// تمرکز: وضعیت شخصی (سطل/آرشیو) هرگز نامه را عوض نمی‌کند، پوشه‌ها محرمانگی را دور نمی‌زنند، چارت سازمانی منبع زیردستان است.
/// </summary>
public class CartableServicesTests
{
    private const int U1 = 101, U2 = 102, U3 = 103, U4 = 104, U5 = 105;
    private static int _seq = 5000;

    private static PEMSDbContext World(string name) => LetterTestWorld.NewContext("Cartable_" + name);

    private static Letter Sent(PEMSDbContext ctx, int author, string subject = "S", string conf = "Internal", string status = "Sent", int? parent = null, int[]? to = null)
    {
        var l = new Letter
        {
            LetterId = ++_seq, LetterType = "Internal", LetterNumber = "N-" + _seq, LetterDate = DateTime.Today, Subject = subject,
            ConfidentialityLevel = conf, Status = status, RegisteredById = author, ParentLetterId = parent, IsActive = true
        };
        foreach (var e in to ?? Array.Empty<int>()) l.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = e });
        ctx.Letters.Add(l);
        ctx.SaveChanges();
        return l;
    }

    private static LetterService Letters(PEMSDbContext ctx) => new(ctx, sanitizer: null!, knowledgeBaseService: null!);

    private static Task<PagedResult<LetterDto>> List(LetterService s, int user, string? folder, bool canViewConfidential = false, LetterAdvancedFilter? f = null)
    {
        f ??= new LetterAdvancedFilter();
        f.Folder = folder;
        return s.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, canViewConfidential, user, false, f);
    }

    /// <summary>سلسله‌مراتب: سمت A(مدیر) ← B(سرپرست) ← C(کارشناس)؛ کارمند 1=A، 2=B، 3=C، 4 هم‌تراز C.</summary>
    private static void Org(PEMSDbContext ctx)
    {
        ctx.Positions.AddRange(
            new Position { PositionId = 11, Title = "Manager", IsActive = true },
            new Position { PositionId = 12, Title = "Supervisor", ParentPositionId = 11, IsActive = true },
            new Position { PositionId = 13, Title = "Engineer", ParentPositionId = 12, IsActive = true },
            new Position { PositionId = 14, Title = "Engineer 2", ParentPositionId = 12, IsActive = true });
        void Hold(int pos, int emp) => ctx.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = pos, EmployeeId = emp, IsPrimary = true, IsActive = true, StartDate = DateTime.Today.AddYears(-1) });
        Hold(11, 1); Hold(12, 2); Hold(13, 3); Hold(14, 4);
        ctx.SaveChanges();
    }

    // ------------------------------------------------------------------ folders
    [Fact]
    public async Task Folders_InboxSentDraftsSeparateByRole()
    {
        var ctx = World(nameof(Folders_InboxSentDraftsSeparateByRole));
        var sent = Sent(ctx, U1, "to-2", to: new[] { 2 });
        var draft = Sent(ctx, U1, "draft", status: "Draft");
        var s = Letters(ctx);

        Assert.Equal(sent.LetterId, Assert.Single((await List(s, U2, LetterFolders.Inbox)).Items).LetterId);
        Assert.Empty((await List(s, U1, LetterFolders.Inbox)).Items);
        Assert.Equal(sent.LetterId, Assert.Single((await List(s, U1, LetterFolders.Sent)).Items).LetterId);
        Assert.Equal(draft.LetterId, Assert.Single((await List(s, U1, LetterFolders.Drafts)).Items).LetterId);
        Assert.Empty((await List(s, U2, LetterFolders.Drafts)).Items);
    }

    [Fact]
    public async Task Folders_NeverWidenConfidentiality()
    {
        var ctx = World(nameof(Folders_NeverWidenConfidentiality));
        Sent(ctx, U1, "secret", conf: "Restricted", to: new[] { 2 });
        var s = Letters(ctx);

        // کاربر 5 گیرنده نیست و محرمانه را نمی‌بیند: هیچ پوشه‌ای (حتی «همه») آن را نشان نمی‌دهد
        foreach (var f in LetterFolders.Ordered) Assert.Empty((await List(s, U5, f)).Items);
        Assert.Single((await List(s, U2, LetterFolders.Inbox)).Items);   // گیرنده می‌بیند
    }

    [Fact]
    public async Task Trash_HidesOnlyForTheUser_AndNeverTouchesTheLetter()
    {
        var ctx = World(nameof(Trash_HidesOnlyForTheUser_AndNeverTouchesTheLetter));
        var l = Sent(ctx, U1, "x", to: new[] { 2, 3 });
        var s = Letters(ctx);
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));

        var r = await svc.TrashAsync(new[] { l.LetterId }, U2, false);
        Assert.True(r.Success);

        Assert.Empty((await List(s, U2, LetterFolders.Inbox)).Items);
        Assert.Empty((await List(s, U2, null)).Items);
        Assert.Single((await List(s, U2, LetterFolders.Trash)).Items);
        Assert.Single((await List(s, U2, LetterFolders.All)).Items.Where(i => i.IsTrashed));
        Assert.Single((await List(s, U3, LetterFolders.Inbox)).Items);     // دیگران دست‌نخورده
        Assert.Single((await List(s, U1, LetterFolders.Sent)).Items);

        var fresh = ctx.Letters.Single(x => x.LetterId == l.LetterId);
        Assert.True(fresh.IsActive);
        Assert.Equal("Sent", fresh.Status);
    }

    [Fact]
    public async Task Trash_RestoreBringsItBack_PurgeIsPermanentButOnlyFromTrash()
    {
        var ctx = World(nameof(Trash_RestoreBringsItBack_PurgeIsPermanentButOnlyFromTrash));
        var l = Sent(ctx, U1, "x", to: new[] { 2 });
        var s = Letters(ctx);
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));

        Assert.False((await svc.PurgeAsync(new[] { l.LetterId }, U2)).Success);   // هنوز در سطل نیست
        await svc.TrashAsync(new[] { l.LetterId }, U2, false);
        Assert.True((await svc.RestoreAsync(new[] { l.LetterId }, U2)).Success);
        Assert.Single((await List(s, U2, LetterFolders.Inbox)).Items);

        await svc.TrashAsync(new[] { l.LetterId }, U2, false);
        Assert.True((await svc.PurgeAsync(new[] { l.LetterId }, U2)).Success);
        Assert.Empty((await List(s, U2, LetterFolders.Trash)).Items);
        Assert.Empty((await List(s, U2, LetterFolders.All)).Items);
        Assert.False((await svc.RestoreAsync(new[] { l.LetterId }, U2)).Success);   // حذف دائم قابل بازیابی نیست
        Assert.True(ctx.Letters.Any(x => x.LetterId == l.LetterId && x.IsActive));      // خود نامه سر جایش است
    }

    [Fact]
    public async Task Trash_CannotTrashWhatYouCannotSee_AndEmptyTrashPurgesAll()
    {
        var ctx = World(nameof(Trash_CannotTrashWhatYouCannotSee_AndEmptyTrashPurgesAll));
        var hidden = Sent(ctx, U1, "secret", conf: "Restricted", to: new[] { 2 });
        var a = Sent(ctx, U1, "a", to: new[] { 5 }); var b = Sent(ctx, U1, "b", to: new[] { 5 });
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));

        Assert.False((await svc.TrashAsync(new[] { hidden.LetterId }, U5, false)).Success);
        Assert.Empty(ctx.LetterUserStates);

        await svc.TrashAsync(new[] { a.LetterId, b.LetterId }, U5, false);
        var empty = await svc.EmptyTrashAsync(U5);
        Assert.Equal(2, empty.Affected);
        Assert.False((await svc.EmptyTrashAsync(U5)).Success);
    }

    [Fact]
    public async Task Archive_IsPersonal_AndRevivesOnNewActivity()
    {
        var ctx = World(nameof(Archive_IsPersonal_AndRevivesOnNewActivity));
        var l = Sent(ctx, U1, "thread", to: new[] { 2 });
        var s = Letters(ctx);
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));

        await svc.ArchiveForMeAsync(new[] { l.LetterId }, U2, false);
        Assert.Empty((await List(s, U2, LetterFolders.Inbox)).Items);
        var archived = Assert.Single((await List(s, U2, LetterFolders.Archive)).Items);
        Assert.True(archived.IsArchivedByMe);
        Assert.False(archived.IsRevived);

        // بعد از آرشیو، فعالیت تازه (ارجاع) ثبت می‌شود → «احیاشده»
        var state = ctx.LetterUserStates.Single();
        state.ArchivedDate = DateTime.Now.AddHours(-2);
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l.LetterId, ReferredToEmployeeId = 2, Status = "Pending", ReferredDate = DateTime.Now.AddHours(-1) });
        await ctx.SaveChangesAsync();
        Assert.True(Assert.Single((await List(s, U2, LetterFolders.Archive)).Items).IsRevived);

        Assert.True((await svc.UnarchiveForMeAsync(new[] { l.LetterId }, U2)).Success);
        Assert.Single((await List(s, U2, LetterFolders.Inbox)).Items);
    }

    [Fact]
    public async Task SetRead_TogglesOnlyMyRecipientRow()
    {
        var ctx = World(nameof(SetRead_TogglesOnlyMyRecipientRow));
        var l = Sent(ctx, U1, "x", to: new[] { 2, 3 });
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));

        Assert.True((await svc.SetReadAsync(new[] { l.LetterId }, U2, true, false)).Success);
        Assert.True(ctx.LetterRecipients.Single(r => r.EmployeeId == 2).IsRead);
        Assert.False(ctx.LetterRecipients.Single(r => r.EmployeeId == 3).IsRead);
        Assert.False((await svc.SetReadAsync(new[] { l.LetterId }, U2, true, false)).Success);   // بدون تغییر
        Assert.True((await svc.SetReadAsync(new[] { l.LetterId }, U2, false, false)).Success);
        Assert.False(ctx.LetterRecipients.Single(r => r.EmployeeId == 2).IsRead);
        Assert.False((await svc.SetReadAsync(new[] { l.LetterId }, U5, true, false)).Success);    // گیرنده نیست
    }

    // ------------------------------------------------------------------ counts
    [Fact]
    public async Task Counts_MatchTheFolders()
    {
        var ctx = World(nameof(Counts_MatchTheFolders));
        Sent(ctx, U1, "a", to: new[] { 2 }); Sent(ctx, U1, "b", to: new[] { 2 }); Sent(ctx, U1, "d", status: "Draft");
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));
        ctx.LetterRecipients.First(r => r.EmployeeId == 2).IsRead = true;
        await ctx.SaveChangesAsync();

        var c2 = await svc.GetCountsAsync(U2, false);
        Assert.Equal(2, c2.Inbox);
        Assert.Equal(1, c2.InboxUnread);
        var c1 = await svc.GetCountsAsync(U1, false);
        Assert.Equal(2, c1.Sent);
        Assert.Equal(1, c1.Drafts);
        Assert.Equal(0, c1.Trash);
    }

    // ------------------------------------------------------------------ threads
    [Fact]
    public async Task Thread_CountsOnlyVisibleMembers_AndRootsOnlyCollapsesReplies()
    {
        var ctx = World(nameof(Thread_CountsOnlyVisibleMembers_AndRootsOnlyCollapsesReplies));
        var root = Sent(ctx, U1, "root", to: new[] { 2, 5 });
        var reply = Sent(ctx, U2, "re: root", parent: root.LetterId, to: new[] { 1 });
        var secret = Sent(ctx, U2, "re: secret", conf: "Restricted", parent: reply.LetterId, to: new[] { 1 });
        var s = Letters(ctx);

        var flat = await List(s, U1, LetterFolders.All, canViewConfidential: false);
        Assert.Equal(3, flat.Items.Count);
        // کاربر ۵ نامه‌ی محرمانه را نمی‌بیند: رشته برایش فقط دو عضو دارد (ریشه + پاسخ داخلی)
        var five = await List(s, U5, null);
        Assert.Equal(2, five.Items.Count);
        var rootDto = five.Items.Single(i => i.LetterId == root.LetterId);
        Assert.Equal(2, rootDto.ThreadCount);

        var asRoots = await List(s, U1, LetterFolders.All, canViewConfidential: true, f: new LetterAdvancedFilter { ThreadRootsOnly = true });
        var r = Assert.Single(asRoots.Items);
        Assert.Equal(root.LetterId, r.LetterId);
        Assert.Equal(3, r.ThreadCount);

        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));
        var members = await svc.GetThreadAsync(reply.LetterId, U5, canViewConfidential: false);
        Assert.Equal(new[] { root.LetterId, reply.LetterId }, members.Select(m => m.LetterId));
        Assert.Equal(new[] { 0, 1 }, members.Select(m => m.Depth));
        Assert.Single(members.Where(m => m.IsCurrent), m => m.LetterId == reply.LetterId);
        Assert.DoesNotContain(members, m => m.LetterId == secret.LetterId);
    }

    // ------------------------------------------------------------------ sort / filters
    [Fact]
    public async Task Sorting_BySubjectAscendingAndPriority()
    {
        var ctx = World(nameof(Sorting_BySubjectAscendingAndPriority));
        Sent(ctx, U1, "b", to: new[] { 2 }); Sent(ctx, U1, "a", to: new[] { 2 }); Sent(ctx, U1, "c", to: new[] { 2 });
        var urgent = Sent(ctx, U1, "u", to: new[] { 2 }); urgent.Priority = "Urgent"; await ctx.SaveChangesAsync();
        var s = Letters(ctx);

        var bySubject = await List(s, U2, LetterFolders.Inbox, f: new LetterAdvancedFilter { SortBy = "subject", SortDescending = false });
        Assert.Equal(new[] { "a", "b", "c", "u" }, bySubject.Items.Select(i => i.Subject));
        var byPriority = await List(s, U2, LetterFolders.Inbox, f: new LetterAdvancedFilter { SortBy = "priority", SortDescending = true });
        Assert.Equal("u", byPriority.Items.First().Subject);
    }

    [Fact]
    public async Task TagFilter_IncludesSubtree_AndHidesOthersPersonalTags()
    {
        var ctx = World(nameof(TagFilter_IncludesSubtree_AndHidesOthersPersonalTags));
        var a = Sent(ctx, U1, "a", to: new[] { 2 }); var b = Sent(ctx, U1, "b", to: new[] { 2 }); var c = Sent(ctx, U1, "c", to: new[] { 2 });
        ctx.Tags.AddRange(new Tag { TagId = 1, Name = "Engineering" }, new Tag { TagId = 2, Name = "Electrical", ParentTagId = 1 },
            new Tag { TagId = 3, Name = "mine", OwnerUserId = U2 }, new Tag { TagId = 4, Name = "theirs", OwnerUserId = U3 });
        ctx.LetterTags.AddRange(new LetterTag { LetterId = a.LetterId, TagId = 1 }, new LetterTag { LetterId = b.LetterId, TagId = 2 },
            new LetterTag { LetterId = c.LetterId, TagId = 4 });
        await ctx.SaveChangesAsync();
        var s = Letters(ctx);

        var parent = await List(s, U2, LetterFolders.Inbox, f: new LetterAdvancedFilter { TagId = 1 });
        Assert.Equal(new[] { a.LetterId, b.LetterId }.OrderBy(x => x), parent.Items.Select(i => i.LetterId).OrderBy(x => x));
        Assert.Single((await List(s, U2, LetterFolders.Inbox, f: new LetterAdvancedFilter { TagId = 2 })).Items);
        // برچسب شخصیِ دیگری: نتیجه‌ی خالی و در برچسب‌های ردیف هم دیده نمی‌شود
        Assert.Empty((await List(s, U2, LetterFolders.Inbox, f: new LetterAdvancedFilter { TagId = 4 })).Items);
        Assert.DoesNotContain((await List(s, U2, LetterFolders.Inbox)).Items.SelectMany(i => i.Tags), t => t.Name == "theirs");
    }

    // ------------------------------------------------------------------ tag tree service
    [Fact]
    public async Task TagTree_CreateUpdateDeactivateWithPermissionsAndScope()
    {
        var ctx = World(nameof(TagTree_CreateUpdateDeactivateWithPermissionsAndScope));
        var svc = new LetterTagTreeService(ctx);

        Assert.False((await svc.CreateAsync("Org", "primary", null, personal: false, U2, canManageOrganizational: false)).Success);
        var org = await svc.CreateAsync("Engineering", "primary", null, false, U1, true);
        Assert.True(org.Success);
        var child = await svc.CreateAsync("Electrical", "bogus", org.TagId, false, U1, true);
        Assert.Equal("secondary", ctx.Tags.Single(t => t.TagId == child.TagId).Color);   // رنگ ناشناخته → پیش‌فرض
        Assert.Equal("Tag_Err_Duplicate", (await svc.CreateAsync("Electrical", null, org.TagId, false, U1, true)).ErrorKey);
        var mine = await svc.CreateAsync("Mine", null, org.TagId, personal: true, U2, false);
        Assert.True(mine.Success);

        // بیننده‌ها: کاربر ۲ سازمانی + شخصی خودش را می‌بیند؛ کاربر ۳ شخصی کاربر ۲ را نه
        var tree2 = await svc.GetTreeAsync(U2, false, false, false);
        Assert.Equal(new[] { "Engineering", "Electrical", "Mine" }, tree2.Select(n => n.Name));
        Assert.Equal(new[] { 1, 2, 2 }, tree2.Select(n => n.Depth));
        Assert.True(tree2.Single(n => n.Name == "Mine").CanEdit);
        Assert.False(tree2.Single(n => n.Name == "Engineering").CanEdit);
        Assert.DoesNotContain(await svc.GetTreeAsync(U3, false, false, false), n => n.Name == "Mine");

        // ویرایش: دور و عمق و مجوز
        Assert.Equal(TagTreeRules.ErrCycle, (await svc.UpdateAsync(org.TagId, "Engineering", null, child.TagId, 0, U1, true)).ErrorKey);
        Assert.Equal("Tag_Err_NoPermission", (await svc.UpdateAsync(mine.TagId, "x", null, org.TagId, 0, U3, true)).ErrorKey);
        Assert.Equal("Tag_Err_HasChildren", (await svc.DeactivateAsync(org.TagId, U1, true)).ErrorKey);
        Assert.True((await svc.DeactivateAsync(child.TagId, U1, true)).Success);
        Assert.Equal("Tag_Err_HasChildren", (await svc.DeactivateAsync(org.TagId, U1, true)).ErrorKey);   // «Mine» (شخصی) هنوز زیرمجموعه‌ی فعال است
        Assert.True((await svc.DeactivateAsync(mine.TagId, U2, false)).Success);
        Assert.True((await svc.DeactivateAsync(org.TagId, U1, true)).Success);
    }

    [Fact]
    public async Task TagTree_ApplyAttachesOnlyToVisibleLetters_AndRespectsPersonalOwnership()
    {
        var ctx = World(nameof(TagTree_ApplyAttachesOnlyToVisibleLetters_AndRespectsPersonalOwnership));
        var ok = Sent(ctx, U1, "ok", to: new[] { 2 });
        var secret = Sent(ctx, U1, "secret", conf: "Restricted", to: new[] { 3 });
        ctx.Tags.AddRange(new Tag { TagId = 1, Name = "Org" }, new Tag { TagId = 2, Name = "Mine", OwnerUserId = U2 });
        await ctx.SaveChangesAsync();
        var svc = new LetterTagTreeService(ctx);

        var r = await svc.ApplyAsync(1, new[] { ok.LetterId, secret.LetterId }, attach: true, U2, canViewConfidential: false);
        Assert.Equal(1, r.Affected);
        Assert.Single(ctx.LetterTags);
        Assert.False((await svc.ApplyAsync(2, new[] { ok.LetterId }, true, U3, false)).Success);   // شخصی دیگری
        Assert.True((await svc.ApplyAsync(1, new[] { ok.LetterId }, attach: false, U2, false)).Success);
        Assert.Empty(ctx.LetterTags);
    }

    // ------------------------------------------------------------------ org chart
    [Fact]
    public async Task OrgResolver_SubordinatesManagersAndPeersComeFromPositions()
    {
        var ctx = World(nameof(OrgResolver_SubordinatesManagersAndPeersComeFromPositions));
        Org(ctx);
        var org = new OrganizationSubordinateResolver(ctx);

        var manager = await org.GetRelationsAsync(1);
        Assert.Equal(new[] { 2, 3, 4 }, manager.Subordinates.Select(s => s.EmployeeId).OrderBy(x => x));
        Assert.Equal(1, manager.Subordinates.Single(s => s.EmployeeId == 2).Depth);
        Assert.Equal(2, manager.Subordinates.Single(s => s.EmployeeId == 3).Depth);
        Assert.Empty(manager.ManagerEmployeeIds);

        var engineer = await org.GetRelationsAsync(3);
        Assert.Equal(new[] { 2, 1 }, engineer.ManagerEmployeeIds);   // از نزدیک به دور
        Assert.Equal(new[] { 4 }, engineer.PeerEmployeeIds);
        Assert.Empty(engineer.Subordinates);

        Assert.Equal(2, (await org.GetSubordinatesAsync(2, includeIndirect: false)).Count);
        // بدون سمت اصلی معتبر هیچ رابطه‌ای حدس زده نمی‌شود
        var none = await org.GetRelationsAsync(7);
        Assert.Empty(none.Subordinates); Assert.Empty(none.ManagerEmployeeIds); Assert.Empty(none.PeerEmployeeIds);
    }


    [Fact]
    public async Task OrgResolver_IgnoresExpiredAndInactiveAssignmentsAndCycles()
    {
        var ctx = World(nameof(OrgResolver_IgnoresExpiredAndInactiveAssignmentsAndCycles));
        Org(ctx);
        // کارمند ۴: تخصیص منقضی → دیگر زیردست ۲ نیست
        ctx.EmployeeAssignments.Single(a => a.EmployeeId == 4).EndDate = DateTime.Today.AddDays(-1);
        // دور: مدیر ← کارشناس
        ctx.Positions.Single(p => p.PositionId == 11).ParentPositionId = 13;
        await ctx.SaveChangesAsync();

        var rel = await new OrganizationSubordinateResolver(ctx).GetRelationsAsync(2);
        Assert.DoesNotContain(rel.Subordinates, s => s.EmployeeId == 4);
        Assert.Contains(rel.Subordinates, s => s.EmployeeId == 3);
    }

    // ------------------------------------------------------------------ history filter via org
    [Fact]
    public async Task FilterHistory_UsesOrgChartForManagerAndSubordinates()
    {
        var ctx = World(nameof(FilterHistory_UsesOrgChartForManagerAndSubordinates));
        Org(ctx);
        var svc = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));
        var events = new List<LetterTimelineEventDto>
        {
            new() { EventDate = DateTime.Today, EventType = "Created", ActorEmployeeId = 3 },    // خودم (کارشناس)
            new() { EventDate = DateTime.Today.AddHours(1), EventType = "Read", ActorEmployeeId = 2 },     // مدیر مستقیم
            new() { EventDate = DateTime.Today.AddHours(2), EventType = "Read", ActorEmployeeId = 5 }      // نامرتبط
        };

        Assert.Equal(new[] { "Created" }, (await svc.FilterHistoryAsync(events, "me", false, U3)).Select(e => e.EventType));
        Assert.Equal(2, (await svc.FilterHistoryAsync(events, "manager", false, U3)).Single().ActorEmployeeId);
        Assert.Empty(await svc.FilterHistoryAsync(events, "subordinates", false, U3));
        Assert.Equal(new[] { 3 }, (await svc.FilterHistoryAsync(events, "subordinates", false, 102)).Select(e => e.ActorEmployeeId!.Value));   // کاربر ۲ = مدیر کارمند ۳
    }
}
