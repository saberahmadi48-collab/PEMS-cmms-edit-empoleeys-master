using System.Text;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace PEMS.Tests.Cmms;

/// <summary>ثبت مکاتبه‌ی وارده در DCC: ذخیره‌ی اتمیک، شماره‌ی شمسی، طرف مکاتبه، ارجاع/رونوشت، «در پاسخ به»، ثبت تکراری، پیوندها و رشته‌ی مکاتبه.</summary>
public class IncomingCorrespondenceServiceTests
{
    private static readonly DateTime Today = DateTime.Today;
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 test scan");

    private sealed class FakeFiles : IFileStorageService
    {
        public List<string> Saved { get; } = new();
        public List<string> Deleted { get; } = new();
        public int? FailOnCall { get; set; }
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content)
        {
            if (FailOnCall == Saved.Count + 1) throw new IOException("disk full");
            Saved.Add("key-" + (Saved.Count + 1));
            return Task.FromResult(Saved[^1]);
        }
        public Task<Stream> OpenReadAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) { Deleted.Add(key); return Task.CompletedTask; }
    }

    private sealed record Rig(IncomingCorrespondenceService Service, PEMSDbContext Ctx, FakeFiles Files, LetterTestNotifications Notes);

    private static Rig NewRig(string name)
    {
        var ctx = LetterTestWorld.NewContext(name);
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "JPC", ProjectName = "JPC Plant" });
        ctx.Projects.Add(new Project { ProjectId = 2, ProjectCode = "XYZ", ProjectName = "Other" });
        ctx.Disciplines.Add(new Discipline { DisciplineId = 1, Code = "ME", Name = "Mechanical", IsActive = true });
        ctx.SaveChanges();

        var files = new FakeFiles();
        var notes = new LetterTestNotifications();
        return new Rig(new IncomingCorrespondenceService(ctx, new LetterNumberingService(ctx), files, new FakeKnowledgeBaseService(), notes), ctx, files, notes);
    }

    private static IncomingFileInput Pdf(string name = "scan.pdf", string role = "Original", byte[]? bytes = null)
    {
        var data = bytes ?? PdfBytes;
        return new IncomingFileInput { FileName = name, ContentType = "application/pdf", Length = data.Length, Role = role, Content = new MemoryStream(data) };
    }

    private static IncomingRegistrationInput Valid(int? partyId = null, string partyName = "Acme Co") => new()
    {
        ProjectId = 1,
        CorrespondentPartyId = partyId,
        NewPartyName = partyId == null ? partyName : null,
        NewPartyType = partyId == null ? "Client" : null,
        SenderReference = "ACME-0452",
        DocumentDate = Today.AddDays(-3),
        ReceivedDate = Today,
        Subject = "Request for update",
        Summary = "Please <b>confirm</b> the schedule.\nSecond line.",
        Files = { Pdf() }
    };

    private static async Task<int> AddPartyAsync(PEMSDbContext ctx, string name = "Acme Co", int? vendorId = null)
    {
        var party = new CorrespondentParty { Name = name, PartyType = "Client", VendorId = vendorId };
        ctx.CorrespondentParties.Add(party);
        await ctx.SaveChangesAsync();
        return party.CorrespondentPartyId;
    }

    // ------------------------------------------------------------------ registration

    [Fact]
    public async Task Register_StoresEveryFieldOnTheLetter_WithAJalaliProjectNumber_AndAProvisionalParty()
    {
        var rig = NewRig("reg1");
        var input = Valid();
        input.Priority = "High"; input.Category = "Request"; input.ConfidentialityLevel = "Confidential"; input.DisciplineId = 1;
        input.ReceiptChannel = "Email";

        var result = await rig.Service.RegisterAsync(input, LetterTestWorld.Author, 1);

        Assert.True(result.Success, string.Join(",", result.Issues.Select(i => i.Code)));
        Assert.Equal($"JPC-IN-{JalaliDate.Year(Today)}-0001", result.LetterNumber);

        var letter = await rig.Ctx.Letters.Include(l => l.Attachments).Include(l => l.CorrespondentParty).SingleAsync();
        Assert.Equal("Incoming", letter.LetterType);
        Assert.Equal("Registered", letter.Status);
        Assert.Equal("High", letter.Priority);                       // the code, not localized text
        Assert.Equal("Confidential", letter.ConfidentialityLevel);   // not fixed to Internal any more
        Assert.Equal("Request", letter.Category);
        Assert.Equal("Email", letter.ReceiptChannel);                // a column, not a "[منبع:…]" prefix in the body
        Assert.Equal("ACME-0452", letter.SenderReference);
        Assert.Equal(Today.AddDays(-3), letter.DocumentDate);
        Assert.Equal(Today, letter.ReceivedDate);
        Assert.Equal(Today, letter.LetterDate.Date);
        Assert.Equal("Acme Co", letter.Sender);
        Assert.Equal(1, letter.ProjectId);
        Assert.Equal(1, letter.DisciplineId);
        Assert.Equal(LetterTestWorld.Author, letter.RegisteredById);
        Assert.DoesNotContain("[منبع", letter.Body);
        Assert.Contains("&lt;b&gt;confirm&lt;/b&gt;", letter.Body);   // the summary is plain text: markup is encoded, never executed
        Assert.Contains("<p>Second line.</p>", letter.Body);

        Assert.True(letter.CorrespondentParty!.IsProvisional);
        Assert.Equal("Client", letter.CorrespondentParty.PartyType);

        var file = Assert.Single(letter.Attachments);
        Assert.Equal("Original", file.FileRole);
        Assert.True(file.IsOfficial);
        Assert.Equal(1, file.UploadedById);
        Assert.Equal(64, file.FileHash!.Length);
        Assert.Equal("key-1", file.FilePath);
    }

    [Fact]
    public async Task Register_NumbersCountPerProject_AndRestartInANewJalaliYear()
    {
        var rig = NewRig("reg2");
        var partyId = await AddPartyAsync(rig.Ctx);

        IncomingRegistrationInput At(int project, DateTime received) { var i = Valid(partyId); i.ProjectId = project; i.ReceivedDate = received; i.DocumentDate = received.AddDays(-1); i.SenderReference = Guid.NewGuid().ToString("N")[..8]; return i; }

        var a = await rig.Service.RegisterAsync(At(1, new DateTime(2026, 3, 20)), 101, 1);   // 29 Esfand 1404
        var b = await rig.Service.RegisterAsync(At(1, new DateTime(2026, 3, 21)), 101, 1);   // 1 Farvardin 1405
        var c = await rig.Service.RegisterAsync(At(2, new DateTime(2026, 3, 21)), 101, 1);
        var d = await rig.Service.RegisterAsync(At(1, new DateTime(2026, 4, 1)), 101, 1);

        Assert.Equal(new[] { "JPC-IN-1404-0001", "JPC-IN-1405-0001", "XYZ-IN-1405-0001", "JPC-IN-1405-0002" },
            new[] { a.LetterNumber, b.LetterNumber, c.LetterNumber, d.LetterNumber });
    }

    [Fact]
    public async Task Register_PeekedNumber_IsTheOneThatIsIssued()
    {
        var rig = NewRig("reg3");
        var peek = await rig.Service.PeekNextNumberAsync(1, Today);
        var result = await rig.Service.RegisterAsync(Valid(), 101, 1);
        Assert.Equal(peek, result.LetterNumber);
        Assert.Equal(await rig.Service.PeekNextNumberAsync(1, Today), $"JPC-IN-{JalaliDate.Year(Today)}-0002");
    }

    [Fact]
    public async Task Register_AnExistingPartyName_IsReused_NotDuplicated()
    {
        var rig = NewRig("reg4");
        var first = Valid(partyName: "  Acme Co ");
        var second = Valid(partyName: "ACME CO"); second.SenderReference = "ACME-9999";

        await rig.Service.RegisterAsync(first, 101, 1);
        var result = await rig.Service.RegisterAsync(second, 101, 1);

        Assert.True(result.Success);
        Assert.Single(await rig.Ctx.CorrespondentParties.ToListAsync());
        Assert.Equal(2, await rig.Ctx.Letters.CountAsync(l => l.CorrespondentPartyId != null));
    }

    [Fact]
    public async Task Register_WithAnOwner_CreatesTheReferral_SetsReferred_AndNotifiesOwnerAndCc_ButNotTwice()
    {
        var rig = NewRig("reg5");
        var input = Valid();
        input.ResponseRequired = true; input.ResponseDueDate = Today.AddDays(7); input.OwnerEmployeeId = 2;
        input.CcEmployeeIds = new() { 3, 2, 3 };   // duplicates and the owner are collapsed
        input.Instruction = "  Reply with the revised schedule  ";

        var result = await rig.Service.RegisterAsync(input, LetterTestWorld.Author, 1);

        Assert.True(result.Success, string.Join(",", result.Issues.Select(i => i.Code)));
        var letter = await rig.Ctx.Letters.Include(l => l.Referrals).Include(l => l.Recipients).SingleAsync();
        Assert.Equal("Referred", letter.Status);
        Assert.True(letter.ResponseRequired);
        Assert.Equal(Today.AddDays(7), letter.ResponseDueDate);

        var referral = Assert.Single(letter.Referrals);
        Assert.Equal(2, referral.ReferredToEmployeeId);
        Assert.Equal("Reply with the revised schedule", referral.Instructions);
        Assert.Equal(Today.AddDays(7), referral.DueDate);
        Assert.Equal("E1 L1", referral.ReferredByName);

        var cc = Assert.Single(letter.Recipients);
        Assert.Equal((3, "Cc"), (cc.EmployeeId, cc.RecipientType));

        Assert.Equal(new[] { 102, 103 }, rig.Notes.Sent.Select(n => n.UserId).OrderBy(x => x));
    }

    [Fact]
    public async Task Register_WithoutAnOwner_StaysRegistered_AndFailingNotificationsDoNotBreakTheRegistration()
    {
        var rig = NewRig("reg6");
        rig.Notes.Throw = true;
        var input = Valid(); input.CcEmployeeIds = new() { 3 };

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.True(result.Success);
        Assert.Equal("Registered", (await rig.Ctx.Letters.SingleAsync()).Status);
    }

    [Fact]
    public async Task Register_Validation_ReturnsFieldErrors_AndPersistsNothing()
    {
        var rig = NewRig("reg7");
        var input = new IncomingRegistrationInput { Subject = " " };

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Code == "SubjectRequired" && i.Field == "subject");
        Assert.Empty(rig.Ctx.Letters);
        Assert.Empty(rig.Ctx.LetterNumberSequences);
        Assert.Empty(rig.Files.Saved);
    }

    [Fact]
    public async Task Register_UnknownProjectContractDisciplineOwnerCcParty_AreAllReported()
    {
        var rig = NewRig("reg8");
        var input = Valid(partyId: 999);
        input.ProjectId = 77; input.DisciplineId = 55; input.OwnerEmployeeId = 999; input.CcEmployeeIds = new() { 998 };

        var codes = (await rig.Service.RegisterAsync(input, 101, 1)).Issues.Select(i => i.Code).ToList();

        Assert.Contains("ProjectNotFound", codes);
        Assert.Contains("DisciplineNotFound", codes);
        Assert.Contains("PartyNotFound", codes);
        Assert.Contains("OwnerNotFound", codes);
        Assert.Contains("CcNotFound", codes);
        Assert.Empty(rig.Ctx.Letters);
    }

    [Fact]
    public async Task Register_AContractOfAnotherProject_IsRejected()
    {
        var rig = NewRig("reg9");
        rig.Ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "Vend" });
        rig.Ctx.Contracts.Add(new Contract { ContractId = 1, ContractNo = "C-1", ProjectId = 2, VendorId = 1 });
        await rig.Ctx.SaveChangesAsync();
        var input = Valid(); input.ContractId = 1;

        Assert.Contains("ContractInvalid", (await rig.Service.RegisterAsync(input, 101, 1)).Issues.Select(i => i.Code));

        input.ProjectId = 2;
        Assert.True((await rig.Service.RegisterAsync(input, 101, 1)).Success);
        Assert.Equal(1, (await rig.Ctx.Letters.SingleAsync()).ContractId);
    }

    // ------------------------------------------------------------------ files

    [Fact]
    public async Task Register_AFileWhoseContentDoesNotMatchItsExtension_IsRejected_BeforeAnythingIsStored()
    {
        var rig = NewRig("file1");
        var input = Valid();
        input.Files = new() { Pdf("scan.pdf", bytes: Encoding.ASCII.GetBytes("MZ this is an executable")) };

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.Contains(result.Issues, i => i.Code == "FileContentMismatch" && i.Detail == "scan.pdf");
        Assert.Empty(rig.Files.Saved);
        Assert.Empty(rig.Ctx.Letters);
        Assert.Empty(rig.Ctx.LetterNumberSequences);   // the number is not even consumed
    }

    [Fact]
    public async Task Register_WhenStoringALaterFileFails_TheEarlierStagedFilesAreDeleted_AndNoLetterExists()
    {
        var rig = NewRig("file2");
        rig.Files.FailOnCall = 2;
        var input = Valid(); input.Files = new() { Pdf("a.pdf"), Pdf("b.pdf", "Attachment") };

        await Assert.ThrowsAsync<IOException>(() => rig.Service.RegisterAsync(input, 101, 1));

        Assert.Equal(new[] { "key-1" }, rig.Files.Deleted);
        Assert.Empty(rig.Ctx.Letters);
        Assert.Empty(rig.Ctx.DocumentAttachments);
    }

    [Fact]
    public async Task Register_StoresEachFileWithItsRoleAndHash_OnlyTheOriginalIsOfficial()
    {
        var rig = NewRig("file3");
        var input = Valid(); input.Files = new() { Pdf("letter.pdf"), Pdf("annex.pdf", "Attachment", Encoding.ASCII.GetBytes("%PDF annex")) };

        await rig.Service.RegisterAsync(input, 101, 1);

        var files = await rig.Ctx.DocumentAttachments.OrderBy(a => a.AttachmentId).ToListAsync();
        Assert.Equal(new[] { "Original", "Attachment" }, files.Select(f => f.FileRole));
        Assert.Equal(new[] { true, false }, files.Select(f => f.IsOfficial));
        Assert.NotEqual(files[0].FileHash, files[1].FileHash);
    }

    // ------------------------------------------------------------------ duplicates

    [Fact]
    public async Task Register_ADuplicateSenderReference_IsHeldUntilConfirmed_CaseInsensitively()
    {
        var rig = NewRig("dup1");
        var partyId = await AddPartyAsync(rig.Ctx);
        var first = await rig.Service.RegisterAsync(Valid(partyId), 101, 1);

        var again = Valid(partyId); again.SenderReference = "acme-0452";
        var held = await rig.Service.RegisterAsync(again, 101, 1);

        Assert.False(held.Success);
        Assert.Contains(held.Issues, i => i.Code == "Duplicate" && i.Field == "senderReference");
        Assert.Equal(first.LetterNumber, held.DuplicateLetterNumber);
        Assert.Equal(1, await rig.Ctx.Letters.CountAsync());

        var confirmed = Valid(partyId); confirmed.SenderReference = "acme-0452"; confirmed.ConfirmDuplicate = true;
        Assert.True((await rig.Service.RegisterAsync(confirmed, 101, 1)).Success);
        Assert.Equal(2, await rig.Ctx.Letters.CountAsync());
    }

    [Fact]
    public async Task FindDuplicate_OnlyMatchesTheSamePartyAndAnActiveIncomingLetter()
    {
        var rig = NewRig("dup2");
        var a = await AddPartyAsync(rig.Ctx, "A Co");
        var b = await AddPartyAsync(rig.Ctx, "B Co");
        await rig.Service.RegisterAsync(Valid(a), 101, 1);

        Assert.NotNull(await rig.Service.FindDuplicateAsync(a, " ACME-0452 "));
        Assert.Null(await rig.Service.FindDuplicateAsync(b, "ACME-0452"));
        Assert.Null(await rig.Service.FindDuplicateAsync(a, ""));

        rig.Ctx.Letters.Single().IsActive = false;
        await rig.Ctx.SaveChangesAsync();
        Assert.Null(await rig.Service.FindDuplicateAsync(a, "ACME-0452"));
    }

    // ------------------------------------------------------------------ in reply to

    private static async Task<int> AddOutgoingAsync(PEMSDbContext ctx, int? partyId, string status = "Issued", bool responseRequired = true,
        string? receiver = null, string level = "Internal", string number = "OUT-1", DateTime? due = null)
    {
        var letter = new Letter
        {
            LetterType = "Outgoing", LetterNumber = number, Subject = "Our request", Status = status, CorrespondentPartyId = partyId, Receiver = receiver,
            ResponseRequired = responseRequired, ResponseDueDate = due ?? Today.AddDays(5), ConfidentialityLevel = level, LetterDate = Today.AddDays(-10), IsActive = true
        };
        ctx.Letters.Add(letter);
        await ctx.SaveChangesAsync();
        return letter.LetterId;
    }

    [Fact]
    public async Task Register_InReplyTo_LinksTheParent_ClosesIt_AndCountsAsTheAnswer()
    {
        var rig = NewRig("reply1");
        var partyId = await AddPartyAsync(rig.Ctx);
        var parentId = await AddOutgoingAsync(rig.Ctx, partyId);
        var input = Valid(partyId); input.ParentLetterId = parentId;

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.True(result.Success, string.Join(",", result.Issues.Select(i => i.Code)));
        var parent = await rig.Ctx.Letters.SingleAsync(l => l.LetterId == parentId);
        Assert.Equal("Closed", parent.Status);
        Assert.NotNull(parent.ClosedDate);
        Assert.Equal(parentId, (await rig.Ctx.Letters.SingleAsync(l => l.LetterId == result.LetterId)).ParentLetterId);

        var thread = await rig.Service.GetPartyThreadAsync(partyId, true, 101);
        Assert.Equal(0, thread!.AwaitingThem);
        Assert.True(thread.Items.Single(i => i.LetterId == parentId).Answered);
    }

    [Fact]
    public async Task Register_InReplyTo_RejectsDraftsIncomingLettersAndAnotherPartysLetter()
    {
        var rig = NewRig("reply2");
        var partyId = await AddPartyAsync(rig.Ctx);
        var otherParty = await AddPartyAsync(rig.Ctx, "Other Co");
        var draft = await AddOutgoingAsync(rig.Ctx, partyId, status: "Draft", number: "OUT-D");
        var foreign = await AddOutgoingAsync(rig.Ctx, otherParty, number: "OUT-F");
        var incoming = (await rig.Service.RegisterAsync(Valid(partyId), 101, 1)).LetterId!.Value;

        foreach (var parent in new[] { draft, incoming, 12345 })
        {
            var input = Valid(partyId); input.SenderReference = "R-" + parent; input.ParentLetterId = parent;
            Assert.Contains("ParentInvalid", (await rig.Service.RegisterAsync(input, 101, 1)).Issues.Select(i => i.Code));
        }

        var mismatch = Valid(partyId); mismatch.SenderReference = "R-X"; mismatch.ParentLetterId = foreign;
        Assert.Contains("ParentPartyMismatch", (await rig.Service.RegisterAsync(mismatch, 101, 1)).Issues.Select(i => i.Code));
    }

    [Fact]
    public async Task Register_InReplyTo_AParentThatDoesNotAskForAReply_StaysOpen()
    {
        var rig = NewRig("reply3");
        var partyId = await AddPartyAsync(rig.Ctx);
        var parentId = await AddOutgoingAsync(rig.Ctx, partyId, responseRequired: false);
        var input = Valid(partyId); input.ParentLetterId = parentId;

        Assert.True((await rig.Service.RegisterAsync(input, 101, 1)).Success);
        Assert.Equal("Issued", (await rig.Ctx.Letters.SingleAsync(l => l.LetterId == parentId)).Status);
    }

    // ------------------------------------------------------------------ links

    [Fact]
    public async Task Register_Links_AreSnapshottedAndTheLegacyColumnsKeepTheFirstDocumentAndMr()
    {
        var rig = NewRig("link1");
        rig.Ctx.Set<EngineeringDocument>().Add(new EngineeringDocument { DocumentNo = "ENG-001", Title = "PFD", ProjectId = 1, DocumentType = "PFD" });
        rig.Ctx.Set<VDRItem>().Add(new VDRItem { DocumentNo = "VDR-009", Title = "Pump datasheet", ProjectId = 1, DocumentType = "DS", DocumentKind = "Vendor" });
        rig.Ctx.MaterialRequisitions.Add(new MaterialRequisition { MRNumber = "MR-77", ProjectId = 1 });
        rig.Ctx.Transmittals.Add(new Transmittal { TransmittalNo = "TR-5", ProjectId = 1, From = "A", To = "B" });
        await rig.Ctx.SaveChangesAsync();

        var docId = rig.Ctx.Set<EngineeringDocument>().Single().DocumentId;
        var vdrId = rig.Ctx.Set<VDRItem>().Single().DocumentId;
        var input = Valid();
        input.Links = new()
        {
            new("EngineeringDocument", docId), new("VdrItem", vdrId), new("MaterialRequisition", rig.Ctx.MaterialRequisitions.Single().MRId),
            new("Transmittal", rig.Ctx.Transmittals.Single().TransmittalId), new("Transmittal", rig.Ctx.Transmittals.Single().TransmittalId)
        };

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.True(result.Success, string.Join(",", result.Issues.Select(i => i.Code + ":" + i.Detail)));
        var letter = await rig.Ctx.Letters.Include(l => l.Links).SingleAsync();
        Assert.Equal(4, letter.Links.Count);   // the repeated transmittal is collapsed
        Assert.Equal(new[] { "ENG-001", "MR-77", "TR-5", "VDR-009" }, letter.Links.Select(l => l.TargetNumber).OrderBy(x => x));
        Assert.Equal(docId, letter.RelatedDocumentId);
        Assert.Equal(rig.Ctx.MaterialRequisitions.Single().MRId, letter.RelatedMRId);
    }

    [Fact]
    public async Task Register_AMissingLinkTarget_FailsTheWholeRegistration()
    {
        var rig = NewRig("link2");
        var input = Valid(); input.Links = new() { new("MaterialRequisition", 4242) };

        var result = await rig.Service.RegisterAsync(input, 101, 1);

        Assert.Contains(result.Issues, i => i.Code == "LinkNotFound" && i.Detail == "MaterialRequisition:4242");
        Assert.Empty(rig.Ctx.Letters);
        Assert.Empty(rig.Files.Saved);
    }

    [Fact]
    public async Task SearchLinks_FiltersByTypeProjectAndText()
    {
        var rig = NewRig("link3");
        rig.Ctx.Set<EngineeringDocument>().AddRange(
            new EngineeringDocument { DocumentNo = "ENG-001", Title = "Process flow", ProjectId = 1, DocumentType = "PFD" },
            new EngineeringDocument { DocumentNo = "ENG-002", Title = "Layout", ProjectId = 2, DocumentType = "PLT" });
        await rig.Ctx.SaveChangesAsync();

        Assert.Equal(new[] { "ENG-001", "ENG-002" }, (await rig.Service.SearchLinksAsync("EngineeringDocument", "eng", null)).Select(l => l.Number).OrderBy(x => x));
        Assert.Equal("ENG-001", Assert.Single(await rig.Service.SearchLinksAsync("EngineeringDocument", null, 1)).Number);
        Assert.Equal("ENG-002", Assert.Single(await rig.Service.SearchLinksAsync("EngineeringDocument", "layout", null)).Number);
        Assert.Empty(await rig.Service.SearchLinksAsync("Nonsense", null, null));
    }

    // ------------------------------------------------------------------ parties and thread

    [Fact]
    public async Task SeedPartiesFromVendors_IsIdempotent_AndSkipsInactiveVendors()
    {
        var rig = NewRig("party1");
        rig.Ctx.Vendors.AddRange(new Vendor { Name = "Alpha", VendorCode = "ALP", IsActive = true }, new Vendor { Name = "Beta", IsActive = true }, new Vendor { Name = "Gone", IsActive = false });
        await rig.Ctx.SaveChangesAsync();

        Assert.Equal(2, await rig.Service.SeedPartiesFromVendorsAsync());
        Assert.Equal(0, await rig.Service.SeedPartiesFromVendorsAsync());

        var parties = await rig.Ctx.CorrespondentParties.OrderBy(p => p.Name).ToListAsync();
        Assert.Equal(new[] { "Alpha", "Beta" }, parties.Select(p => p.Name));
        Assert.All(parties, p => { Assert.Equal("Vendor", p.PartyType); Assert.False(p.IsProvisional); Assert.NotNull(p.VendorId); });
        Assert.Equal("ALP", parties[0].Code);
    }

    [Fact]
    public async Task SearchParties_MatchesNameOrCode_AndListsTheVendorsContractsForTheProject()
    {
        var rig = NewRig("party2");
        rig.Ctx.Vendors.Add(new Vendor { VendorId = 5, Name = "Alpha Pumps", VendorCode = "ALP" });
        rig.Ctx.Contracts.AddRange(new Contract { ContractNo = "C-1", ProjectId = 1, VendorId = 5 }, new Contract { ContractNo = "C-2", ProjectId = 2, VendorId = 5 });
        await rig.Ctx.SaveChangesAsync();
        await rig.Service.SeedPartiesFromVendorsAsync();
        await AddPartyAsync(rig.Ctx, "Client Corp");

        var byName = await rig.Service.SearchPartiesAsync("pump", 1);
        var party = Assert.Single(byName);
        Assert.Equal(new[] { "C-1" }, party.Contracts.Select(c => c.ContractNo));

        Assert.Single(await rig.Service.SearchPartiesAsync("alp", null));
        Assert.Equal(new[] { "C-1", "C-2" }, (await rig.Service.SearchPartiesAsync("Alpha", null)).Single().Contracts.Select(c => c.ContractNo));
        Assert.Equal(2, (await rig.Service.SearchPartiesAsync(null, null)).Count);
        Assert.Empty((await rig.Service.SearchPartiesAsync("Client", 1)).Single().Contracts);
    }

    [Fact]
    public async Task SearchPeople_FindsActiveEmployeesByNameOrPersonnelCode()
    {
        var rig = NewRig("people1");
        rig.Ctx.Employees.Single(e => e.EmployeeId == 4).IsActive = false;
        await rig.Ctx.SaveChangesAsync();

        Assert.Equal("E2 L2", Assert.Single(await rig.Service.SearchPeopleAsync("e2 l")).Name);
        Assert.Equal("E3 L3", Assert.Single(await rig.Service.SearchPeopleAsync("p3")).Name);
        Assert.DoesNotContain(await rig.Service.SearchPeopleAsync(null), p => p.EmployeeId == 4);
        Assert.Equal("Engineering", (await rig.Service.SearchPeopleAsync("e2")).Single().Department);
    }

    [Fact]
    public async Task PartyThread_CountsWhoIsWaitingOnWhom_AndFlagsOverdueLetters()
    {
        var rig = NewRig("thread1");
        var partyId = await AddPartyAsync(rig.Ctx);

        // they wrote to us twice (one needs our reply, overdue), we wrote once and wait for them
        var overdue = Valid(partyId); overdue.SenderReference = "A-1"; overdue.ResponseRequired = true; overdue.OwnerEmployeeId = 2;
        overdue.DocumentDate = Today.AddDays(-30); overdue.ReceivedDate = Today.AddDays(-20); overdue.ResponseDueDate = Today.AddDays(-5);
        var info = Valid(partyId); info.SenderReference = "A-2";
        await rig.Service.RegisterAsync(overdue, 101, 1);
        await rig.Service.RegisterAsync(info, 101, 1);
        await AddOutgoingAsync(rig.Ctx, partyId, number: "OUT-9", due: Today.AddDays(3));

        var thread = (await rig.Service.GetPartyThreadAsync(partyId, true, 101))!;

        Assert.Equal(3, thread.Total);
        Assert.Equal(1, thread.AwaitingUs);
        Assert.Equal(1, thread.AwaitingThem);
        Assert.Equal(Today, thread.LastContact!.Value.Date);
        Assert.True(thread.Items.Single(i => i.SenderReference == "A-1").Overdue);
        Assert.False(thread.Items.Single(i => i.LetterNumber == "OUT-9").Overdue);
        Assert.Equal("Outgoing", thread.Items.Single(i => i.LetterNumber == "OUT-9").Direction);
    }

    [Fact]
    public async Task PartyThread_ForALegacyOutgoingLetterWithoutAParty_MatchesTheReceiverName_AndHidesDrafts()
    {
        var rig = NewRig("thread2");
        var partyId = await AddPartyAsync(rig.Ctx, "Acme Co");
        await AddOutgoingAsync(rig.Ctx, null, receiver: "ACME CO", number: "OUT-L");
        await AddOutgoingAsync(rig.Ctx, partyId, status: "Draft", number: "OUT-D");
        await AddOutgoingAsync(rig.Ctx, null, receiver: "Someone Else", number: "OUT-X");

        var thread = (await rig.Service.GetPartyThreadAsync(partyId, true, 101))!;

        Assert.Equal(new[] { "OUT-L" }, thread.Items.Select(i => i.LetterNumber));
    }

    [Fact]
    public async Task PartyThread_AppliesTheSameConfidentialityRuleAsTheLetterList()
    {
        var rig = NewRig("thread3");
        var partyId = await AddPartyAsync(rig.Ctx);
        await AddOutgoingAsync(rig.Ctx, partyId, level: "Restricted", number: "OUT-R");
        await AddOutgoingAsync(rig.Ctx, partyId, level: "Internal", number: "OUT-I");

        var asOutsider = (await rig.Service.GetPartyThreadAsync(partyId, canViewConfidential: false, LetterTestWorld.Other))!;
        var asManager = (await rig.Service.GetPartyThreadAsync(partyId, canViewConfidential: true, LetterTestWorld.Other))!;

        Assert.Equal(new[] { "OUT-I" }, asOutsider.Items.Select(i => i.LetterNumber));
        Assert.Equal(new[] { "OUT-I", "OUT-R" }, asManager.Items.Select(i => i.LetterNumber).OrderBy(x => x));
        Assert.Equal(1, asOutsider.Total);   // the counters must not leak the hidden letter either
    }

    [Fact]
    public async Task OutgoingForReply_ListsIssuedLettersWithTheAwaitingOnesFirst()
    {
        var rig = NewRig("thread4");
        var partyId = await AddPartyAsync(rig.Ctx);
        await AddOutgoingAsync(rig.Ctx, partyId, responseRequired: false, number: "OUT-1");
        await AddOutgoingAsync(rig.Ctx, partyId, responseRequired: true, number: "OUT-2");
        await AddOutgoingAsync(rig.Ctx, partyId, status: "Draft", number: "OUT-3");

        var list = await rig.Service.GetOutgoingForReplyAsync(partyId, true, 101);

        Assert.Equal(new[] { "OUT-2", "OUT-1" }, list.Select(l => l.LetterNumber));
        Assert.Equal(new[] { true, false }, list.Select(l => l.AwaitingReply));
    }

    [Fact]
    public async Task PartyThread_ForAnUnknownParty_IsNull()
    {
        var rig = NewRig("thread5");
        Assert.Null(await rig.Service.GetPartyThreadAsync(404, true, 101));
        Assert.Empty(await rig.Service.GetOutgoingForReplyAsync(404, true, 101));
    }

    // ------------------------------------------------------------------ the answered rule

    [Fact]
    public void AnsweredChildStatuses_IncludeTheIncomingRegistrationStatuses_AndStillTheOutgoingOnes()
    {
        foreach (var status in new[] { "Registered", "UnderReview", "Referred", "Issued", "Sent", "Acknowledged", "Closed" })
            Assert.Contains(status, LetterResponseRules.AnsweredChildStatuses);
        foreach (var status in new[] { "Draft", "PendingApproval", "Rejected" })
            Assert.DoesNotContain(status, LetterResponseRules.AnsweredChildStatuses);
    }
}
