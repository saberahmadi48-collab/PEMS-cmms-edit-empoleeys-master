using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;
using PEMS.Application.PartCatalogue;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Warehouse.Controllers;

namespace PEMS.Tests.PartCatalogue;

/// <summary>
/// تطبیق FREE FORMAT TEXT با Technical Class. بخش «مرجع واقعی» همه‌ی ۴۱۶۱ ردیف فایل docs/part-catalogue/TECHNICAL CLASSESS-2026-08-29.xlsx
/// را می‌خواند و ثابت می‌کند الگوریتم برای هیچ کلاسی کلاس «دیگری» را به‌صورت قطعی انتخاب نمی‌کند.
/// </summary>
public class TechnicalClassMatcherTests
{
    private static readonly TechnicalClassRef[] Small =
    {
        new(1, "IR25749", "ELBOW GENERIC"), new(2, "IR25898", "FLANGE,PIPE"), new(3, "IR100", "SWITCH"), new(4, "IR101", "SWITCH,LIMIT"),
        new(5, "IR200", "PIPE"), new(6, "IR201", "PIPE,BENT METALLIC"), new(7, "IR202", "PIPE,NON METALLIC"),
        new(8, "IR300", "DRUM"), new(9, "IR301", "DRUM"), new(10, "12345678", "BEARING,BALL"), new(11, "IR400", "PUMP,CENTRIFUGAL"), new(12, "IR500", "GRIP"),
        new(13, "IR600", "ELBOW,SPECIAL"), new(14, "IR601", "PUMP CENTRIFUGAL")
    };

    private static TechnicalClassMatcher M() => new(Small);

    [Fact]
    public void ExplicitIrCode_InCompanyFormTemplateFormat_ResolvesDeterministically()
    {
        var r = M().Resolve("ELBOW GENERIC(IR25749)");
        Assert.Equal(TechnicalClassResolutionStatus.Resolved, r.Status);
        Assert.Equal("Code", r.Reason);
        Assert.Equal(1, r.Resolved!.Id);
        Assert.Equal(1, M().Resolve("some text ir25749 more").Resolved!.Id);              // case-insensitive, anywhere
    }

    [Fact]
    public void NumericCode_IsHonoured_OnlyInsideParentheses()
    {
        Assert.Equal(10, M().Resolve("RADIAL (12345678)").Resolved!.Id);
        Assert.Equal(TechnicalClassResolutionStatus.None, M().Resolve("part no 12345678 qty 4").Status);   // plain number = could be a dimension
    }

    [Fact]
    public void ClassNameAtStart_ResolvesUnique_WithWordBoundary_AndLongestNameWins()
    {
        Assert.Equal(2, M().Resolve("FLANGE,PIPE, WN, 6 IN, 150#, A105").Resolved!.Id);
        Assert.Equal(2, M().Resolve("flange , pipe  WN 6in").Resolved!.Id);                // case / whitespace / comma spacing
        Assert.Equal(4, M().Resolve("SWITCH,LIMIT, 24V").Resolved!.Id);                    // longest of SWITCH / SWITCH,LIMIT
        Assert.Equal(3, M().Resolve("SWITCH, 24V DC").Resolved!.Id);
        Assert.Equal(TechnicalClassResolutionStatus.None, M().Resolve("GRIPPER ASSEMBLY").Status);       // GRIP is not a whole word of GRIPPER
    }

    [Fact]
    public void CommaAndSpaceSeparators_AreEquivalent_ButTheExactFormBreaksTies()
    {
        Assert.Equal(13, M().Resolve("ELBOW SPECIAL 90 DEG").Resolved!.Id);                // reference says "ELBOW,SPECIAL", user types a space
        Assert.Equal(1, M().Resolve("ELBOW GENERIC 90 DEG").Resolved!.Id);
        Assert.Equal(11, M().Resolve("PUMP,CENTRIFUGAL, 5 KW").Resolved!.Id);              // two classes differ only by separator => typed form decides
        Assert.Equal(14, M().Resolve("PUMP CENTRIFUGAL 5 KW").Resolved!.Id);
    }

    [Fact]
    public void PersianDigitsAndCommas_AreNormalised()
    {
        Assert.Equal(11, M().Resolve("PUMP،CENTRIFUGAL، ۵۰ کیلووات").Resolved!.Id);
        Assert.Equal("A,B 12", TechnicalClassMatcher.Normalize("a ، b  ۱۲"));
    }

    [Fact]
    public void DuplicateDescriptions_AreAmbiguous_NeverGuessed()
    {
        var r = M().Resolve("DRUM, 200 L");
        Assert.Equal(TechnicalClassResolutionStatus.Ambiguous, r.Status);
        Assert.Equal("NameAmbiguous", r.Reason);
        Assert.Equal(new[] { 8, 9 }, r.Candidates.Select(c => c.Id).OrderBy(x => x).ToArray());
        Assert.Null(r.Resolved);
    }

    [Fact]
    public void CodeThatContradictsTheNameAtTheStart_IsAmbiguous_NotAWinner()
    {
        var r = M().Resolve("FLANGE,PIPE (IR25749)");                                      // name says class 2, code says class 1
        Assert.Equal(TechnicalClassResolutionStatus.Ambiguous, r.Status);
        Assert.Equal("CodeNameConflict", r.Reason);
        Assert.Equal(new[] { 1, 2 }, r.Candidates.Select(c => c.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void TwoDifferentCodes_AreAmbiguous()
    {
        var r = M().Resolve("ELBOW GENERIC(IR25749) / FLANGE,PIPE (IR25898)");
        Assert.Equal(TechnicalClassResolutionStatus.Ambiguous, r.Status);
        Assert.Equal("CodeAmbiguous", r.Reason);
        Assert.Equal(2, r.Candidates.Count);
    }

    [Fact]
    public void PartialChildName_IsAmbiguous_NotSilentlyResolvedToTheParent()
    {
        var r = M().Resolve("PIPE,BENT, 6 IN");                                           // "PIPE,BENT METALLIC" is only half written
        Assert.Equal(TechnicalClassResolutionStatus.Ambiguous, r.Status);
        Assert.Equal("PartialChild", r.Reason);
        Assert.Contains(r.Candidates, c => c.Id == 5);
        Assert.Contains(r.Candidates, c => c.Id == 6);
        Assert.DoesNotContain(r.Candidates, c => c.Id == 7);
        Assert.Equal(5, M().Resolve("PIPE, 6 IN SCH40").Resolved!.Id);                    // unrelated continuation => the plain class
    }

    [Fact]
    public void KeywordsAnywhere_OnlySuggestCandidates_AndSingleWordNamesAreNeverSuggestedThisWay()
    {
        var r = M().Resolve("SPARE FOR CENTRIFUGAL PUMP");
        Assert.Equal(TechnicalClassResolutionStatus.Ambiguous, r.Status);
        Assert.Equal("Keywords", r.Reason);
        Assert.Null(r.Resolved);
        Assert.Equal(new[] { 11, 14 }, r.Candidates.Select(c => c.Id).OrderBy(x => x).ToArray());
        Assert.Equal(TechnicalClassResolutionStatus.None, M().Resolve("rubber GRIP for handle").Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    [InlineData("completely unrelated words")]
    public void EmptyOrUnmatched_ResolvesNothing(string? text)
    {
        var r = M().Resolve(text);
        Assert.Equal(TechnicalClassResolutionStatus.None, r.Status);
        Assert.Null(r.Resolved);
        Assert.Empty(r.Candidates);
    }

    [Fact]
    public void ChangingTheText_ChangesTheResult_NoStickyState()
    {
        var m = M();
        Assert.Equal(2, m.Resolve("FLANGE,PIPE 6 IN").Resolved!.Id);
        Assert.Equal(TechnicalClassResolutionStatus.None, m.Resolve("something else").Status);
        Assert.Equal(1, m.Resolve("ELBOW GENERIC").Resolved!.Id);
    }

    [Fact]
    public void PastedMarkup_IsOnlyDataNeverInterpreted()
    {
        var r = M().Resolve("<script>alert(1)</script>FLANGE,PIPE");
        Assert.NotEqual(TechnicalClassResolutionStatus.Resolved, r.Status);                // class name not at the start => never an automatic pick
        Assert.Null(r.Resolved);
        Assert.Equal(2, M().Resolve("FLANGE,PIPE <img src=x onerror=alert(1)>").Resolved!.Id);   // markup is plain data
    }

    // ------------------------------------------------------------------ real reference file (4161 classes)

    private static List<TechnicalClassRef> LoadReference()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        const string rel = "docs/part-catalogue/TECHNICAL CLASSESS-2026-08-29.xlsx";
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, rel))) dir = dir.Parent;
        Assert.NotNull(dir);
        using var fs = new FileStream(Path.Combine(dir!.FullName, rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(fs);
        var ws = wb.Worksheets.First();
        var list = new List<TechnicalClassRef>();
        var id = 1;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var code = row.Cell(1).GetString().Trim();
            var desc = row.Cell(2).GetString().Trim();
            if (code.Length > 0 && desc.Length > 0) list.Add(new TechnicalClassRef(id++, code, desc));
        }
        return list;
    }

    [Fact]
    public void RealReference_EveryClassCodeInTemplateFormat_ResolvesToItself()
    {
        var all = LoadReference();
        Assert.True(all.Count >= 4000, "reference has " + all.Count);
        var m = new TechnicalClassMatcher(all);
        foreach (var c in all)
        {
            var r = m.Resolve($"{c.Description}({c.Code})");
            Assert.True(r.Status == TechnicalClassResolutionStatus.Resolved && r.Resolved!.Id == c.Id, $"{c.Description}({c.Code}) => {r.Status}/{r.Reason}/{r.Resolved?.Code}");
        }
    }

    [Fact]
    public void RealReference_NameOnlyText_NeverAutoResolvesToADifferentClass()
    {
        var all = LoadReference();
        var m = new TechnicalClassMatcher(all);
        int resolved = 0, ambiguous = 0, none = 0;
        foreach (var c in all)
        {
            var r = m.Resolve($"{c.Description}, 6 IN, 150#, A105");
            switch (r.Status)
            {
                case TechnicalClassResolutionStatus.Resolved:
                    Assert.True(r.Resolved!.Id == c.Id, $"'{c.Description}' wrongly resolved to '{r.Resolved.Description}'");
                    resolved++; break;
                case TechnicalClassResolutionStatus.Ambiguous:
                    Assert.Contains(r.Candidates, x => x.Id == c.Id); ambiguous++; break;
                default:
                    Assert.Fail($"'{c.Description}' not matched at all"); none++; break;
            }
        }
        // evidence for the report: how often a name alone is decisive vs needs an explicit choice
        Console.WriteLine($"Technical Class reference: {all.Count} classes; name-only text => resolved {resolved}, ambiguous {ambiguous}, none {none}");
        Assert.True(resolved > all.Count * 0.9, $"only {resolved}/{all.Count} resolved");
    }

    [Fact]
    public void RealReference_SpaceSeparatedText_ResolvesToSelfOrASeparatorTwin_NeverToADifferentClass()
    {
        var all = LoadReference();
        var m = new TechnicalClassMatcher(all);
        static string Flat(string s) => System.Text.RegularExpressions.Regex.Replace(TechnicalClassMatcher.Normalize(s).Replace(',', ' '), @"\s+", " ");
        int resolved = 0, ambiguous = 0;
        foreach (var c in all)
        {
            var r = m.Resolve(Flat(c.Description) + " 6 IN 150# A105");
            if (r.Status == TechnicalClassResolutionStatus.Resolved)
            {
                Assert.True(Flat(r.Resolved!.Description) == Flat(c.Description), $"'{c.Description}' (space form) wrongly resolved to '{r.Resolved.Description}'");
                resolved++;
            }
            else if (r.Status == TechnicalClassResolutionStatus.Ambiguous) { Assert.Contains(r.Candidates, x => x.Id == c.Id); ambiguous++; }
            else Assert.Fail($"'{c.Description}' (space form) not matched at all");
        }
        Console.WriteLine($"Technical Class reference, space-separated text: resolved {resolved}, ambiguous {ambiguous}");
    }

    // ------------------------------------------------------------------ service + controller

    [Fact]
    public async Task Service_ResolvesAgainstActiveClassesOnly_AndReturnsLongDescription()
    {
        var db = PemsDbContextFactory.CreateInMemory("tc_resolve_" + Guid.NewGuid().ToString("N"));
        db.PartCatalogueTechnicalClasses.Add(new PartCatalogueTechnicalClass { Code = "IR1", Description = "ELBOW GENERIC", LongDescription = "Definition: a bend", IsActive = true });
        db.PartCatalogueTechnicalClasses.Add(new PartCatalogueTechnicalClass { Code = "IR2", Description = "OLD CLASS", IsActive = false });
        await db.SaveChangesAsync();
        var svc = new PartCatalogueLookupService(db);

        var ok = await svc.ResolveTechnicalClassAsync("ELBOW GENERIC, 90 DEG");
        Assert.Equal("resolved", ok.Status);
        Assert.Equal("IR1 - ELBOW GENERIC", ok.Resolved!.Text);
        Assert.Equal("Definition: a bend", ok.Resolved.Extra);

        Assert.Equal("none", (await svc.ResolveTechnicalClassAsync("OLD CLASS, X (IR2)")).Status);   // inactive class is never offered
        Assert.Equal("none", (await svc.ResolveTechnicalClassAsync("   ")).Status);
    }

    private sealed class Actions : IRoleModuleActionService
    {
        private readonly bool _allow;
        public Actions(bool allow) => _allow = allow;
        public Task<bool> HasActionAsync(IEnumerable<string> userRoles, string module, string action) => Task.FromResult(_allow && module == "PartCatalogue");
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ResolveEndpoint_RequiresPartCatalogueAction(bool allowed, bool expectForbid)
    {
        var db = PemsDbContextFactory.CreateInMemory("tc_ctl_" + Guid.NewGuid().ToString("N"));
        var operations = new PEMS.Web.Services.PartCatalogueOperations(new PartCatalogueLookupService(db), null!, null!, new Actions(allowed), null!, db);
        if (expectForbid) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.ResolveTechnicalClassAsync(new List<string> { "R" }, "FLANGE,PIPE"));
        else Assert.NotNull(await operations.ResolveTechnicalClassAsync(new List<string> { "R" }, "FLANGE,PIPE"));
    }
}
