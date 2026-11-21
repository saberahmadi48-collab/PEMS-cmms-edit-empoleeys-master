using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Phase 2B»: VDRController پیش از این فاز هیچ بررسی مجوزی نداشت - داده‌ی
/// RolePermissions برای ماژول "VDR" از قبل در PermissionService.SeedDefaultsAsync وجود
/// داشت (EngineeringManager: Create/Edit/Delete/Approve، DocumentController: Create/Edit،
/// DisciplineEngineer: Edit/Approve، Viewer: فقط View) ولی هرگز به کنترلر وصل نشده بود. این
/// تست‌ها ثابت می‌کنند گیت جدید واقعاً کار می‌کند - و طبق تصمیم صریح کاربر، Index/Details
/// (فقط‌خواندنی) عمداً دست‌نخورده و برای همه‌ی نقش‌ها در دسترس می‌مانند تا هیچ‌کس از صفحاتی
/// که امروز مرور می‌کند قفل نشود. سرویس‌های وابسته (Workflow/Numbering/Ftp) با Spy/Throwing
/// Fake جایگزین شدند تا این تست‌ها فقط مسیر مجوز را بسنجند، نه منطق کسب‌وکار خودِ آن سرویس‌ها
/// (که جای دیگری پوشش داده شده/می‌شود).
/// </summary>
public class VDRControllerAuthorizationTests
{
    private class SpyVdrWorkflowService : IVdrWorkflowService
    {
        public bool SubmitForReviewCalled;
        public bool DisciplineRespondCalled;
        public bool LeadReviewCalled;
        public bool ReturnToVendorCalled;

        public Task SubmitForReviewAsync(int vdrItemId, int revisionNo, string vendorTransmittalNo, DateTime? vendorActualIssuedDate,
            string? purposeOfIssueCode, IReadOnlyList<int>? disciplineIds, int? actionByEmployeeId, IReadOnlyList<string> actorRoles,
            string? ipAddress = null, string? userAgent = null)
        {
            SubmitForReviewCalled = true;
            return Task.CompletedTask;
        }

        public Task DisciplineRespondAsync(int vdrItemId, string decision, string? comment, int actorEmployeeId, int actorDisciplineId,
            string? ipAddress = null, string? userAgent = null)
        {
            DisciplineRespondCalled = true;
            return Task.CompletedTask;
        }

        public Task LeadReviewAsync(int vdrItemId, string reviewCategory, string? commentSheetNo, DateTime? commentDate,
            int? actionByEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress = null, string? userAgent = null)
        {
            LeadReviewCalled = true;
            return Task.CompletedTask;
        }

        public Task ReturnToVendorAsync(int vdrItemId, string? replySheetNo, string? clientTransmittalNo,
            int? actionByEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress = null, string? userAgent = null)
        {
            ReturnToVendorCalled = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VdrReviewStatusItemDto>> GetDisciplineReviewStatusAsync(int vdrItemId) =>
            Task.FromResult<IReadOnlyList<VdrReviewStatusItemDto>>(new List<VdrReviewStatusItemDto>());
    }

    private class SpyVDRService : IVDRService
    {
        public bool CreateCalled;
        public VDRItemDto? ItemToReturn;

        public Task<List<VDRItemDto>> GetAllAsync(int? projectId = null, int? vendorId = null, string? workflowStatus = null) =>
            Task.FromResult(new List<VDRItemDto>());

        public Task<PagedResult<VDRListItemDto>> GetPagedAsync(int? projectId, int? vendorId, string? workflowStatus, int page, int pageSize, string? search = null) =>
            Task.FromResult(new PagedResult<VDRListItemDto> { Page = page, PageSize = pageSize });

        public Task<(List<VDRListItemDto> Items, int Total)> GetAllMatchingAsync(int? projectId, int? vendorId, string? workflowStatus, string? search, int maxRows) =>
            Task.FromResult((new List<VDRListItemDto>(), 0));

        public Task<VDRItemDto?> GetByIdAsync(int id) => Task.FromResult(ItemToReturn);

        public Task<int> CreateManualAsync(VDRItemDto item)
        {
            CreateCalled = true;
            return Task.FromResult(1);
        }

        public Task<VDRDashboardDto> GetDashboardAsync(int? projectId = null) => Task.FromResult(new VDRDashboardDto());

        // SCRUM-559
        public Task<VdrActionResult> UpdateDetailsAsync(int vdrItemId, VdrItemEditInput input, string? userName) => Task.FromResult(VdrActionResult.Ok());
        public Task<VdrActionResult> DeactivateAsync(int vdrItemId, string? reason, string? userName) => Task.FromResult(VdrActionResult.Ok());
    }

    private class ThrowingNumberingService : IVdrDocumentNumberingService
    {
        public Task<string> GenerateContractorNumberAsync(int vdrItemId) => throw new InvalidOperationException("Should not be called when unauthorized");
        public Task<string> GenerateVendorNumberAsync(int vdrItemId) => throw new InvalidOperationException("Should not be called when unauthorized");
    }

    private class ThrowingFtpUploadService : IFtpUploadService
    {
        public bool IsConfigured => false;
        public Task<string> UploadAsync(Stream fileStream, string fileName, string remoteFolder) =>
            throw new InvalidOperationException("Should not be called when unauthorized");
    }

    private class ThrowingFileStorageService : IFileStorageService
    {
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content) =>
            throw new InvalidOperationException("Should not be called when unauthorized");
        public Task<Stream> OpenReadAsync(string key) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string key) => throw new NotImplementedException();
        public Task DeleteAsync(string key) => throw new NotImplementedException();
    }

    private class ThrowingCommentService : IDocumentCommentService
    {
        public Task<List<VDRCommentDto>> GetForDocumentAsync(int documentId) => Task.FromResult(new List<VDRCommentDto>());
        public Task<int> AddCommentAsync(int documentId, string commentText, string? raisedByName, int? vdrSubmissionId = null,
            string? sheet = null, string? zone = null, string? reference = null, string? category = null, string? severity = null,
            string? requiredAction = null, DateTime? dueDate = null, int? attachmentId = null, int? pageNumber = null,
            double? pinX = null, double? pinY = null, bool marksRevisionRequired = false) => throw new NotImplementedException();
        public Task SetActionStatusAsync(int commentId, string actionStatus) => throw new NotImplementedException();
        public Task RecordResponseAsync(int commentId, string? responseText, int? replyAttachmentId = null) => throw new NotImplementedException();
    }

    private static async Task<(VDRController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx, SpyVdrWorkflowService Workflow, SpyVDRService Vdr)> CreateController(
        string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"VDRAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>() };

        var workflow = new SpyVdrWorkflowService();
        var vdrService = new SpyVDRService();

        var controller = new VDRController(
            vdrService, new ThrowingCommentService(), workflow, new FakeLetterService(), new FakeWorkflowRuntime(),
            ctx, currentUser, new ThrowingNumberingService(), new ThrowingFtpUploadService(),
            new ThrowingFileStorageService(), permissionService).WithTempData();

        return (controller, ctx, workflow, vdrService);
    }

    // ==============================================================================
    // Create — طبق سید موجود فقط EngineeringManager/DocumentController مجاز به Create هستند
    // ==============================================================================

    [Fact]
    public async Task Create_ViewerRole_Denied_NoItemCreated()
    {
        var (controller, _, _, vdr) = await CreateController(nameof(Create_ViewerRole_Denied_NoItemCreated), SystemRoles.Viewer);

        var result = await controller.Create(new VDRItemDto());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(vdr.CreateCalled);
    }

    [Fact]
    public async Task Create_DocumentController_Authorized_CreatesItem()
    {
        var (controller, _, _, vdr) = await CreateController(nameof(Create_DocumentController_Authorized_CreatesItem), SystemRoles.DocumentController);

        var result = await controller.Create(new VDRItemDto());

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(vdr.CreateCalled);
    }

    // ==============================================================================
    // SubmitForReview / DisciplineRespond — طبق سید، عملیات "Edit"
    // ==============================================================================

    [Fact]
    public async Task SubmitForReview_ViewerRole_Denied_WorkflowNotCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(SubmitForReview_ViewerRole_Denied_WorkflowNotCalled), SystemRoles.Viewer);

        var result = await controller.SubmitForReview(1, 0, "VT-1", null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(workflow.SubmitForReviewCalled);
    }

    [Fact]
    public async Task SubmitForReview_EngineeringManager_Authorized_WorkflowCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(SubmitForReview_EngineeringManager_Authorized_WorkflowCalled), SystemRoles.EngineeringManager);

        var result = await controller.SubmitForReview(1, 0, "VT-1", null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(workflow.SubmitForReviewCalled);
    }

    [Fact]
    public async Task DisciplineRespond_ViewerRole_Denied_WorkflowNotCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(DisciplineRespond_ViewerRole_Denied_WorkflowNotCalled), SystemRoles.Viewer);

        var result = await controller.DisciplineRespond(1, "Approve", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(workflow.DisciplineRespondCalled);
    }

    // ==============================================================================
    // LeadReview / ReturnToVendor — طبق سید، عملیات "Approve" (فقط EngineeringManager/DisciplineEngineer)
    // ==============================================================================

    [Fact]
    public async Task LeadReview_ViewerRole_Denied_WorkflowNotCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(LeadReview_ViewerRole_Denied_WorkflowNotCalled), SystemRoles.Viewer);

        var result = await controller.LeadReview(1, "AP", null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(workflow.LeadReviewCalled);
    }

    [Fact]
    public async Task LeadReview_DocumentController_Denied_NoApprovePermission()
    {
        // طبق سید: DocumentController فقط Create/Edit دارد، نه Approve
        var (controller, _, workflow, _) = await CreateController(nameof(LeadReview_DocumentController_Denied_NoApprovePermission), SystemRoles.DocumentController);

        var result = await controller.LeadReview(1, "AP", null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(workflow.LeadReviewCalled);
    }

    [Fact]
    public async Task LeadReview_DisciplineEngineer_Authorized_WorkflowCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(LeadReview_DisciplineEngineer_Authorized_WorkflowCalled), SystemRoles.DisciplineEngineer);

        var result = await controller.LeadReview(1, "AP", null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(workflow.LeadReviewCalled);
    }

    [Fact]
    public async Task ReturnToVendor_ViewerRole_Denied_WorkflowNotCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(ReturnToVendor_ViewerRole_Denied_WorkflowNotCalled), SystemRoles.Viewer);

        var result = await controller.ReturnToVendor(1, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(workflow.ReturnToVendorCalled);
    }

    [Fact]
    public async Task ReturnToVendor_EngineeringManager_Authorized_WorkflowCalled()
    {
        var (controller, _, workflow, _) = await CreateController(nameof(ReturnToVendor_EngineeringManager_Authorized_WorkflowCalled), SystemRoles.EngineeringManager);

        var result = await controller.ReturnToVendor(1, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(workflow.ReturnToVendorCalled);
    }

    // ==============================================================================
    // GenerateContractorNumber / UploadToFtp — طبق سید، عملیات "Edit"؛ سرویس‌های وابسته Throwing
    // هستند - اگر گیت درست کار نکند، خودِ تست با Exception می‌شکند (نه فقط Assert)
    // ==============================================================================

    [Fact]
    public async Task GenerateContractorNumber_ViewerRole_Denied_NumberingServiceNeverInvoked()
    {
        var (controller, _, _, _) = await CreateController(nameof(GenerateContractorNumber_ViewerRole_Denied_NumberingServiceNeverInvoked), SystemRoles.Viewer);

        var result = await controller.GenerateContractorNumber(1, null, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task UploadToFtp_ViewerRole_Denied_FtpServiceNeverInvoked()
    {
        var (controller, _, _, _) = await CreateController(nameof(UploadToFtp_ViewerRole_Denied_FtpServiceNeverInvoked), SystemRoles.Viewer);

        var result = await controller.UploadToFtp(1, 1, null!);

        Assert.IsType<RedirectToActionResult>(result);
    }

    // ==============================================================================
    // Index/Details — طبق تصمیم صریح کاربر: عمداً دست‌نخورده، برای همه‌ی نقش‌ها در دسترس
    // ==============================================================================

    [Fact]
    public async Task Index_ViewerRole_StillAccessible_NoRegressionFromNewGates()
    {
        var (controller, _, _, _) = await CreateController(nameof(Index_ViewerRole_StillAccessible_NoRegressionFromNewGates), SystemRoles.Viewer);

        var result = await controller.Index(null, null, null);

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Details_ViewerRole_StillAccessible_NoRegressionFromNewGates()
    {
        var (controller, _, _, vdr) = await CreateController(nameof(Details_ViewerRole_StillAccessible_NoRegressionFromNewGates), SystemRoles.Viewer);
        vdr.ItemToReturn = new VDRItemDto { VDRItemId = 1 };

        var result = await controller.Details(1);

        Assert.IsType<ViewResult>(result);
    }
}
