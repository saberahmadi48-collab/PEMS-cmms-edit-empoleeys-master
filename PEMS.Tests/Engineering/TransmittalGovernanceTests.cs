using PEMS.Application.DTOs;
using PEMS.Application.Exceptions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Engineering;

/// <summary>
/// طبق بخش G ممیزی سخت‌سازی مهندسی (Transmittal Governance): مستقیماً روی TransmittalService
/// InMemory - هیچ منطق این سرویس به SQL خام وابسته نیست (بخش یکتایی سطح دیتابیس با ایندکس
/// Unique/DbUpdateException جدا در NumberingRegressionTests بررسی می‌شود چون به SQL Server
/// واقعی نیاز دارد).
/// </summary>
public class TransmittalGovernanceTests
{
    private static async Task<(TransmittalService svc, Project project, PEMS.Persistence.Context.PEMSDbContext ctx)> SeedAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory(dbName);
        var project = new Project { ProjectName = "Test Project", ProjectCode = "TP" };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();

        return (new TransmittalService(ctx), project, ctx);
    }

    private static async Task<Document> SeedDocumentAsync(PEMS.Persistence.Context.PEMSDbContext ctx, int projectId, bool onHold = false)
    {
        var doc = new EngineeringDocument
        {
            DocumentNo = $"TP-GEN-DAT-{Guid.NewGuid():N}"[..20],
            Title = "Test Doc",
            DocumentType = "Datasheet",
            ProjectId = projectId,
            Status = "Draft",
            RevisionNo = "00",
            IsOnHold = onHold
        };

        ctx.EngineeringDocuments.Add(doc);
        await ctx.SaveChangesAsync();
        return doc;
    }

    private static List<TransmittalRecipientInput> OneRecipient() => new()
    {
        new TransmittalRecipientInput { Name = "Client DCC", RecipientType = "Client" }
    };

    [Fact]
    public async Task CreateAsync_DuplicateTransmittalNo_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(CreateAsync_DuplicateTransmittalNo_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        await svc.CreateAsync("TR-001", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateAsync("TR-001", project.ProjectId, "Eng", "Client", "IFA", null,
                new List<int> { doc.DocumentId }, false, OneRecipient()));
        Assert.Equal("transmittal-code-duplicate", error.Code);
    }

    [Fact]
    public async Task CreateAsync_DocumentOnHold_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(CreateAsync_DocumentOnHold_Throws));
        var heldDoc = await SeedDocumentAsync(ctx, project.ProjectId, onHold: true);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateAsync("TR-002", project.ProjectId, "Eng", "Client", "IFA", null,
                new List<int> { heldDoc.DocumentId }, false, OneRecipient()));
        Assert.Equal("transmittal-documents-held", error.Code);
    }

    [Fact]
    public async Task CreateAsync_SnapshotsRevisionNoAtTimeOfCreation()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(CreateAsync_SnapshotsRevisionNoAtTimeOfCreation));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-003", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());

        // طبق بخش ۸-۹ (Immutability + Traceability): رویژن در لحظه‌ی ارسال ثبت می‌شود؛ تغییر
        // بعدی رویژن مدرک نباید تاریخچه‌ی این ترانسمیتال را عوض کند
        doc.RevisionNo = "01";
        await ctx.SaveChangesAsync();

        var dto = await svc.GetByIdAsync(id);
        Assert.Equal("00", dto!.Items.Single().RevisionNo);
    }

    [Fact]
    public async Task MarkSentAsync_ResponseRequired_ComputesDueDateViaWorkingDayCalculator()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(MarkSentAsync_ResponseRequired_ComputesDueDateViaWorkingDayCalculator));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-004", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, true, OneRecipient());

        await svc.MarkSentAsync(id, responseDueWorkingDays: 10);

        var dto = await svc.GetByIdAsync(id);
        Assert.NotNull(dto!.ResponseDueDate);
        Assert.NotEqual(DayOfWeek.Friday, dto.ResponseDueDate!.Value.DayOfWeek);
        Assert.True(dto.ResponseDueDate.Value > DateTime.Now.AddDays(9));
    }

    [Fact]
    public async Task MarkSentAsync_ResponseNotRequired_LeavesDueDateNull()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(MarkSentAsync_ResponseNotRequired_LeavesDueDateNull));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-005", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());

        await svc.MarkSentAsync(id);

        var dto = await svc.GetByIdAsync(id);
        Assert.False(dto!.ResponseRequired);
        Assert.Null(dto.ResponseDueDate);
    }

    [Fact]
    public async Task MarkSentAsync_NoRecipients_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(MarkSentAsync_NoRecipients_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-006", project.ProjectId, "Eng", "", "IFA", null,
            new List<int> { doc.DocumentId }, false, new List<TransmittalRecipientInput>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MarkSentAsync(id));
    }

    [Fact]
    public async Task MarkSentAsync_AlreadySent_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(MarkSentAsync_AlreadySent_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-007", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());
        await svc.MarkSentAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.MarkSentAsync(id));
    }

    [Fact]
    public async Task CancelAsync_ThenReissueAsync_CreatesLinkedDraftPreservingItemsAndRecipients()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(CancelAsync_ThenReissueAsync_CreatesLinkedDraftPreservingItemsAndRecipients));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-008", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());
        await svc.MarkSentAsync(id);

        await svc.CancelAsync(id, "wrong revision attached", actorEmployeeId: 5);

        var cancelled = await svc.GetByIdAsync(id);
        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.Equal("wrong revision attached", cancelled.CancelledReason);

        // طبق بخش ۸ (Immutability): تاریخچه‌ی صدور اصلی (SentDate/مدارک/گیرندگان) نباید پاک شود
        Assert.NotNull(cancelled.SentDate);
        Assert.Single(cancelled.Items);
        Assert.Single(cancelled.Recipients);

        var newId = await svc.ReissueAsync(id, actorEmployeeId: 5);
        var reissued = await svc.GetByIdAsync(newId);

        Assert.Equal("Draft", reissued!.Status);
        Assert.Equal(id, reissued.SupersedesTransmittalId);
        Assert.Single(reissued.Items);
        Assert.Single(reissued.Recipients);

        var reloadedOriginal = await svc.GetByIdAsync(id);
        Assert.Equal(newId, reloadedOriginal!.SupersededByTransmittalId);
    }

    [Fact]
    public async Task ReissueAsync_OnNonCancelledTransmittal_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(ReissueAsync_OnNonCancelledTransmittal_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-009", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ReissueAsync(id, 1));
    }

    [Fact]
    public async Task RecordResponseAsync_BeforeIssue_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(RecordResponseAsync_BeforeIssue_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-010", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, true, OneRecipient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordResponseAsync(id, "note"));
    }

    [Fact]
    public async Task RecordResponseAsync_WhenNotRequired_Throws()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(RecordResponseAsync_WhenNotRequired_Throws));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-011", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, false, OneRecipient());
        await svc.MarkSentAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RecordResponseAsync(id, "note"));
    }

    [Fact]
    public async Task RecordResponseAsync_AfterIssueWithResponseRequired_Succeeds()
    {
        var (svc, project, ctx) = await SeedAsync(nameof(RecordResponseAsync_AfterIssueWithResponseRequired_Succeeds));
        var doc = await SeedDocumentAsync(ctx, project.ProjectId);

        var id = await svc.CreateAsync("TR-012", project.ProjectId, "Eng", "Client", "IFA", null,
            new List<int> { doc.DocumentId }, true, OneRecipient());
        await svc.MarkSentAsync(id);

        await svc.RecordResponseAsync(id, "approved with comments");

        var dto = await svc.GetByIdAsync(id);
        Assert.NotNull(dto!.ResponseReceivedDate);
        Assert.Equal("approved with comments", dto.ResponseNote);
        Assert.False(dto.IsResponseOverdue);
    }
}
