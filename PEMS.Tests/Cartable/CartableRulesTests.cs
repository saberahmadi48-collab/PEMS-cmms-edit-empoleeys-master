using PEMS.Application.Common;
using PEMS.Application.DTOs;

namespace PEMS.Tests.Cartable;

/// <summary>قواعد خالص کارتابل: پوشه‌ها، درخت برچسب، تفویض، مسیریابی خودکار، تنظیمات و فیلتر تاریخچه‌ی گردش.</summary>
public class CartableRulesTests
{
    // ------------------------------------------------------------------ folders
    [Theory]
    [InlineData("INBOX", "inbox")]
    [InlineData(" trash ", "trash")]
    [InlineData("all", "all")]
    [InlineData("nonsense", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Folders_NormalizeAcceptsOnlyKnownKeys(string? input, string? expected) => Assert.Equal(expected, LetterFolders.Normalize(input));

    // ------------------------------------------------------------------ tag tree
    private static Dictionary<int, TagTreeRules.Node> Tree(params (int Id, int? Parent, int? Owner)[] rows)
        => rows.ToDictionary(r => r.Id, r => new TagTreeRules.Node(r.Id, r.Parent, r.Owner, true));

    [Fact]
    public void TagTree_RootIsAlwaysAllowed() => Assert.Null(TagTreeRules.ValidateParent(null, null, null, Tree()));

    [Fact]
    public void TagTree_RejectsSelfParentAndCycle()
    {
        var t = Tree((1, null, null), (2, 1, null), (3, 2, null));
        Assert.Equal(TagTreeRules.ErrSelfParent, TagTreeRules.ValidateParent(2, 2, null, t));
        Assert.Equal(TagTreeRules.ErrCycle, TagTreeRules.ValidateParent(1, 3, null, t));   // 3 زیرمجموعه‌ی 1 است
        Assert.Null(TagTreeRules.ValidateParent(3, 1, null, t));
    }

    [Fact]
    public void TagTree_EnforcesMaxDepthIncludingMovedSubtree()
    {
        var t = Tree((1, null, null), (2, 1, null), (3, 2, null), (4, 3, null), (10, null, null), (11, 10, null));
        Assert.Equal(TagTreeRules.ErrTooDeep, TagTreeRules.ValidateParent(null, 4, null, t));   // عمق ۵
        // انتقال شاخه‌ی ۱۰←۱۱ (ارتفاع ۲) زیر ۳ (عمق ۳) = عمق ۵ مجاز نیست
        Assert.Equal(TagTreeRules.ErrTooDeep, TagTreeRules.ValidateParent(10, 3, null, t));
        Assert.Null(TagTreeRules.ValidateParent(10, 2, null, t));   // ۲+۲ = ۴
    }

    [Fact]
    public void TagTree_RespectsOrganizationalAndPersonalScope()
    {
        var t = Tree((1, null, null), (2, null, 7), (3, null, 8));
        Assert.Null(TagTreeRules.ValidateParent(null, 1, 7, t));                                        // شخصی زیر سازمانی
        Assert.Null(TagTreeRules.ValidateParent(null, 2, 7, t));                                        // شخصی زیر شخصیِ خودش
        Assert.Equal(TagTreeRules.ErrScopeMismatch, TagTreeRules.ValidateParent(null, 3, 7, t));        // زیر شخصیِ دیگری
        Assert.Equal(TagTreeRules.ErrScopeMismatch, TagTreeRules.ValidateParent(null, 2, null, t));     // سازمانی زیر شخصی
    }

    [Fact]
    public void TagTree_MissingOrInactiveParentIsRejected()
    {
        var t = new Dictionary<int, TagTreeRules.Node> { [1] = new(1, null, null, false) };
        Assert.Equal(TagTreeRules.ErrParentMissing, TagTreeRules.ValidateParent(null, 1, null, t));
        Assert.Equal(TagTreeRules.ErrParentMissing, TagTreeRules.ValidateParent(null, 99, null, t));
    }

    [Fact]
    public void TagTree_DescendantsAndVisibility()
    {
        var nodes = new[] { new TagTreeRules.Node(1, null, null, true), new TagTreeRules.Node(2, 1, null, true), new TagTreeRules.Node(3, 2, null, true), new TagTreeRules.Node(4, null, null, true) };
        Assert.Equal(new[] { 1, 2, 3 }, TagTreeRules.DescendantsAndSelf(1, nodes).OrderBy(x => x));
        Assert.True(TagTreeRules.IsVisibleTo(null, 5));
        Assert.True(TagTreeRules.IsVisibleTo(5, 5));
        Assert.False(TagTreeRules.IsVisibleTo(5, 6));
        Assert.False(TagTreeRules.IsVisibleTo(5, null));
    }

    // ------------------------------------------------------------------ delegation
    private static readonly DateTime Now = new(2026, 10, 24);

    [Fact]
    public void Delegation_ValidatesSelfDatesPastLengthAndOverlap()
    {
        var none = Array.Empty<LetterDelegationRules.Span>();
        Assert.Equal(LetterDelegationRules.ErrSelf, LetterDelegationRules.Validate(1, 1, Now, Now.AddDays(2), Now, none));
        Assert.Equal(LetterDelegationRules.ErrDates, LetterDelegationRules.Validate(1, 2, Now.AddDays(3), Now, Now, none));
        Assert.Equal(LetterDelegationRules.ErrPast, LetterDelegationRules.Validate(1, 2, Now.AddDays(-5), Now.AddDays(-1), Now, none));
        Assert.Equal(LetterDelegationRules.ErrTooLong, LetterDelegationRules.Validate(1, 2, Now, Now.AddDays(400), Now, none));
        Assert.Null(LetterDelegationRules.Validate(1, 2, Now, Now.AddDays(5), Now, none));

        var existing = new[] { new LetterDelegationRules.Span(1, 2, Now, Now.AddDays(10), true) };
        Assert.Equal(LetterDelegationRules.ErrOverlap, LetterDelegationRules.Validate(1, 2, Now.AddDays(5), Now.AddDays(12), Now, existing));
        Assert.Null(LetterDelegationRules.Validate(1, 2, Now.AddDays(11), Now.AddDays(12), Now, existing));
        Assert.Equal(LetterDelegationRules.ErrCircular, LetterDelegationRules.Validate(2, 1, Now.AddDays(1), Now.AddDays(2), Now, existing));
        // لغوشده‌ها نادیده گرفته می‌شوند
        Assert.Null(LetterDelegationRules.Validate(1, 2, Now.AddDays(5), Now.AddDays(12), Now, new[] { existing[0] with { IsActive = false } }));
    }

    [Fact]
    public void Delegation_IsEffectiveOnlyInsideTheWindowWhileActive()
    {
        var span = new LetterDelegationRules.Span(1, 2, Now, Now.AddDays(3), true);
        Assert.True(LetterDelegationRules.IsEffective(span, Now));
        Assert.True(LetterDelegationRules.IsEffective(span, Now.AddDays(3).AddHours(20)));
        Assert.False(LetterDelegationRules.IsEffective(span, Now.AddDays(-1)));
        Assert.False(LetterDelegationRules.IsEffective(span, Now.AddDays(4)));
        Assert.False(LetterDelegationRules.IsEffective(span with { IsActive = false }, Now));
    }

    // ------------------------------------------------------------------ routing
    private static readonly LetterRoutingFacts Facts = new("Incoming", "Urgent", "Request", 5, 2, "ACME Petrochemical Co.", "Pump P-101 datasheet revision");

    [Fact]
    public void Routing_RuleWithoutConditionsNeverMatches()
        => Assert.False(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition(null, null, null, null, null, null, null), Facts));

    [Fact]
    public void Routing_AllFilledConditionsMustHold_CaseInsensitive()
    {
        Assert.True(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition("incoming", "URGENT", null, 5, null, "acme", "datasheet"), Facts));
        Assert.False(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition("Incoming", "Low", null, null, null, null, null), Facts));
        Assert.False(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition(null, null, null, 6, null, null, null), Facts));
        Assert.False(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition(null, null, null, null, null, "other co", null), Facts));
        Assert.True(LetterRoutingRuleEvaluator.Matches(new LetterRoutingCondition(null, null, null, null, 2, null, null), Facts));
    }

    // ------------------------------------------------------------------ settings
    [Fact]
    public void Settings_SanitizeReplacesInvalidValues()
    {
        var s = LetterUserSettingsRules.Sanitize(new LetterUserSettingsRules.Settings
        {
            View = "weird", Density = "huge", HiddenColumns = new() { "priority", "subject", "priority", "evil" }, FrozenColumns = 99, PageSize = 7
        });
        Assert.Equal("flat", s.View);
        Assert.Equal("normal", s.Density);
        Assert.Equal(new[] { "priority" }, s.HiddenColumns);   // subject قابل پنهان‌شدن نیست
        Assert.Equal(3, s.FrozenColumns);
        Assert.Equal(50, s.PageSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[]")]
    public void Settings_CorruptJsonFallsBackToDefaults(string? json)
    {
        var s = LetterUserSettingsRules.Parse(json);
        Assert.Equal("flat", s.View);
        Assert.Equal(50, s.PageSize);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var json = LetterUserSettingsRules.Serialize(new LetterUserSettingsRules.Settings { View = "thread", Density = "dense", FrozenColumns = 2, PageSize = 100 });
        var s = LetterUserSettingsRules.Parse(json);
        Assert.Equal("thread", s.View);
        Assert.Equal("dense", s.Density);
        Assert.Equal(2, s.FrozenColumns);
        Assert.Equal(100, s.PageSize);
    }

    // ------------------------------------------------------------------ workflow history
    private static List<LetterTimelineEventDto> Events() => new()
    {
        new() { EventDate = new DateTime(2026, 1, 1), EventType = "Created", ActorEmployeeId = 1 },
        new() { EventDate = new DateTime(2026, 1, 2), EventType = "Referred" },               // اقدام‌کننده نامعلوم
        new() { EventDate = new DateTime(2026, 1, 3), EventType = "Read", ActorEmployeeId = 2 },
        new() { EventDate = new DateTime(2026, 1, 4), EventType = "Completed", ActorEmployeeId = 3 }
    };

    [Fact]
    public void History_AllKeepsEverythingAndSortsBothWays()
    {
        var none = new HashSet<int>();
        Assert.Equal(new[] { "Created", "Referred", "Read", "Completed" }, LetterHistoryFilter.Apply(Events(), "all", false, 1, none, none).Select(e => e.EventType));
        Assert.Equal(new[] { "Completed", "Read", "Referred", "Created" }, LetterHistoryFilter.Apply(Events(), "all", true, 1, none, none).Select(e => e.EventType));
    }

    [Fact]
    public void History_RoleFiltersUseOrgChartSetsAndDropUnknownActors()
    {
        var managers = new HashSet<int> { 2 }; var subs = new HashSet<int> { 3 };
        Assert.Equal(new[] { "Created" }, LetterHistoryFilter.Apply(Events(), "me", false, 1, managers, subs).Select(e => e.EventType));
        Assert.Equal(new[] { "Read" }, LetterHistoryFilter.Apply(Events(), "manager", false, 1, managers, subs).Select(e => e.EventType));
        Assert.Equal(new[] { "Completed" }, LetterHistoryFilter.Apply(Events(), "subordinates", false, 1, managers, subs).Select(e => e.EventType));
        Assert.Empty(LetterHistoryFilter.Apply(Events(), "me", false, null, managers, subs));
        Assert.Equal("all", LetterHistoryFilter.NormalizeRole("bogus"));
    }
}
