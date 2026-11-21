using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cartable;

/// <summary>تفویض، قانون مسیریابی، مخاطبین/گروه‌ها، یادآور شخصی، تنظیمات و گزارش/SLA (چارت سازمانی: 1=مدیر ← 2=سرپرست ← 3،4=کارشناس).</summary>
public class CartableSettingsServicesTests
{
    private const int U1 = 101, U2 = 102, U3 = 103, U4 = 104, U5 = 105;
    private static int _seq = 9000;

    private static PEMSDbContext World(string name)
    {
        var ctx = LetterTestWorld.NewContext("CartableSet_" + name);
        ctx.Positions.AddRange(
            new Position { PositionId = 11, Title = "Manager", IsActive = true },
            new Position { PositionId = 12, Title = "Supervisor", ParentPositionId = 11, IsActive = true },
            new Position { PositionId = 13, Title = "Engineer", ParentPositionId = 12, IsActive = true },
            new Position { PositionId = 14, Title = "Engineer 2", ParentPositionId = 12, IsActive = true });
        void Hold(int pos, int emp) => ctx.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = pos, EmployeeId = emp, IsPrimary = true, IsActive = true, StartDate = DateTime.Today.AddYears(-1) });
        Hold(11, 1); Hold(12, 2); Hold(13, 3); Hold(14, 4);
        ctx.SaveChanges();
        return ctx;
    }

    private static OrganizationSubordinateResolver Org(PEMSDbContext c) => new(c);

    private static Letter Sent(PEMSDbContext ctx, int author, int[] to, string subject = "S", string conf = "Internal", string priority = "Normal",
        string status = "Sent", bool responseRequired = false, DateTime? due = null, int? parent = null, DateTime? date = null, int? disciplineId = null)
    {
        var l = new Letter
        {
            LetterId = ++_seq, LetterType = "Internal", LetterNumber = "N-" + _seq, LetterDate = date ?? DateTime.Today, Subject = subject, Priority = priority,
            ConfidentialityLevel = conf, Status = status, RegisteredById = author, ParentLetterId = parent, IsActive = true,
            ResponseRequired = responseRequired, ResponseDueDate = due, DisciplineId = disciplineId
        };
        foreach (var e in to) l.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = e });
        ctx.Letters.Add(l);
        ctx.SaveChanges();
        return l;
    }

    // ------------------------------------------------------------------ delegation
    [Fact]
    public async Task Delegation_CreateValidatesAndGivesViewOnlyDelegatedFolder()
    {
        var ctx = World(nameof(Delegation_CreateValidatesAndGivesViewOnlyDelegatedFolder));
        var svc = new LetterDelegationService(ctx, Org(ctx));

        Assert.Equal(LetterDelegationRules.ErrSelf, (await svc.CreateAsync(U2, U2, DateTime.Today, DateTime.Today.AddDays(3), null)).ErrorKey);
        Assert.Equal("Del_Err_DelegateInvalid", (await svc.CreateAsync(U2, 999, DateTime.Today, DateTime.Today.AddDays(3), null)).ErrorKey);
        var ok = await svc.CreateAsync(U2, U3, DateTime.Today, DateTime.Today.AddDays(3), "مرخصی");
        Assert.True(ok.Success);
        Assert.Equal(LetterDelegationRules.ErrOverlap, (await svc.CreateAsync(U2, U3, DateTime.Today.AddDays(1), DateTime.Today.AddDays(2), null)).ErrorKey);

        var letterForDelegator = Sent(ctx, U1, new[] { 2 }, "for-2");
        var hidden = Sent(ctx, U1, new[] { 2 }, "secret", conf: "Restricted");
        var letters = new LetterService(ctx, sanitizer: null!, knowledgeBaseService: null!);

        // کاربر ۳ بدون تفویض کارتابل کاربر ۲ را نمی‌بیند؛ با تفویض، فقط نامه‌های قابل‌دیدن برای خودش (محرمانه نه)
        var mine = await letters.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, false, U3, false, new LetterAdvancedFilter { Folder = LetterFolders.Delegated });
        Assert.Contains(mine.Items, i => i.LetterId == letterForDelegator.LetterId);
        Assert.DoesNotContain(mine.Items, i => i.LetterId == hidden.LetterId);

        var (given, received) = await svc.GetAsync(U3);
        Assert.Empty(given);
        Assert.True(Assert.Single(received).IsEffectiveNow);
        Assert.Equal("مرخصی", received[0].Note);

        // لغو: فقط تفویض‌کننده؛ بعد از لغو پوشه‌ی تفویضی خالی است
        Assert.False((await svc.RevokeAsync(ok.Id, U3)).Success);
        Assert.True((await svc.RevokeAsync(ok.Id, U2)).Success);
        var after = await letters.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, false, U3, false, new LetterAdvancedFilter { Folder = LetterFolders.Delegated });
        Assert.Empty(after.Items);
        Assert.False(ctx.LetterDelegations.Single().IsActive);
        Assert.NotNull(ctx.LetterDelegations.Single().RevokedDate);   // ردیف حذف نمی‌شود
    }

    [Fact]
    public async Task Delegation_ExpiredWindowGivesNothing()
    {
        var ctx = World(nameof(Delegation_ExpiredWindowGivesNothing));
        ctx.LetterDelegations.Add(new LetterDelegation { DelegatorUserId = U2, DelegateUserId = U3, FromDate = DateTime.Today.AddDays(-10), ToDate = DateTime.Today.AddDays(-5), IsActive = true });
        await ctx.SaveChangesAsync();
        Sent(ctx, U1, new[] { 2 });
        var letters = new LetterService(ctx, sanitizer: null!, knowledgeBaseService: null!);

        var r = await letters.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, false, U3, false, new LetterAdvancedFilter { Folder = LetterFolders.Delegated });
        Assert.Empty(r.Items);
    }

    [Fact]
    public async Task Delegation_CandidatesAreLabelledByOrgRelation()
    {
        var ctx = World(nameof(Delegation_CandidatesAreLabelledByOrgRelation));
        var cands = await new LetterDelegationService(ctx, Org(ctx)).GetCandidatesAsync(U3);   // کارمند 3: مدیر مستقیم=2، مدیر بالاتر=1، هم‌تراز=4
        Assert.DoesNotContain(cands, c => c.UserId == U3);
        Assert.Equal("Manager", cands.Single(c => c.UserId == U2).Relation);
        Assert.Equal("Manager", cands.Single(c => c.UserId == U1).Relation);
        Assert.Equal("Peer", cands.Single(c => c.UserId == U4).Relation);
        Assert.Equal("Other", cands.Single(c => c.UserId == U5).Relation);
        Assert.Equal("Manager", cands.First().Relation);   // مدیران اول
    }

    // ------------------------------------------------------------------ routing rules
    private static LetterRoutingRuleService Rules(PEMSDbContext ctx) => new(ctx, Org(ctx), new RecordingReferrals(ctx));

    private sealed class RecordingReferrals : PEMS.Application.Interfaces.ILetterReferralService
    {
        private readonly PEMSDbContext _ctx;
        public RecordingReferrals(PEMSDbContext ctx) => _ctx = ctx;
        public Task<int> CreateAsync(int letterId, int? employee, int? dept, string? instructions, DateTime? due, string? by)
        {
            var r = new LetterReferral { LetterId = letterId, ReferredToEmployeeId = employee, Instructions = instructions, ReferredByName = by, Status = "Pending", ReferredDate = DateTime.Now };
            _ctx.LetterReferrals.Add(r); _ctx.SaveChanges();
            return Task.FromResult(r.LetterReferralId);
        }
        public Task<List<LetterReferralDto>> GetForLetterAsync(int letterId) => throw new NotImplementedException();
        public Task<List<LetterDto>> GetMyReferralsAsync(int applicationUserId, bool openOnly = true) => throw new NotImplementedException();
        public Task MarkReadAsync(int referralId, int applicationUserId) => throw new NotImplementedException();
        public Task CompleteAsync(int referralId, string? responseNote) => throw new NotImplementedException();
    }

    [Fact]
    public async Task RoutingRule_SaveValidatesConditionsActionsAndSubordinateTarget()
    {
        var ctx = World(nameof(RoutingRule_SaveValidatesConditionsActionsAndSubordinateTarget));
        ctx.Tags.AddRange(new Tag { TagId = 1, Name = "Org" }, new Tag { TagId = 2, Name = "Theirs", OwnerUserId = U1 });
        await ctx.SaveChangesAsync();
        var svc = Rules(ctx);

        Assert.Equal("Rule_Err_Name", (await svc.SaveAsync(new LetterRoutingRuleDto { MatchPriority = "Urgent", StarLetter = true }, U2)).ErrorKey);
        Assert.Equal("Rule_Err_NoCondition", (await svc.SaveAsync(new LetterRoutingRuleDto { Name = "r", StarLetter = true }, U2)).ErrorKey);
        Assert.Equal("Rule_Err_NoAction", (await svc.SaveAsync(new LetterRoutingRuleDto { Name = "r", MatchPriority = "Urgent" }, U2)).ErrorKey);
        Assert.Equal("Rule_Err_Tag", (await svc.SaveAsync(new LetterRoutingRuleDto { Name = "r", MatchPriority = "Urgent", ApplyTagId = 2 }, U2)).ErrorKey);   // برچسب شخصی دیگری
        // ارجاع فقط به زیردست: کارمند 2 زیردست 3 را دارد، ولی مدیر (1) زیردست 2 نیست از دید 2
        Assert.Equal("Rule_Err_NotSubordinate", (await svc.SaveAsync(new LetterRoutingRuleDto { Name = "r", MatchPriority = "Urgent", ReferToEmployeeId = 1 }, U2)).ErrorKey);
        Assert.Equal("Rule_Err_NotSubordinate", (await svc.SaveAsync(new LetterRoutingRuleDto { Name = "r", MatchPriority = "Urgent", ReferToEmployeeId = 5 }, U2)).ErrorKey);
        var ok = await svc.SaveAsync(new LetterRoutingRuleDto { Name = "urgent to 3", MatchPriority = "Urgent", ReferToEmployeeId = 3, ApplyTagId = 1, StarLetter = true }, U2);
        Assert.True(ok.Success);

        var list = await svc.GetAsync(U2);
        Assert.Equal("Org", Assert.Single(list).ApplyTagName);
        Assert.Empty(await svc.GetAsync(U3));
        Assert.False((await svc.SetActiveAsync(ok.Id, false, U3)).Success);   // قانون شخصی دیگری
        Assert.False((await svc.DeleteAsync(ok.Id, U3)).Success);
        Assert.True((await svc.SetActiveAsync(ok.Id, false, U2)).Success);
        Assert.True((await svc.DeleteAsync(ok.Id, U2)).Success);
    }

    [Fact]
    public async Task RoutingRule_AppliesTagStarAndReferralToSubordinate_OnlyWhenMatching()
    {
        var ctx = World(nameof(RoutingRule_AppliesTagStarAndReferralToSubordinate_OnlyWhenMatching));
        ctx.Tags.Add(new Tag { TagId = 1, Name = "Urgent-inbox" });
        await ctx.SaveChangesAsync();
        var svc = Rules(ctx);
        var rule = await svc.SaveAsync(new LetterRoutingRuleDto { Name = "urgent", MatchPriority = "Urgent", ApplyTagId = 1, StarLetter = true, ReferToEmployeeId = 3, ReferInstruction = "بررسی شود" }, U2);
        Assert.True(rule.Success);

        var normal = Sent(ctx, U1, new[] { 2 }, priority: "Normal");
        Assert.Equal(0, await svc.ApplyToArrivedLetterAsync(normal.LetterId));
        Assert.Empty(ctx.LetterTags); Assert.Empty(ctx.LetterStars); Assert.Empty(ctx.LetterReferrals);

        var urgent = Sent(ctx, U1, new[] { 2 }, priority: "Urgent");
        Assert.Equal(1, await svc.ApplyToArrivedLetterAsync(urgent.LetterId));
        Assert.Equal(urgent.LetterId, Assert.Single(ctx.LetterTags).LetterId);
        Assert.Equal(U2, Assert.Single(ctx.LetterStars).ApplicationUserId);
        var referral = Assert.Single(ctx.LetterReferrals);
        Assert.Equal(3, referral.ReferredToEmployeeId);
        Assert.Equal("بررسی شود", referral.Instructions);
        Assert.Equal(1, ctx.LetterRoutingRules.Single().MatchCount);

        // اجرای دوباره، ارجاع/برچسب/ستاره‌ی تکراری نمی‌سازد
        await svc.ApplyToArrivedLetterAsync(urgent.LetterId);
        Assert.Single(ctx.LetterTags); Assert.Single(ctx.LetterStars); Assert.Single(ctx.LetterReferrals);
    }

    [Fact]
    public async Task RoutingRule_DoesNotRunForPreSendOrInvisibleOrNotRecipient_AndNeverThrows()
    {
        var ctx = World(nameof(RoutingRule_DoesNotRunForPreSendOrInvisibleOrNotRecipient_AndNeverThrows));
        var svc = Rules(ctx);
        await svc.SaveAsync(new LetterRoutingRuleDto { Name = "all urgent", MatchPriority = "Urgent", StarLetter = true }, U2);

        var draft = Sent(ctx, U1, new[] { 2 }, priority: "Urgent", status: "Draft");
        var otherRecipient = Sent(ctx, U1, new[] { 5 }, priority: "Urgent");
        Assert.Equal(0, await svc.ApplyToArrivedLetterAsync(draft.LetterId));          // پیش‌ازارسال
        Assert.Equal(0, await svc.ApplyToArrivedLetterAsync(otherRecipient.LetterId)); // گیرنده نیست
        Assert.Equal(0, await svc.ApplyToArrivedLetterAsync(-1));                      // وجود ندارد
        Assert.Empty(ctx.LetterStars);
    }

    [Fact]
    public async Task RoutingRule_RespectsConfidentialityOfOwner()
    {
        var ctx = World(nameof(RoutingRule_RespectsConfidentialityOfOwner));
        var svc = Rules(ctx);
        await svc.SaveAsync(new LetterRoutingRuleDto { Name = "mark", MatchPriority = "Urgent", StarLetter = true }, U2);
        // کاربر ۲ گیرنده‌ی نامه‌ی محرمانه است → می‌بیند → اجرا می‌شود (گیرنده بودن اجازه‌ی دیدن می‌دهد)
        var secret = Sent(ctx, U1, new[] { 2 }, priority: "Urgent", conf: "Restricted");
        Assert.Equal(1, await svc.ApplyToArrivedLetterAsync(secret.LetterId));
    }

    // ------------------------------------------------------------------ contacts / groups
    [Fact]
    public async Task Contacts_SearchAndGroups()
    {
        var ctx = World(nameof(Contacts_SearchAndGroups));
        ctx.CorrespondentParties.Add(new CorrespondentParty { CorrespondentPartyId = 1, Name = "ACME Petrochemical", PartyType = "Vendor", IsActive = true });
        await ctx.SaveChangesAsync();
        var svc = new LetterContactService(ctx);

        var hits = await svc.SearchAsync("ACME");
        Assert.Equal("Party", Assert.Single(hits).Kind);
        Assert.Contains(await svc.SearchAsync("E1"), c => c.Kind == "Employee" && c.Id == 1);

        Assert.Equal("Grp_Err_Name", (await svc.SaveGroupAsync(null, " ", true, new[] { 2 }, Array.Empty<int>(), U1, false)).ErrorKey);
        Assert.Equal("Grp_Err_Empty", (await svc.SaveGroupAsync(null, "G", true, Array.Empty<int>(), Array.Empty<int>(), U1, false)).ErrorKey);
        Assert.Equal("Grp_Err_InvalidMember", (await svc.SaveGroupAsync(null, "G", true, new[] { 999 }, Array.Empty<int>(), U1, false)).ErrorKey);
        Assert.Equal("Grp_Err_NoPermission", (await svc.SaveGroupAsync(null, "Org", personal: false, new[] { 2 }, Array.Empty<int>(), U1, canManageOrganizational: false)).ErrorKey);

        var g = await svc.SaveGroupAsync(null, "Project team", true, new[] { 2, 3 }, new[] { 1 }, U1, false);
        Assert.True(g.Success);
        Assert.Equal("Grp_Err_Duplicate", (await svc.SaveGroupAsync(null, "Project team", true, new[] { 2 }, Array.Empty<int>(), U1, false)).ErrorKey);
        var org = await svc.SaveGroupAsync(null, "All engineers", false, new[] { 2, 3, 4 }, Array.Empty<int>(), U5, true);
        Assert.True(org.Success);

        var mine = await svc.GetGroupsAsync(U1, false);
        Assert.Equal(new[] { "All engineers", "Project team" }, mine.Select(x => x.Name).OrderBy(x => x));
        Assert.Equal(3, mine.Single(x => x.Name == "Project team").Members.Count);
        Assert.True(mine.Single(x => x.Name == "Project team").CanEdit);
        Assert.False(mine.Single(x => x.Name == "All engineers").CanEdit);
        Assert.DoesNotContain(await svc.GetGroupsAsync(U2, false), x => x.Name == "Project team");   // شخصی دیگران دیده نمی‌شود

        // ویرایش اعضا جایگزین می‌شود
        Assert.True((await svc.SaveGroupAsync(g.Id, "Project team", true, new[] { 4 }, Array.Empty<int>(), U1, false)).Success);
        Assert.Single((await svc.GetGroupsAsync(U1, false)).Single(x => x.Name == "Project team").Members);
        Assert.False((await svc.DeleteGroupAsync(g.Id, U2, false)).Success);
        Assert.True((await svc.DeleteGroupAsync(g.Id, U1, false)).Success);
        Assert.DoesNotContain(await svc.GetGroupsAsync(U1, false), x => x.Name == "Project team");
    }

    // ------------------------------------------------------------------ personal reminders
    [Fact]
    public async Task Reminders_AddValidatesVisibilityAndDispatchesOnce()
    {
        var ctx = World(nameof(Reminders_AddValidatesVisibilityAndDispatchesOnce));
        var notes = new LetterTestNotifications();
        var svc = new LetterPersonalReminderService(ctx, notes);
        var l = Sent(ctx, U1, new[] { 2 });
        var secret = Sent(ctx, U1, new[] { 3 }, conf: "Restricted");

        Assert.Equal("Rem_Err_Past", (await svc.AddAsync(l.LetterId, DateTime.Now.AddDays(-1), null, U2, false)).ErrorKey);
        Assert.Equal("Rem_Err_NoLetter", (await svc.AddAsync(secret.LetterId, DateTime.Now.AddDays(1), null, U5, false)).ErrorKey);   // نامه‌ی نادیدنی
        var ok = await svc.AddAsync(l.LetterId, DateTime.Now.AddMinutes(30), "پیگیری", U2, false);
        Assert.True(ok.Success);

        Assert.Equal(0, await svc.DispatchDueAsync(DateTime.Now));                        // هنوز نرسیده
        Assert.Equal(1, await svc.DispatchDueAsync(DateTime.Now.AddHours(1)));            // رسیده → یک اعلان
        Assert.Equal(0, await svc.DispatchDueAsync(DateTime.Now.AddHours(2)));            // فقط یک‌بار
        var n = Assert.Single(notes.Sent);
        Assert.Equal(U2, n.UserId);
        Assert.Contains("پیگیری", n.Message);

        Assert.False((await svc.CompleteAsync(ok.Id, U3)).Success);                        // یادآور دیگری
        Assert.True((await svc.CompleteAsync(ok.Id, U2)).Success);
        Assert.Empty(await svc.GetAsync(U2));
        Assert.Single(await svc.GetAsync(U2, includeDone: true));
        Assert.True((await svc.DeleteAsync(ok.Id, U2)).Success);
    }

    [Fact]
    public async Task Reminders_DoNotNotifyWhenAlreadyDoneAndListHidesNowInvisibleLetters()
    {
        var ctx = World(nameof(Reminders_DoNotNotifyWhenAlreadyDoneAndListHidesNowInvisibleLetters));
        var notes = new LetterTestNotifications();
        var svc = new LetterPersonalReminderService(ctx, notes);
        var l = Sent(ctx, U1, new[] { 2 });
        var r = await svc.AddAsync(l.LetterId, DateTime.Now.AddMinutes(5), null, U2, false);
        await svc.CompleteAsync(r.Id, U2);
        Assert.Equal(0, await svc.DispatchDueAsync(DateTime.Now.AddDays(1)));
        Assert.Empty(notes.Sent);

        var secret = Sent(ctx, U1, new[] { 2 }, conf: "Restricted");
        ctx.LetterReminders.Add(new LetterReminder { LetterId = secret.LetterId, OwnerUserId = U5, RemindAt = DateTime.Now.AddDays(1) });
        await ctx.SaveChangesAsync();
        Assert.Empty(await svc.GetAsync(U5));   // محرمانگی بعد از ثبت یادآور هم رعایت می‌شود
    }

    // ------------------------------------------------------------------ settings
    [Fact]
    public async Task UserSettings_PersistSanitized_PerUser()
    {
        var ctx = World(nameof(UserSettings_PersistSanitized_PerUser));
        var svc = new LetterUserSettingsService(ctx);
        Assert.Equal("flat", (await svc.GetAsync(U1)).View);

        await svc.SaveAsync(U1, new LetterUserSettingsRules.Settings { View = "thread", Density = "dense", HiddenColumns = new() { "priority", "evil" }, PageSize = 100 });
        var s = await svc.GetAsync(U1);
        Assert.Equal("thread", s.View);
        Assert.Equal(new[] { "priority" }, s.HiddenColumns);
        Assert.Equal("flat", (await svc.GetAsync(U2)).View);

        await svc.SaveAsync(U1, new LetterUserSettingsRules.Settings { View = "flat" });
        Assert.Single(ctx.LetterUserSettings);   // upsert، نه ردیف جدید
    }

    // ------------------------------------------------------------------ report / SLA
    [Fact]
    public async Task Report_SlaAndReferralsForTheUser_AndSubordinateTableFromOrgChart()
    {
        var ctx = World(nameof(Report_SlaAndReferralsForTheUser_AndSubordinateTableFromOrgChart));
        var today = DateTime.Today;
        // کاربر ۲ (کارمند ۲) سه نامه‌ی پاسخ‌خواه دریافت کرده: پاسخ به‌موقع، پاسخ دیرهنگام، بدون پاسخ و سررسیده
        var onTime = Sent(ctx, U1, new[] { 2 }, responseRequired: true, due: today.AddDays(5), date: today.AddDays(-3));
        var late = Sent(ctx, U1, new[] { 2 }, responseRequired: true, due: today.AddDays(-5), date: today.AddDays(-10));
        var open = Sent(ctx, U1, new[] { 2 }, responseRequired: true, due: today.AddDays(-1), date: today.AddDays(-4));
        var fine = Sent(ctx, U1, new[] { 2 }, responseRequired: true, due: today.AddDays(9), date: today.AddDays(-1));
        Sent(ctx, U2, new[] { 1 }, parent: onTime.LetterId).CreatedDate = DateTime.Now.AddDays(-2);
        Sent(ctx, U2, new[] { 1 }, parent: late.LetterId).CreatedDate = DateTime.Now;
        await ctx.SaveChangesAsync();

        ctx.LetterReferrals.AddRange(
            new LetterReferral { LetterId = open.LetterId, ReferredToEmployeeId = 3, Status = "Pending", ReferredDate = DateTime.Now.AddDays(-4), DueDate = DateTime.Now.AddDays(-1) },
            new LetterReferral { LetterId = fine.LetterId, ReferredToEmployeeId = 3, Status = "Completed", ReferredDate = DateTime.Now.AddDays(-2), CompletedDate = DateTime.Now },
            new LetterReferral { LetterId = fine.LetterId, ReferredToEmployeeId = 2, Status = "Pending", ReferredDate = DateTime.Now.AddDays(-1), DueDate = DateTime.Now.AddDays(3) });
        await ctx.SaveChangesAsync();

        var report = await new LetterReportService(ctx, Org(ctx)).GetAsync(U2, today.AddDays(-30), today, canViewConfidential: false);

        Assert.Equal(4, report.Received);
        Assert.Equal(4, report.Unread);
        Assert.Equal(4, report.ResponseRequired);
        Assert.Equal(1, report.AnsweredOnTime);
        Assert.Equal(1, report.AnsweredLate);
        Assert.Equal(1, report.OpenOverdue);
        Assert.Equal(1, report.OpenWithinSla);
        Assert.Equal(33.3, report.OnTimePercent);
        Assert.Equal(1, report.OpenReferrals);      // فقط ارجاعِ خودِ کاربر ۲
        Assert.Equal(0, report.OverdueReferrals);
        Assert.NotNull(report.AvgResponseDays);

        // زیردستان از چارت: کارمند ۲ دو زیردست دارد (۳ و ۴)
        Assert.True(report.HasSubordinates);
        Assert.Equal(new[] { 3, 4 }, report.Subordinates.Select(s => s.EmployeeId).OrderBy(x => x));
        var three = report.Subordinates.Single(s => s.EmployeeId == 3);
        Assert.Equal(1, three.OpenReferrals);
        Assert.Equal(1, three.OverdueReferrals);
        Assert.Equal(1, three.CompletedReferrals);
        Assert.Equal(2.0, three.AvgReferralDays);
        Assert.Equal(0, report.Subordinates.Single(s => s.EmployeeId == 4).OpenReferrals);

        // کارمند بدون زیردست جدول زیردستان ندارد
        var engineer = await new LetterReportService(ctx, Org(ctx)).GetAsync(U3, today.AddDays(-30), today, false);
        Assert.False(engineer.HasSubordinates);
        Assert.Empty(engineer.Subordinates);
    }

    [Fact]
    public async Task Report_SubordinateCountsExcludeConfidentialLettersTheViewerCannotSee()
    {
        var ctx = World(nameof(Report_SubordinateCountsExcludeConfidentialLettersTheViewerCannotSee));
        Sent(ctx, U1, new[] { 3 }, conf: "Internal");
        Sent(ctx, U1, new[] { 3 }, conf: "Restricted");
        var svc = new LetterReportService(ctx, Org(ctx));

        var plain = await svc.GetAsync(U2, DateTime.Today.AddDays(-5), DateTime.Today, canViewConfidential: false);
        Assert.Equal(1, plain.Subordinates.Single(s => s.EmployeeId == 3).Received);
        var full = await svc.GetAsync(U2, DateTime.Today.AddDays(-5), DateTime.Today, canViewConfidential: true);
        Assert.Equal(2, full.Subordinates.Single(s => s.EmployeeId == 3).Received);
    }

    [Fact]
    public async Task Report_UserWithoutEmployeeGetsEmptyReport_AndSwappedDatesAreNormalised()
    {
        var ctx = World(nameof(Report_UserWithoutEmployeeGetsEmptyReport_AndSwappedDatesAreNormalised));
        ctx.Users.Add(new PEMS.Persistence.Identity.ApplicationUser { Id = 300, UserName = "lonely", NormalizedUserName = "LONELY", IsActive = true, FullName = "Lonely" });
        await ctx.SaveChangesAsync();
        var r = await new LetterReportService(ctx, Org(ctx)).GetAsync(300, DateTime.Today, DateTime.Today.AddDays(-30), false);
        Assert.Equal(0, r.Received);
        Assert.False(r.HasSubordinates);
        Assert.True(r.FromDate <= r.ToDate);
        Assert.Equal(100, r.OnTimePercent);
    }
}
