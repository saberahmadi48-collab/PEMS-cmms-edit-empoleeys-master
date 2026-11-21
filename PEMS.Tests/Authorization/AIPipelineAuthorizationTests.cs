using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS — P0 AI Pipeline Endpoint Hardening»: AIExtractionController/TableExtractionController/
/// RelationshipController/EngineeringDocumentIntelligenceController/OCRProcessingController هیچ‌کدام
/// بررسی مجوزی نداشتند - Approve/ConfirmRow/BulkConfirm/ConfirmSuggestedLink/ApproveClassification/
/// AdvanceStage/Retry (که پیشنهاد AI را به داده‌ی معتبر تبدیل می‌کنند یا Job پس‌زمینه صف می‌کنند)
/// برای هر کاربر احرازهویت‌شده‌ای باز بودند. همون الگوی ImportPipelineAuthorizationTests.cs.
/// </summary>
public class AIPipelineAuthorizationTests
{
    private static async Task<(PEMSDbContext Ctx, IPermissionService Permissions, ICurrentUserService CurrentUser)>
        CreateInfra(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"AIPipelineAuth_{dbName}");
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
        return (ctx, permissionService, currentUser);
    }

    private class FakeDocumentAIIngestionService : IDocumentAIIngestionService
    {
        public bool ApproveTagCalled;
        public bool RejectTagCalled;
        public Task ProcessAttachmentAsync(int attachmentId) => Task.CompletedTask;
        public Task ApproveExtractedTagAsync(int extractedTagId, int equipmentId, int? reviewedById = null) { ApproveTagCalled = true; return Task.CompletedTask; }
        public Task RejectExtractedTagAsync(int extractedTagId, string? comment = null, int? reviewedById = null) { RejectTagCalled = true; return Task.CompletedTask; }
        public Task ApproveExtractedSpecificationAsync(int extractedSpecificationId, int? reviewedById = null) => Task.CompletedTask;
        public Task RejectExtractedSpecificationAsync(int extractedSpecificationId, string? comment = null, int? reviewedById = null) => Task.CompletedTask;
    }

    private class FakeTableExtractionImportService : ITableExtractionImportService
    {
        public bool ConfirmRowCalled;
        public bool BulkConfirmCalled;
        public Task<int> StageAsync(int attachmentId, string extractionMethod, List<TableExtractionRowInput> rows, int? createdById) => Task.FromResult(1);
        public Task ConfirmRowAsync(int rowId, int equipmentId, int? reviewedById) { ConfirmRowCalled = true; return Task.CompletedTask; }
        public Task RejectRowAsync(int rowId, int? reviewedById, string? comment) => Task.CompletedTask;
        public Task<(int Confirmed, List<string> Errors)> BulkConfirmMatchedAsync(List<int> rowIds, int? reviewedById)
        { BulkConfirmCalled = true; return Task.FromResult<(int, List<string>)>((rowIds.Count, new List<string>())); }
    }

    private class FakeRelationshipService : IRelationshipService
    {
        public bool ConfirmSuggestedLinkCalled;
        public bool BulkConfirmCalled;
        public Task<RelationshipExplorationDto?> ExploreFromEquipmentAsync(int equipmentId) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task<RelationshipExplorationDto?> ExploreFromDocumentAsync(int documentId) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task<RelationshipExplorationDto?> ExploreFromVendorAsync(int vendorId) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task<RelationshipExplorationDto?> ExploreFromProcurementPackageAsync(int procurementPackageId) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task<RelationshipExplorationDto?> ExploreFromContractAsync(int contractId) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task<RelationshipExplorationDto?> ExploreByFreeTextAsync(string text) => Task.FromResult<RelationshipExplorationDto?>(null);
        public Task AddAIProposedRelationshipAsync(string sourceType, int sourceId, string targetType, int targetId, string relationshipType, double confidence) => Task.CompletedTask;
        public Task ConfirmSuggestedLinkAsync(int equipmentId, string targetType, int targetId, bool force = false) { ConfirmSuggestedLinkCalled = true; return Task.CompletedTask; }
        public Task<RelationshipGapReportDto> GetGapReportAsync(int? projectId = null) => Task.FromResult(new RelationshipGapReportDto());
        public Task<List<BulkSuggestedLinkItemDto>> GetAllSuggestedLinksAsync(int? projectId = null) => Task.FromResult(new List<BulkSuggestedLinkItemDto>());
        public Task<(int Confirmed, List<string> Errors)> BulkConfirmAsync(IReadOnlyList<(int EquipmentId, string TargetType, int TargetId)> items)
        { BulkConfirmCalled = true; return Task.FromResult<(int, List<string>)>((items.Count, new List<string>())); }
    }

    private class FakeEngineeringDocumentIntelligenceService : IEngineeringDocumentIntelligenceService
    {
        public bool ApproveClassificationCalled;
        public bool ApproveTagLinkCalled;
        public bool AdvanceStageCalled;
        public Task<DocumentIntelligenceDashboardDto> GetDashboardAsync() => Task.FromResult(new DocumentIntelligenceDashboardDto());
        public Task<List<DocumentIntelligenceDetailDto>> GetPendingReviewAsync() => Task.FromResult(new List<DocumentIntelligenceDetailDto>());
        public Task<DocumentIntelligenceDetailDto?> GetDetailAsync(int documentId) => Task.FromResult<DocumentIntelligenceDetailDto?>(null);
        public Task ApproveClassificationAsync(int classificationId, bool applyDiscipline, bool applyVendor, bool applyMR, bool applyRevision, bool applyIssuePurpose, int? reviewedById = null)
        { ApproveClassificationCalled = true; return Task.CompletedTask; }
        public Task RejectClassificationAsync(int classificationId, int? reviewedById = null) => Task.CompletedTask;
        public Task ApproveTagLinkAsync(int tagLinkId, int? reviewedById = null) { ApproveTagLinkCalled = true; return Task.CompletedTask; }
        public Task RejectTagLinkAsync(int tagLinkId, int? reviewedById = null) => Task.CompletedTask;
        public Task AdvanceStageAsync(int documentId, string newStage) { AdvanceStageCalled = true; return Task.CompletedTask; }
        public Task<int> AddCommentAsync(int documentId, string commentText, string? raisedByName) => Task.FromResult(1);
        public Task SetCommentActionStatusAsync(int commentId, string actionStatus) => Task.CompletedTask;
        public Task RecordCommentResponseAsync(int commentId, string responseText) => Task.CompletedTask;
    }

    // ================= TableExtraction =================

    [Fact]
    public async Task TableExtraction_ConfirmRow_WithoutPermission_ReturnsForbid_DoesNotConfirm()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(TableExtraction_ConfirmRow_WithoutPermission_ReturnsForbid_DoesNotConfirm), role: SystemRoles.Viewer);
        var service = new FakeTableExtractionImportService();
        var controller = new TableExtractionController(ctx, null!, service, user, perms).WithTempData();

        var result = await controller.ConfirmRow(1, 1, 1);

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.ConfirmRowCalled);
    }

    [Fact]
    public async Task TableExtraction_ConfirmRow_WithPermission_Confirms()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(TableExtraction_ConfirmRow_WithPermission_Confirms), role: SystemRoles.DisciplineEngineer);
        var service = new FakeTableExtractionImportService();
        var controller = new TableExtractionController(ctx, null!, service, user, perms).WithTempData();

        var result = await controller.ConfirmRow(1, 1, 1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(service.ConfirmRowCalled);
    }

    [Fact]
    public async Task TableExtraction_BulkConfirm_WithoutPermission_ReturnsForbid_DoesNotConfirm()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(TableExtraction_BulkConfirm_WithoutPermission_ReturnsForbid_DoesNotConfirm), role: SystemRoles.Viewer);
        var service = new FakeTableExtractionImportService();
        var controller = new TableExtractionController(ctx, null!, service, user, perms).WithTempData();

        var result = await controller.BulkConfirm(1, new List<int> { 10, 11 });

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.BulkConfirmCalled);
    }

    [Fact]
    public async Task TableExtraction_ConfirmRowManual_WithoutPermission_ReturnsForbid_DoesNotLookUpOrConfirm()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(TableExtraction_ConfirmRowManual_WithoutPermission_ReturnsForbid_DoesNotLookUpOrConfirm), role: SystemRoles.Viewer);
        var service = new FakeTableExtractionImportService();
        var controller = new TableExtractionController(ctx, null!, service, user, perms).WithTempData();

        var result = await controller.ConfirmRowManual(1, 1, "EQ-999");

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.ConfirmRowCalled);
    }

    [Fact]
    public async Task TableExtraction_Index_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(TableExtraction_Index_WithoutViewPermission_ReturnsForbid), role: null);
        var controller = new TableExtractionController(ctx, null!, new FakeTableExtractionImportService(), user, perms).WithTempData();

        var result = await controller.Index();

        Assert.IsType<ForbidResult>(result);
    }

    // ================= Relationship =================

    [Fact]
    public async Task Relationship_ConfirmSuggestedLink_WithoutPermission_ReturnsForbid_DoesNotConfirm()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Relationship_ConfirmSuggestedLink_WithoutPermission_ReturnsForbid_DoesNotConfirm), role: SystemRoles.Viewer);
        var service = new FakeRelationshipService();
        var controller = new RelationshipController(service, user, perms).WithTempData();

        var result = await controller.ConfirmSuggestedLink(1, "MR", 1);

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.ConfirmSuggestedLinkCalled);
    }

    [Fact]
    public async Task Relationship_ConfirmSuggestedLink_WithPermission_Confirms()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Relationship_ConfirmSuggestedLink_WithPermission_Confirms), role: SystemRoles.EngineeringManager);
        var service = new FakeRelationshipService();
        var controller = new RelationshipController(service, user, perms).WithTempData();

        var result = await controller.ConfirmSuggestedLink(1, "MR", 1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(service.ConfirmSuggestedLinkCalled);
    }

    [Fact]
    public async Task Relationship_BulkConfirm_WithoutPermission_ReturnsForbid_DoesNotConfirm()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Relationship_BulkConfirm_WithoutPermission_ReturnsForbid_DoesNotConfirm), role: SystemRoles.Viewer);
        var service = new FakeRelationshipService();
        var controller = new RelationshipController(service, user, perms).WithTempData();

        var result = await controller.BulkConfirm(new List<string> { "1|MR|1" });

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.BulkConfirmCalled);
    }

    [Fact]
    public async Task Relationship_Index_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Relationship_Index_WithoutViewPermission_ReturnsForbid), role: null);
        var controller = new RelationshipController(new FakeRelationshipService(), user, perms).WithTempData();

        var result = await controller.Index(null);

        Assert.IsType<ForbidResult>(result);
    }

    // ================= EngineeringDocumentIntelligence =================

    [Fact]
    public async Task EDI_ApproveClassification_WithoutPermission_ReturnsForbid_DoesNotApprove()
    {
        // DisciplineEngineer has Create/Edit but NOT Approve on "EngineeringDocuments" - همون
        // تفکیک وظایفی که برای خودِ EngineeringDocuments از قبل جاافتاده.
        var (ctx, perms, user) = await CreateInfra(nameof(EDI_ApproveClassification_WithoutPermission_ReturnsForbid_DoesNotApprove), role: SystemRoles.DisciplineEngineer);
        var service = new FakeEngineeringDocumentIntelligenceService();
        var controller = new EngineeringDocumentIntelligenceController(service, user, perms).WithTempData();

        var result = await controller.ApproveClassification(1, 1, true, true, true, true, true);

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.ApproveClassificationCalled);
    }

    [Fact]
    public async Task EDI_ApproveClassification_WithPermission_Approves()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(EDI_ApproveClassification_WithPermission_Approves), role: SystemRoles.EngineeringManager);
        var service = new FakeEngineeringDocumentIntelligenceService();
        var controller = new EngineeringDocumentIntelligenceController(service, user, perms).WithTempData();

        var result = await controller.ApproveClassification(1, 1, true, true, true, true, true);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(service.ApproveClassificationCalled);
    }

    [Fact]
    public async Task EDI_ApproveTagLink_WithoutPermission_ReturnsForbid_DoesNotApprove()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(EDI_ApproveTagLink_WithoutPermission_ReturnsForbid_DoesNotApprove), role: SystemRoles.DocumentController);
        var service = new FakeEngineeringDocumentIntelligenceService();
        var controller = new EngineeringDocumentIntelligenceController(service, user, perms).WithTempData();

        var result = await controller.ApproveTagLink(1, 1);

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.ApproveTagLinkCalled);
    }

    [Fact]
    public async Task EDI_AdvanceStage_WithoutPermission_ReturnsForbid_DoesNotAdvance()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(EDI_AdvanceStage_WithoutPermission_ReturnsForbid_DoesNotAdvance), role: SystemRoles.Viewer);
        var service = new FakeEngineeringDocumentIntelligenceService();
        var controller = new EngineeringDocumentIntelligenceController(service, user, perms).WithTempData();

        var result = await controller.AdvanceStage(1, "Revision");

        Assert.IsType<ForbidResult>(result);
        Assert.False(service.AdvanceStageCalled);
    }

    [Fact]
    public async Task EDI_Index_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(EDI_Index_WithoutViewPermission_ReturnsForbid), role: null);
        var controller = new EngineeringDocumentIntelligenceController(new FakeEngineeringDocumentIntelligenceService(), user, perms).WithTempData();

        var result = await controller.Index();

        Assert.IsType<ForbidResult>(result);
    }

    // ================= OCRProcessing =================
    // طبق ممیزی: OCRProcessingController.Retry مستقیم Hangfire.BackgroundJob.Enqueue (استاتیک، به
    // JobStorage.Current وابسته) را صدا می‌زند - در تست واحد بدون Storage واقعی قابل‌فراخوانی
    // نیست. مسیر «رد بدون مجوز» کاملاً امن تست می‌شود (Forbid قبل از رسیدن به Enqueue برمی‌گردد،
    // پس هیچ Job ای صف نمی‌شود). مسیر «مجاز» بدون واداشتن Hangfire به اجرا، مستقیماً از طریق خودِ
    // IPermissionService تایید می‌شود - اثبات می‌کند نقش‌های مجاز واقعاً از گیت رد می‌شوند.

    [Fact]
    public async Task OCR_Retry_WithoutPermission_ReturnsForbid_DoesNotEnqueueJob()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(OCR_Retry_WithoutPermission_ReturnsForbid_DoesNotEnqueueJob), role: SystemRoles.Viewer);
        var controller = new OCRProcessingController(ctx, null!, null!, user, perms).WithTempData();

        var result = await controller.Retry(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task OCR_Index_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(OCR_Index_WithoutViewPermission_ReturnsForbid), role: SystemRoles.ContractManager);
        var controller = new OCRProcessingController(ctx, null!, null!, user, perms).WithTempData();

        var result = await controller.Index(null);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData(SystemRoles.EngineeringManager)]
    [InlineData(SystemRoles.DocumentController)]
    public async Task OCR_Retry_AuthorizedRoles_WouldPassTheGate(string role)
    {
        var (_, perms, _) = await CreateInfra(nameof(OCR_Retry_AuthorizedRoles_WouldPassTheGate) + role, role);

        var allowed = await perms.HasPermissionAsync(new List<string> { role }, "OCRProcessing", "Create");

        Assert.True(allowed);
    }
}
