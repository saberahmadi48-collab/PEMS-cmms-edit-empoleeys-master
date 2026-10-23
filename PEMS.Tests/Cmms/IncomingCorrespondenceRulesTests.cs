using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>قواعد خالص ثبت مکاتبه‌ی وارده (DCC)، تاریخ جلالی و شماره‌گذاری «پروژه-IN-سال شمسی-ترتیب».</summary>
public class IncomingCorrespondenceRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 22);

    private static IncomingFileInput Pdf(string name = "scan.pdf", string role = "Original", long length = 2048)
        => new() { FileName = name, Length = length, Role = role };

    private static IncomingRegistrationInput Valid() => new()
    {
        ProjectId = 1,
        CorrespondentPartyId = 5,
        SenderReference = "ACME-0452",
        DocumentDate = Today.AddDays(-3),
        ReceivedDate = Today,
        Subject = "Request for update",
        Files = { Pdf() }
    };

    private static List<string> Codes(IncomingRegistrationInput input) => IncomingCorrespondenceRules.ValidateStructure(input, Today).Select(i => i.Code).ToList();

    [Fact]
    public void AValidInput_HasNoIssues() => Assert.Empty(Codes(Valid()));

    [Fact]
    public void RequiredFields_AreReportedWithStableCodes_AndFieldNames()
    {
        var input = new IncomingRegistrationInput();
        var issues = IncomingCorrespondenceRules.ValidateStructure(input, Today);

        var byCode = issues.ToDictionary(i => i.Code, i => i.Field);
        Assert.Equal("projectId", byCode["ProjectRequired"]);
        Assert.Equal("party", byCode["PartyRequired"]);
        Assert.Equal("subject", byCode["SubjectRequired"]);
        Assert.Equal("senderReference", byCode["SenderReferenceRequired"]);
        Assert.Equal("documentDate", byCode["DocumentDateRequired"]);
        Assert.Equal("files", byCode["OriginalRequired"]);
    }

    [Theory]
    [InlineData("Email", false)]
    [InlineData("Portal", false)]
    [InlineData("Hand", false)]
    [InlineData("OfficialLetter", true)]
    public void SenderReference_IsRequiredOnlyForOfficialLetters(string channel, bool required)
    {
        var input = Valid();
        input.SenderReference = null;
        input.ReceiptChannel = channel;
        Assert.Equal(required, Codes(input).Contains("SenderReferenceRequired"));
    }

    [Fact]
    public void Dates_DocumentInTheFuture_ReceivedBeforeDocument_AndReceivedInTheFuture_AreRejected()
    {
        var future = Valid(); future.DocumentDate = Today.AddDays(1);
        Assert.Contains("DocumentDateFuture", Codes(future));

        var before = Valid(); before.DocumentDate = Today.AddDays(-2); before.ReceivedDate = Today.AddDays(-5);
        Assert.Contains("ReceivedBeforeDocument", Codes(before));

        var received = Valid(); received.ReceivedDate = Today.AddDays(2);
        Assert.Contains("ReceivedDateFuture", Codes(received));
    }

    [Fact]
    public void ReceivedDate_DefaultsToToday_WhenOmitted()
    {
        var input = Valid(); input.ReceivedDate = null; input.DocumentDate = Today;
        Assert.Empty(Codes(input));
    }

    [Fact]
    public void ResponseRequired_NeedsADueDateAfterReceipt_AndAnOwner()
    {
        var input = Valid(); input.ResponseRequired = true;
        var codes = Codes(input);
        Assert.Contains("ResponseDueRequired", codes);
        Assert.Contains("OwnerRequired", codes);

        input.ResponseDueDate = Today.AddDays(-1); input.OwnerEmployeeId = 2;
        Assert.Contains("ResponseDueBeforeReceived", Codes(input));

        input.ResponseDueDate = Today.AddDays(7);
        Assert.Empty(Codes(input));
    }

    [Fact]
    public void Whitelists_RejectUnknownPriorityCategoryConfidentialityChannelAndPartyType()
    {
        var input = Valid();
        input.Priority = "Whenever"; input.Category = "Gossip"; input.ConfidentialityLevel = "Secret"; input.ReceiptChannel = "Pigeon";
        input.CorrespondentPartyId = null; input.NewPartyName = "New Co"; input.NewPartyType = "Alien";

        var codes = Codes(input);
        Assert.Contains("PriorityInvalid", codes);
        Assert.Contains("CategoryInvalid", codes);
        Assert.Contains("ConfidentialityInvalid", codes);
        Assert.Contains("ChannelInvalid", codes);
        Assert.Contains("PartyTypeInvalid", codes);
    }

    [Fact]
    public void Files_ExactlyOneOriginal_RolesChecked_CountAndSizeLimited()
    {
        var none = Valid(); none.Files = new() { Pdf(role: "Attachment") };
        Assert.Contains("OriginalRequired", Codes(none));

        var two = Valid(); two.Files = new() { Pdf(), Pdf("b.pdf") };
        Assert.Contains("OneOriginalOnly", Codes(two));

        var role = Valid(); role.Files = new() { Pdf(), Pdf("b.pdf", "Mystery") };
        Assert.Contains("FileRoleInvalid", Codes(role));

        var many = Valid(); many.Files = Enumerable.Range(0, IncomingCorrespondenceRules.MaxFiles + 1)
            .Select(i => Pdf($"f{i}.pdf", i == 0 ? "Original" : "Attachment")).ToList();
        Assert.Contains("TooManyFiles", Codes(many));

        var big = Valid(); big.Files = new() { Pdf(length: LetterComposeRules.MaxAttachmentBytes + 1) };
        Assert.Contains("FileTooLarge", Codes(big));

        var exe = Valid(); exe.Files = new() { Pdf("run.exe") };
        Assert.Contains("FileTypeNotAllowed", Codes(exe));

        var empty = Valid(); empty.Files = new() { Pdf(length: 0) };
        Assert.Contains("FileEmpty", Codes(empty));
    }

    [Theory]
    [InlineData("a.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, true)]
    [InlineData("a.pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]           // an .exe renamed to .pdf
    [InlineData("a.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, true)]
    [InlineData("a.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("a.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true)]
    [InlineData("a.doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, true)]
    [InlineData("a.msg", new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, true)]
    [InlineData("a.eml", new byte[] { 0x46, 0x72, 0x6F, 0x6D }, true)]            // text formats are not signature-checked
    [InlineData("a.pdf", new byte[] { }, false)]
    public void SignatureMatches_ComparesTheFirstBytesWithTheExtension(string name, byte[] head, bool expected)
        => Assert.Equal(expected, IncomingCorrespondenceRules.SignatureMatches(name, head));

    [Fact]
    public void LinkTypes_AreValidated_AndCapped()
    {
        var bad = Valid(); bad.Links = new() { new IncomingLinkInput("Planet", 1) };
        Assert.Contains("LinkInvalid", Codes(bad));

        var many = Valid(); many.Links = Enumerable.Range(1, IncomingCorrespondenceRules.MaxLinks + 1).Select(i => new IncomingLinkInput("Transmittal", i)).ToList();
        Assert.Contains("TooManyLinks", Codes(many));
    }

    [Fact]
    public void ExternalSubjects_MayBeLongerThanInternalOnes()
    {
        var input = Valid(); input.Subject = new string('x', 200);
        Assert.Empty(Codes(input));
        input.Subject = new string('x', IncomingCorrespondenceRules.MaxSubjectLength + 1);
        Assert.Contains("SubjectTooLong", Codes(input));
    }

    // ------------------------------------------------------------------ Jalali dates

    [Theory]
    [InlineData("2026-03-21", 1405, 1, 1)]     // Nowruz 1405
    [InlineData("2026-03-20", 1404, 12, 29)]   // last day of 1404 (not a leap year)
    [InlineData("2025-03-20", 1403, 12, 30)]   // 1403 is a leap year
    [InlineData("2026-10-22", 1405, 7, 30)]
    public void JalaliDate_ConvertsLikeThePersianCalendar(string iso, int y, int m, int d)
    {
        var date = DateTime.ParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal((y, m, d), JalaliDate.Parts(date));
        Assert.Equal($"{y:D4}/{m:D2}/{d:D2}", JalaliDate.Format(date));
        Assert.True(JalaliDate.TryToGregorian(y, m, d, out var back));
        Assert.Equal(date, back);
    }

    [Theory]
    [InlineData("1405/02/05", true)]
    [InlineData("۱۴۰۵/۰۲/۰۵", true)]
    [InlineData("1405-02-05", true)]
    [InlineData("1404/12/30", false)]    // Esfand 1404 has 29 days
    [InlineData("1405/13/01", false)]
    [InlineData("1405/00/10", false)]
    [InlineData("1405/02", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void JalaliDate_TryParse_AcceptsRealDatesOnly(string text, bool ok) => Assert.Equal(ok, JalaliDate.TryParse(text, out _));

    // ------------------------------------------------------------------ numbering

    private static (LetterNumberingService Numbering, PEMS.Persistence.Context.PEMSDbContext Ctx) World(string name)
    {
        var ctx = LetterTestWorld.NewContext(name);
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "jpc", ProjectName = "JPC Plant" });
        ctx.Projects.Add(new Project { ProjectId = 2, ProjectCode = "AB-7", ProjectName = "Second" });
        ctx.SaveChanges();
        return (new LetterNumberingService(ctx), ctx);
    }

    [Fact]
    public async Task IncomingNumber_UsesProjectCode_IN_JalaliYear_AndAFourDigitSequence()
    {
        var (numbering, _) = World("num1");
        var received = new DateTime(2026, 10, 22);   // 1405

        Assert.Equal("JPC-IN-1405-0001", await numbering.GenerateNextIncomingNumberAsync(1, received));
        Assert.Equal("JPC-IN-1405-0002", await numbering.GenerateNextIncomingNumberAsync(1, received));
    }

    [Fact]
    public async Task IncomingNumber_CountersAreIndependentPerProject_AndRestartOnFarvardin()
    {
        var (numbering, _) = World("num2");

        Assert.Equal("JPC-IN-1404-0001", await numbering.GenerateNextIncomingNumberAsync(1, new DateTime(2026, 3, 20)));   // 29 Esfand 1404
        Assert.Equal("JPC-IN-1405-0001", await numbering.GenerateNextIncomingNumberAsync(1, new DateTime(2026, 3, 21)));   // 1 Farvardin 1405: restarts
        Assert.Equal("AB7-IN-1405-0001", await numbering.GenerateNextIncomingNumberAsync(2, new DateTime(2026, 3, 21)));   // other project, code sanitised
        Assert.Equal("JPC-IN-1405-0002", await numbering.GenerateNextIncomingNumberAsync(1, new DateTime(2026, 4, 1)));
    }

    [Fact]
    public async Task IncomingNumber_WithoutAProject_FallsBackToGEN()
    {
        var (numbering, _) = World("num3");
        Assert.Equal("GEN-IN-1405-0001", await numbering.GenerateNextIncomingNumberAsync(null, new DateTime(2026, 10, 22)));
    }

    [Fact]
    public async Task IncomingNumber_ContinuesAfterTheHighestExistingNumber_AndSkipsTakenOnes()
    {
        var (numbering, ctx) = World("num4");
        ctx.Letters.Add(new Letter { LetterType = "Incoming", LetterNumber = "JPC-IN-1405-0007", Subject = "old", Status = "Registered" });
        await ctx.SaveChangesAsync();

        Assert.Equal("JPC-IN-1405-0008", await numbering.GenerateNextIncomingNumberAsync(1, new DateTime(2026, 10, 22)));
    }

    [Fact]
    public void Format_KeepsFiveDigitsForDepartmentNumbers_AndFourForIncoming()
    {
        Assert.Equal("ENG-2026-00123", LetterNumberingService.Format("ENG", 2026, 123));
        Assert.Equal("JPC-IN-1405-0187", LetterNumberingService.Format("JPC-IN", 1405, 187, 4));
    }

    [Fact]
    public async Task DepartmentNumbering_IsUnchanged()
    {
        var ctx = LetterTestWorld.NewContext("num5");
        var n = await new LetterNumberingService(ctx).GenerateNextNumberAsync("Internal", null, 1);
        Assert.Matches($@"^ENG01-{DateTime.Now.Year}-00001$", n);
    }
}
