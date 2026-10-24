using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using static PEMS.Tests.TestSupport.LetterTestWorld;

namespace PEMS.Tests.Cmms;

/// <summary>فرم نوشتن نامه‌ی رسمی: پیش‌نویس، بلوک فرستنده، گیرندگان To/Cc/Bcc، اعتبارسنجی، هشدارها، نسخه‌ها، دایرکتوری و CSV.</summary>
public class LetterComposeServiceTests
{
    private static readonly int Year = DateTime.Now.Year;

    private static bool Has(IEnumerable<LetterComposeIssue> issues, string code) => issues.Any(i => i.Code == code);

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task NewDraft_GetsADepartmentNumber_SenderSnapshot_RecipientsAndVersionOne()
    {
        var (compose, _, ctx, _) = Services(nameof(NewDraft_GetsADepartmentNumber_SenderSnapshot_RecipientsAndVersionOne));
        var input = ValidInput();
        input.CcEmployeeIds = new() { 3 };
        input.BccEmployeeIds = new() { 4 };
        input.ReferenceNumber = "REF-9";
        input.Body = "<p>Hello</p><script>alert(1)</script>";

        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.True(result.Success, string.Join(",", result.Errors.Select(e => e.Code)));
        Assert.Equal($"ENG01-{Year}-00001", result.LetterNumber);
        Assert.Equal(1, result.VersionNo);

        var letter = ctx.Letters.Single(l => l.LetterId == result.LetterId);
        Assert.Equal("Draft", letter.Status);
        Assert.Equal(Author, letter.RegisteredById);
        Assert.Equal("Internal", letter.LetterType);
        Assert.Equal("Request", letter.Category);
        Assert.Equal("REF-9", letter.ReferenceNumber);
        Assert.Equal(1, letter.SenderEmployeeId);
        Assert.Equal("Lead Engineer", letter.SenderTitle);          // position title snapshot
        Assert.Equal(1, letter.SenderDepartmentId);
        Assert.Equal("E1 L1", letter.Sender);
        Assert.Equal("E2 L2", letter.Receiver);                      // To names, for list/PDF
        Assert.Equal(2, letter.ReceiverEmployeeId);
        Assert.DoesNotContain("script", letter.Body);

        var recipients = ctx.LetterRecipients.Where(r => r.LetterId == letter.LetterId).ToList();
        Assert.Equal(new[] { "Bcc:4", "Cc:3", "To:2" }, recipients.Select(r => $"{r.RecipientType}:{r.EmployeeId}").OrderBy(x => x));
        Assert.Single(ctx.LetterVersions.Where(v => v.LetterId == letter.LetterId));
    }

    [Fact]
    public async Task TheSenderDepartmentIsEditable_AndDrivesTheNumberPrefix()
    {
        var (compose, _, ctx, _) = Services(nameof(TheSenderDepartmentIsEditable_AndDrivesTheNumberPrefix));
        ctx.Departments.Add(new Department { DepartmentId = 2, Code = "HSE", Name = "HSE" });
        await ctx.SaveChangesAsync();
        var input = ValidInput();
        input.SenderDepartmentId = 2;

        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.Equal($"HSE-{Year}-00001", result.LetterNumber);
        Assert.Equal(2, ctx.Letters.Single(l => l.LetterId == result.LetterId).SenderDepartmentId);
    }

    [Fact]
    public async Task OutgoingLettersAlwaysRequireApproval_EvenIfTheFormSaysNo()
    {
        var (compose, _, ctx, _) = Services(nameof(OutgoingLettersAlwaysRequireApproval_EvenIfTheFormSaysNo));
        var input = ValidInput("Outgoing");
        input.ApprovalRequired = false;

        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.True(result.Success);
        var letter = ctx.Letters.Single(l => l.LetterId == result.LetterId);
        Assert.True(letter.ApprovalRequired);
        Assert.Equal("ACME Ltd", letter.Receiver);
        Assert.Null(letter.ReceiverEmployeeId);
    }

    [Fact]
    public async Task ManualNumbers_NeedThePermission_AndMustBeUnique()
    {
        var (compose, _, ctx, _) = Services(nameof(ManualNumbers_NeedThePermission_AndMustBeUnique));
        var input = ValidInput();
        input.ManualNumber = "MY-NUMBER-1";

        var without = await compose.SaveDraftAsync(input, AuthorActor(manualNumber: false));
        Assert.StartsWith($"ENG01-{Year}", without.LetterNumber);   // ignored without ManualNumber permission

        var withPermission = await compose.SaveDraftAsync(ValidInputWithManual("MY-NUMBER-1"), AuthorActor(manualNumber: true));
        Assert.Equal("MY-NUMBER-1", withPermission.LetterNumber);

        var duplicate = await compose.SaveDraftAsync(ValidInputWithManual("MY-NUMBER-1"), AuthorActor(manualNumber: true));
        Assert.False(duplicate.Success);
        Assert.True(Has(duplicate.Errors, "NumberInUse"));
        Assert.Equal(2, ctx.Letters.Count());

        static LetterComposeInput ValidInputWithManual(string n) { var i = ValidInput(); i.ManualNumber = n; return i; }
    }

    // ------------------------------------------------------------------ validation

    [Theory]
    [InlineData("subject", "SubjectTooLong")]
    [InlineData("category", "CategoryInvalid")]
    [InlineData("priority", "PriorityInvalid")]
    [InlineData("confidentiality", "ConfidentialityInvalid")]
    [InlineData("type", "TypeInvalid")]
    [InlineData("reference", "ReferenceTooLong")]
    [InlineData("duplicate", "RecipientDuplicate")]
    [InlineData("responseDue", "ResponseDueRequired")]
    [InlineData("responseEarly", "ResponseDueBeforeLetterDate")]
    [InlineData("chainDup", "ApprovalChainDuplicate")]
    public async Task StructuralMistakes_AreRejectedWithAStableCode(string mutation, string expectedCode)
    {
        var (compose, _, ctx, _) = Services("Struct_" + mutation);
        var input = ValidInput();
        switch (mutation)
        {
            case "subject": input.Subject = new string('x', LetterComposeRules.MaxSubjectLength + 1); break;
            case "category": input.Category = "Gossip"; break;
            case "priority": input.Priority = "Whenever"; break;
            case "confidentiality": input.ConfidentialityLevel = "TopSecret"; break;
            case "type": input.LetterType = "Incoming"; break;
            case "reference": input.ReferenceNumber = new string('r', 101); break;
            case "duplicate": input.CcEmployeeIds = new() { 2 }; break;   // same person in To and Cc
            case "responseDue": input.ResponseRequired = true; input.ResponseDueDate = null; break;
            case "responseEarly": input.ResponseRequired = true; input.LetterDate = DateTime.Today; input.ResponseDueDate = DateTime.Today.AddDays(-1); break;
            case "chainDup": input.ApprovalRequired = true; input.ApproverUserIds = new() { Approver1, Approver1 }; break;
        }

        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.False(result.Success);
        Assert.True(Has(result.Errors, expectedCode), "got: " + string.Join(",", result.Errors.Select(e => e.Code)));
        Assert.Empty(ctx.Letters);                        // nothing is stored on a rejected save
    }

    [Fact]
    public async Task RecipientsMustExistAndBeActive()
    {
        var (compose, _, ctx, _) = Services(nameof(RecipientsMustExistAndBeActive));
        ctx.Employees.Single(e => e.EmployeeId == 3).IsActive = false;
        await ctx.SaveChangesAsync();

        var input = ValidInput();
        input.CcEmployeeIds = new() { 3, 999 };
        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.False(result.Success);
        Assert.True(Has(result.Errors, "RecipientInactive"));
        Assert.True(Has(result.Errors, "RecipientNotFound"));
        Assert.Equal("999", result.Errors.Single(e => e.Code == "RecipientNotFound").Detail);
    }

    [Fact]
    public async Task MoreThanFiveHundredRecipientsIsAnError()
    {
        var (compose, _, _, _) = Services(nameof(MoreThanFiveHundredRecipientsIsAnError));
        var input = ValidInput();
        input.ToEmployeeIds = Enumerable.Range(1000, 501).ToList();

        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.True(Has(result.Errors, "TooManyRecipients"));
    }

    [Fact]
    public async Task Approvers_MustHoldTheApprovePermission_AndNeverBeTheAuthor()
    {
        var (compose, _, _, _) = Services(nameof(Approvers_MustHoldTheApprovePermission_AndNeverBeTheAuthor));

        var notAllowed = ValidInput(); notAllowed.ApprovalRequired = true; notAllowed.ApproverUserIds = new() { To1 };   // no Approve role
        var self = ValidInput(); self.ApprovalRequired = true; self.ApproverUserIds = new() { Author };

        Assert.True(Has((await compose.SaveDraftAsync(notAllowed, AuthorActor())).Errors, "ApproverNotAllowed"));
        Assert.True(Has((await compose.SaveDraftAsync(self, AuthorActor())).Errors, "ApproverIsAuthor"));
    }

    // ------------------------------------------------------------------ update rules

    [Fact]
    public async Task UpdatingADraft_ReplacesRecipientsAndTheApprovalChain()
    {
        var (compose, _, ctx, _) = Services(nameof(UpdatingADraft_ReplacesRecipientsAndTheApprovalChain));
        var first = ValidInput();
        first.CcEmployeeIds = new() { 3 };
        first.ApprovalRequired = true;
        first.ApproverUserIds = new() { Approver1, Approver2 };
        var saved = await compose.SaveDraftAsync(first, AuthorActor());
        Assert.True(saved.Success);

        var edit = (await compose.LoadForEditAsync(saved.LetterId, AuthorActor()))!;
        edit.ToEmployeeIds = new() { 2, 5 };
        edit.CcEmployeeIds = new();
        edit.BccEmployeeIds = new() { 4 };
        edit.ApproverUserIds = new() { Approver2 };
        edit.Subject = "Revised subject";
        var updated = await compose.SaveDraftAsync(edit, AuthorActor());

        Assert.True(updated.Success, string.Join(",", updated.Errors.Select(e => e.Code)));
        Assert.Equal(saved.LetterNumber, updated.LetterNumber);                         // number never changes
        var recipients = ctx.LetterRecipients.Where(r => r.LetterId == saved.LetterId).Select(r => $"{r.RecipientType}:{r.EmployeeId}").OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Bcc:4", "To:2", "To:5" }, recipients);
        var steps = ctx.LetterApprovalSteps.Where(s => s.LetterId == saved.LetterId).OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(new[] { Approver2 }, steps.Select(s => s.ApproverUserId));
        Assert.Equal("Pending", steps[0].Status);
        Assert.Equal("E2 L2, E5 L5", ctx.Letters.Single(l => l.LetterId == saved.LetterId).Receiver);
    }

    [Fact]
    public async Task OnlyTheAuthorCanEdit_AndOnlyWhileTheLetterIsADraftOrReturned()
    {
        var (compose, _, ctx, _) = Services(nameof(OnlyTheAuthorCanEdit_AndOnlyWhileTheLetterIsADraftOrReturned));
        var saved = await compose.SaveDraftAsync(ValidInput(), AuthorActor());
        var edit = (await compose.LoadForEditAsync(saved.LetterId, AuthorActor()))!;

        var stranger = new LetterComposeActor(Other, "u5", 5, false, false);
        Assert.True(Has((await compose.SaveDraftAsync(edit, stranger)).Errors, "NotAuthor"));
        Assert.Null(await compose.LoadForEditAsync(saved.LetterId, stranger));

        foreach (var status in new[] { "PendingApproval", "Approved", "Sent", "Issued", "Archived" })
        {
            ctx.Letters.Single(l => l.LetterId == saved.LetterId).Status = status;
            await ctx.SaveChangesAsync();
            Assert.True(Has((await compose.SaveDraftAsync(edit, AuthorActor())).Errors, "NotEditable"), status);
            Assert.Null(await compose.LoadForEditAsync(saved.LetterId, AuthorActor()));
        }

        ctx.Letters.Single(l => l.LetterId == saved.LetterId).Status = "ReturnedForRevision";
        await ctx.SaveChangesAsync();
        Assert.True((await compose.SaveDraftAsync(edit, AuthorActor())).Success);          // returned letters are editable again
    }

    // ------------------------------------------------------------------ versions

    [Fact]
    public async Task Versions_ExplicitSavesAreAppendOnly_AutoSavesMerge_AndUnchangedContentAddsNothing()
    {
        var (compose, _, ctx, _) = Services(nameof(Versions_ExplicitSavesAreAppendOnly_AutoSavesMerge_AndUnchangedContentAddsNothing));
        var saved = await compose.SaveDraftAsync(ValidInput(), AuthorActor());                    // v1 (explicit)
        var edit = (await compose.LoadForEditAsync(saved.LetterId, AuthorActor()))!;

        edit.Body = "<p>auto 1</p>";
        var a1 = await compose.SaveDraftAsync(edit, AuthorActor(), autoSave: true);              // v2 (auto)
        edit.Body = "<p>auto 2</p>";
        var a2 = await compose.SaveDraftAsync(edit, AuthorActor(), autoSave: true);              // merges into v2
        var same = await compose.SaveDraftAsync(edit, AuthorActor(), autoSave: true);            // identical -> nothing new
        edit.Body = "<p>explicit</p>";
        var explicitSave = await compose.SaveDraftAsync(edit, AuthorActor());                    // v3 (explicit, never overwrites v1/v2)

        Assert.Equal(1, saved.VersionNo);
        Assert.Equal(2, a1.VersionNo);
        Assert.Equal(2, a2.VersionNo);
        Assert.Equal(2, same.VersionNo);
        Assert.Equal(3, explicitSave.VersionNo);

        var versions = ctx.LetterVersions.Where(v => v.LetterId == saved.LetterId).OrderBy(v => v.VersionNo).ToList();
        Assert.Equal(3, versions.Count);
        Assert.Contains("auto 2", versions[1].Body);                  // merged auto-save kept the latest content
        Assert.Contains("Please review", versions[0].Body);           // the first explicit snapshot is untouched
        Assert.Equal(new[] { "Draft", "AutoSave", "Draft" }, versions.Select(v => v.Reason));

        var listed = await compose.GetVersionsAsync(saved.LetterId);
        Assert.Equal(new[] { 3, 2, 1 }, listed.Select(v => v.VersionNo));
        Assert.Equal("User 1", listed[0].SavedByName);
    }

    // ------------------------------------------------------------------ warnings

    [Fact]
    public async Task Warnings_FlagManyRecipients_ExternalPeople_SelfAndPendingWork_WithoutBlockingTheSave()
    {
        var (compose, _, ctx, _) = Services(nameof(Warnings_FlagManyRecipients_ExternalPeople_SelfAndPendingWork_WithoutBlockingTheSave));
        for (var i = 8; i < 60; i++)
            ctx.Employees.Add(new Employee { EmployeeId = i, FirstName = "X" + i, LastName = "Y", IsActive = true, DepartmentId = 1 });
        ctx.Employees.Single(e => e.EmployeeId == 3).PersonnelType = "Contractor";
        ctx.Letters.Add(new Letter { LetterId = 900, LetterNumber = "OLD-1", LetterType = "Incoming", Subject = "x", IsActive = true });
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = 900, ReferredToEmployeeId = 2, Status = "Pending", DueDate = DateTime.Now.AddDays(-2) });
        ctx.TaskItems.Add(new TaskItem { Title = "late task", AssignedToEmployeeId = 2, DueDate = DateTime.Now.AddDays(-1), Status = "Open" });
        await ctx.SaveChangesAsync();

        var input = ValidInput();
        input.ToEmployeeIds = new() { 1, 2 };                                             // 1 = the author themself
        input.CcEmployeeIds = new[] { 3 }.Concat(Enumerable.Range(8, 52)).ToList();       // 53 people, one contractor
        var result = await compose.SaveDraftAsync(input, AuthorActor());

        Assert.True(result.Success);                                                      // warnings never block
        Assert.True(Has(result.Warnings, "ManyRecipients"));
        Assert.True(Has(result.Warnings, "ExternalRecipient"));
        Assert.True(Has(result.Warnings, "SenderInRecipients"));
        var pending = result.Warnings.Single(w => w.Code == "RecipientPendingWork");
        Assert.Contains("E2 L2: 2", pending.Detail);                                       // one overdue referral + one overdue task
    }

    [Fact]
    public async Task NoWarnings_ForAnOrdinaryLetter()
    {
        var (compose, _, _, _) = Services(nameof(NoWarnings_ForAnOrdinaryLetter));

        var result = await compose.SaveDraftAsync(ValidInput(), AuthorActor());

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    // ------------------------------------------------------------------ submit validation

    [Fact]
    public async Task ValidateForSubmit_ListsEverythingMissing_ThenPassesWhenComplete()
    {
        var (compose, _, _, _) = Services(nameof(ValidateForSubmit_ListsEverythingMissing_ThenPassesWhenComplete));
        var skeleton = new LetterComposeInput { LetterType = "Internal", ApprovalRequired = true };
        var saved = await compose.SaveDraftAsync(skeleton, AuthorActor(), autoSave: true);       // an empty auto-saved draft is fine
        Assert.True(saved.Success);

        var incomplete = await compose.ValidateForSubmitAsync(saved.LetterId, AuthorActor());
        Assert.False(incomplete.Success);
        foreach (var code in new[] { "SubjectRequired", "CategoryRequired", "BodyRequired", "RecipientRequired", "ApprovalChainRequired" })
            Assert.True(Has(incomplete.Errors, code), code);

        var complete = ValidInput();
        complete.LetterId = saved.LetterId;
        complete.ApprovalRequired = true;
        complete.ApproverUserIds = new() { Approver1 };
        Assert.True((await compose.SaveDraftAsync(complete, AuthorActor())).Success);

        var ok = await compose.ValidateForSubmitAsync(saved.LetterId, AuthorActor());
        Assert.True(ok.Success, string.Join(",", ok.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task ValidateForSubmit_CatchesARecipientDeactivatedAfterTheDraftWasSaved()
    {
        var (compose, _, ctx, _) = Services(nameof(ValidateForSubmit_CatchesARecipientDeactivatedAfterTheDraftWasSaved));
        var saved = await compose.SaveDraftAsync(ValidInput(), AuthorActor());
        ctx.Employees.Single(e => e.EmployeeId == 2).IsActive = false;
        await ctx.SaveChangesAsync();

        var result = await compose.ValidateForSubmitAsync(saved.LetterId, AuthorActor());

        Assert.False(result.Success);
        Assert.True(Has(result.Errors, "RecipientInactive"));
    }

    // ------------------------------------------------------------------ directory, CSV, approvers, sender

    [Fact]
    public async Task DirectorySearch_MatchesNameTitleDepartmentEmailAndCode_ForActiveEmployeesOnly()
    {
        var (compose, _, ctx, _) = Services(nameof(DirectorySearch_MatchesNameTitleDepartmentEmailAndCode_ForActiveEmployeesOnly));
        ctx.Employees.Single(e => e.EmployeeId == 7).IsActive = false;
        await ctx.SaveChangesAsync();

        Assert.Equal(new[] { "E3 L3" }, (await compose.SearchDirectoryAsync("e3 l3")).Select(e => e.FullName));
        Assert.Equal(new[] { "E1 L1" }, (await compose.SearchDirectoryAsync("lead engineer")).Select(e => e.FullName));
        Assert.Equal(6, (await compose.SearchDirectoryAsync("engineering", 50)).Count);            // 7 employees, one inactive
        Assert.Single(await compose.SearchDirectoryAsync("e5@pems.test"));
        Assert.Single(await compose.SearchDirectoryAsync("P2"));
        Assert.Empty(await compose.SearchDirectoryAsync("E7 L7"));
        Assert.Equal(3, (await compose.SearchDirectoryAsync("", 3)).Count);
        Assert.Equal(1, (await compose.SearchDirectoryAsync("", 0)).Count);                         // take is clamped to at least 1
    }

    [Fact]
    public async Task CsvResolve_MatchesCodeEmailAndName_ReportsUnknownAndAmbiguous_AndDeduplicates()
    {
        var (compose, _, ctx, _) = Services(nameof(CsvResolve_MatchesCodeEmailAndName_ReportsUnknownAndAmbiguous_AndDeduplicates));
        ctx.Employees.Add(new Employee { EmployeeId = 20, FirstName = "Twin", LastName = "Name", IsActive = true, DepartmentId = 1 });
        ctx.Employees.Add(new Employee { EmployeeId = 21, FirstName = "Twin", LastName = "Name", IsActive = true, DepartmentId = 1 });
        await ctx.SaveChangesAsync();

        var result = await compose.ResolveRecipientsAsync("P2\r\ne3@pems.test; E4 L4, P2 ,nobody@x.com\nTwin Name");

        Assert.Equal(new[] { 2, 3, 4 }, result.Matched.Select(m => m.EmployeeId).OrderBy(x => x));
        Assert.Contains("nobody@x.com", result.Unmatched);
        Assert.Contains("Twin Name (ambiguous)", result.Unmatched);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task CsvResolve_IsCappedAtTheLimit()
    {
        var (compose, _, _, _) = Services(nameof(CsvResolve_IsCappedAtTheLimit));
        var text = string.Join("\n", Enumerable.Range(1, 520).Select(i => "nobody" + i));

        var result = await compose.ResolveRecipientsAsync(text, 500);

        Assert.True(result.Truncated);
        Assert.Equal(500, result.Unmatched.Count);
    }

    [Fact]
    public async Task ApproverOptions_AreAllActiveStaff_NotOnlyTheApproveRoleHolders()
    {
        var (compose, _, ctx, _) = Services(nameof(ApproverOptions_AreAllActiveStaff_NotOnlyTheApproveRoleHolders));
        ctx.Users.Single(u => u.Id == Approver2).IsActive = false;
        await ctx.SaveChangesAsync();

        var options = await compose.GetApproverOptionsAsync();

        Assert.Equal(new[] { 101, 102, 103, 104, Other, Approver1 }, options.Select(o => o.UserId).OrderBy(x => x));   // every active employee with an account; inactive users stay out
        Assert.Equal(new[] { Other, Approver1 }, options.Where(o => o.Group == "Eligible").Select(o => o.UserId).OrderBy(x => x));
        Assert.All(options, o => Assert.False(string.IsNullOrWhiteSpace(o.FullName)));
    }

    [Fact]
    public async Task SenderBlock_IsFilledFromTheEmployeeRecord()
    {
        var (compose, _, ctx, _) = Services(nameof(SenderBlock_IsFilledFromTheEmployeeRecord));
        ctx.Employees.Single(e => e.EmployeeId == 1).SignaturePath = "/uploads/signatures/s.png";
        await ctx.SaveChangesAsync();

        var block = await compose.GetSenderBlockAsync(Author);

        Assert.Equal("E1 L1", block.FullName);
        Assert.Equal("Lead Engineer", block.Title);
        Assert.Equal("Engineering", block.DepartmentName);
        Assert.Equal("e1@pems.test", block.Email);
        Assert.Equal("01", block.Phone);
        Assert.Equal("/uploads/signatures/s.png", block.SignaturePath);
        Assert.Equal("", (await compose.GetSenderBlockAsync(99999)).FullName);                       // unknown user -> empty block, no crash
    }

    // ------------------------------------------------------------------ discard

    [Fact]
    public async Task DiscardDraft_IsSoft_AuditedAndOnlyForTheAuthorsOwnDraft()
    {
        var (compose, _, ctx, _) = Services(nameof(DiscardDraft_IsSoft_AuditedAndOnlyForTheAuthorsOwnDraft));
        var saved = await compose.SaveDraftAsync(ValidInput(), AuthorActor());

        Assert.False((await compose.DiscardDraftAsync(saved.LetterId, new LetterComposeActor(Other, "u5", 5, false, false))).Success);
        Assert.True(ctx.Letters.Single(l => l.LetterId == saved.LetterId).IsActive);

        Assert.True((await compose.DiscardDraftAsync(saved.LetterId, AuthorActor())).Success);
        var letter = ctx.Letters.Single(l => l.LetterId == saved.LetterId);
        Assert.False(letter.IsActive);                                                               // the row (and its number) remains
        var audit = Assert.Single(ctx.AuditLogs.Where(a => a.Action == "Discard"));
        Assert.Equal(Author, audit.UserId);
        Assert.Contains(letter.LetterNumber, audit.ChangedFieldsSummary);

        var sent = await compose.SaveDraftAsync(ValidInput(), AuthorActor());
        ctx.Letters.Single(l => l.LetterId == sent.LetterId).Status = "Sent";
        await ctx.SaveChangesAsync();
        Assert.False((await compose.DiscardDraftAsync(sent.LetterId, AuthorActor())).Success);        // sent letters are immutable
    }

    // ------------------------------------------------------------------ pure rules

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("<p> &nbsp; </p>", "")]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("<p>a</p><p>b</p>", "a b")]
    public void PlainText_StripsTagsAndBlankSpace(string? html, string expected) => Assert.Equal(expected, LetterComposeRules.PlainText(html));

    [Theory]
    [InlineData("memo.pdf", 100, null)]
    [InlineData("SHEET.XLSX", 100, null)]
    [InlineData("drawing.dwg", 100, null)]
    [InlineData("run.exe", 100, "AttachmentTypeNotAllowed")]
    [InlineData("script.js", 100, "AttachmentTypeNotAllowed")]
    [InlineData("noextension", 100, "AttachmentTypeNotAllowed")]
    [InlineData("big.pdf", 25L * 1024 * 1024 + 1, "AttachmentTooLarge")]
    [InlineData("exact.pdf", 25L * 1024 * 1024, null)]
    [InlineData("empty.pdf", 0, "AttachmentEmpty")]
    public void AttachmentRules_CheckTypeAndTheTwentyFiveMegabyteLimit(string name, long length, string? expected)
        => Assert.Equal(expected, LetterComposeRules.ValidateAttachment(name, length));
}
