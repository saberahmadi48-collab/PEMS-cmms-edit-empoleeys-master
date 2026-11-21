using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Security;

/// <summary>
/// طبق ممیزی امنیتی (P0 - Engineering File Security Containment): تست‌های زنجیره‌ی مجوزدهی
/// FilesController.Download روی SQL Server واقعی و ایزوله (نه PEMSDB مشترک). ICurrentUserService/
/// IAccessControlService عمداً Fake شده‌اند تا هر سناریو (کاربر ناشناس، دیسیپلین اشتباه، دسترسی
/// لغوشده، ...) به‌صورت قطعی و مستقل از داده‌ی واقعی RBAC قابل بازتولید باشد - خودِ منطق واقعی
/// تصمیم‌گیری (CheckAccessAsync/CheckEngineeringDocumentAccessAsync) دست‌نخورده و واقعی اجرا
/// می‌شود، فقط ورودی‌هایش کنترل‌شده‌اند. ApplicationUser واقعی (نه شبیه‌سازی) ساخته می‌شود چون
/// FilesController مسیر واقعی AspNetUsers.EmployeeId را طی می‌کند.
/// </summary>
public class FilesControllerAccessTests : IAsyncLifetime
{
    private const string IsolatedDbName = "PEMSDB_FileSecurityTest";
    private PEMSDbContext _ctx = null!;
    private string _storageRoot = null!;
    private LocalFileStorageProviderTestable _storage = null!;
    private string _suffix = null!;

    private int _projectId;
    private int _mechDisciplineId;
    private int _elecDisciplineId;
    private int _mechUserId;
    private int _elecUserId;

    public async Task InitializeAsync()
    {
        _ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(IsolatedDbName);
        _storageRoot = Path.Combine(Path.GetTempPath(), "pems_filesecuritytest_" + Guid.NewGuid());
        _storage = new LocalFileStorageProviderTestable(_storageRoot);

        _suffix = Guid.NewGuid().ToString("N")[..8];
        var project = new Project { ProjectName = "FileSecTest " + _suffix, ProjectCode = "FST-" + _suffix };
        var mechDiscipline = new Discipline { Name = "FST Mechanical " + _suffix, Code = "FSTM-" + _suffix };
        var elecDiscipline = new Discipline { Name = "FST Electrical " + _suffix, Code = "FSTE-" + _suffix };
        _ctx.Projects.Add(project);
        _ctx.Disciplines.AddRange(mechDiscipline, elecDiscipline);
        await _ctx.SaveChangesAsync();
        _projectId = project.ProjectId;
        _mechDisciplineId = mechDiscipline.DisciplineId;
        _elecDisciplineId = elecDiscipline.DisciplineId;

        var mechDept = new Department { Code = "FSTMD-" + _suffix, Name = "FST Mech Dept " + _suffix, IsActive = true };
        var elecDept = new Department { Code = "FSTED-" + _suffix, Name = "FST Elec Dept " + _suffix, IsActive = true };
        _ctx.Departments.AddRange(mechDept, elecDept);
        await _ctx.SaveChangesAsync();

        var mechEmployee = new Employee { PersonnelCode = "FSTM-" + _suffix, FirstName = "Mech", LastName = "User", DepartmentId = mechDept.DepartmentId, DisciplineId = _mechDisciplineId, PersonnelType = "Internal" };
        var elecEmployee = new Employee { PersonnelCode = "FSTE-" + _suffix, FirstName = "Elec", LastName = "User", DepartmentId = elecDept.DepartmentId, DisciplineId = _elecDisciplineId, PersonnelType = "Internal" };
        _ctx.Employees.AddRange(mechEmployee, elecEmployee);
        await _ctx.SaveChangesAsync();

        var mechUser = new ApplicationUser { UserName = "fst-mech-" + _suffix, NormalizedUserName = ("fst-mech-" + _suffix).ToUpperInvariant(), FullName = "Mech User", EmployeeId = mechEmployee.EmployeeId, IsActive = true, CreatedDate = DateTime.Now };
        var elecUser = new ApplicationUser { UserName = "fst-elec-" + _suffix, NormalizedUserName = ("fst-elec-" + _suffix).ToUpperInvariant(), FullName = "Elec User", EmployeeId = elecEmployee.EmployeeId, IsActive = true, CreatedDate = DateTime.Now };
        _ctx.Users.AddRange(mechUser, elecUser);
        await _ctx.SaveChangesAsync();
        _mechUserId = mechUser.Id;
        _elecUserId = elecUser.Id;
    }

    public async Task DisposeAsync()
    {
        // پاک‌سازی داده‌ی این اجرا - طبق انضباط این نشست (بدون آلودگی دائمی حتی روی DB ایزوله)
        // طبق محدودیت واقعی EF Core: ExecuteDelete روی سلسله‌مراتب TPT (Document/EngineeringDocument)
        // پشتیبانی نمی‌شود - بارگذاری‌و-حذف معمولی به‌جای آن، هم‌الگو با اسکریپت پاک‌سازی قبلی این نشست
        var attachments = await _ctx.DocumentAttachments.Where(x => x.FileName.StartsWith("FST-")).ToListAsync();
        _ctx.DocumentAttachments.RemoveRange(attachments);

        var docs = await _ctx.EngineeringDocuments.Where(x => x.Description != null && x.Description.StartsWith("FST-")).ToListAsync();
        _ctx.EngineeringDocuments.RemoveRange(docs);
        await _ctx.SaveChangesAsync();

        var users = await _ctx.Users.Where(x => x.UserName!.StartsWith("fst-")).ToListAsync();
        _ctx.Users.RemoveRange(users);

        var employees = await _ctx.Employees.Where(x => x.PersonnelCode.StartsWith("FST")).ToListAsync();
        _ctx.Employees.RemoveRange(employees);
        await _ctx.SaveChangesAsync();

        var departments = await _ctx.Departments.Where(x => x.Code.StartsWith("FST")).ToListAsync();
        _ctx.Departments.RemoveRange(departments);

        var disciplines = await _ctx.Disciplines.Where(x => x.Code.StartsWith("FST")).ToListAsync();
        _ctx.Disciplines.RemoveRange(disciplines);

        var projects = await _ctx.Projects.Where(x => x.ProjectCode.StartsWith("FST-")).ToListAsync();
        _ctx.Projects.RemoveRange(projects);
        await _ctx.SaveChangesAsync();

        await _ctx.DisposeAsync();
        // پاک‌سازی پوشه‌ی موقت Best-Effort است - نباید نتیجه‌ی تست واقعی را زیر سؤال ببرد
        try { if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true); }
        catch (IOException) { }
    }

    private async Task<(int DocumentId, int AttachmentId)> SeedDocumentWithAttachmentAsync(
        int? disciplineId, bool documentActive = true, bool attachmentActive = true, string content = "test-bytes",
        string fileName = "FST-test.txt", string mimeType = "text/plain")
    {
        var doc = new EngineeringDocument
        {
            DocumentNo = $"FST-DOC-{Guid.NewGuid():N}"[..20],
            Title = "FST-" + Guid.NewGuid(),
            DocumentType = "Datasheet",
            ProjectId = _projectId,
            DisciplineId = disciplineId,
            Status = "Draft",
            IsActive = documentActive,
            CreatedDate = DateTime.Now,
            Description = "FST-test"
        };
        _ctx.EngineeringDocuments.Add(doc);
        await _ctx.SaveChangesAsync();

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var key = await _storage.SaveAsync("documents", fileName, stream);

        var attachment = new DocumentAttachment
        {
            DocumentId = doc.DocumentId,
            FileName = fileName,
            FileExtension = Path.GetExtension(fileName),
            FilePath = key,
            FileSize = content.Length,
            MimeType = mimeType,
            UploadDate = DateTime.Now,
            IsActive = attachmentActive,
            AIStatus = "NotApplicable"
        };
        _ctx.DocumentAttachments.Add(attachment);
        await _ctx.SaveChangesAsync();

        return (doc.DocumentId, attachment.AttachmentId);
    }

    private FilesController BuildController(int? userId, IReadOnlyList<string> roles, bool canViewAllDisciplines)
    {
        var currentUser = new FakeCurrentUserService(userId, roles);
        var accessControl = new FakeAccessControlService(canViewAllDisciplines);
        return new FilesController(_ctx, currentUser, accessControl, _storage)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    // ==================== SCRUM-202: served files carry nosniff + CSP; preview is inline only for safe types ====================
    [Fact]
    public async Task Download_IsAlwaysAnAttachment_WithNosniffAndSandbox()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId);
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var file = Assert.IsType<FileStreamResult>(await controller.Download(attachmentId));
        await file.FileStream.DisposeAsync();

        var headers = controller.Response.Headers;
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Contains("sandbox", headers.ContentSecurityPolicy.ToString());
        Assert.StartsWith("attachment", headers.ContentDisposition.ToString());
        Assert.Contains("FST-test.txt", headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task Preview_OfASafeType_IsInline_InASandbox()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId);
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var file = Assert.IsType<FileStreamResult>(await controller.Preview(attachmentId));
        await file.FileStream.DisposeAsync();

        Assert.Equal("text/plain", file.ContentType);
        Assert.StartsWith("inline", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("sandbox", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Contains("default-src 'none'", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task Preview_OfHtmlStoredWithAnHtmlType_IsADownloadOfPlainBytes()
    {
        // an old row whose MIME type came from the browser: it must never be rendered as a page in the PEMS origin
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId,
            content: "<script>alert(1)</script>", fileName: "FST-page.html", mimeType: "text/html");
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var file = Assert.IsType<FileStreamResult>(await controller.Preview(attachmentId));
        await file.FileStream.DisposeAsync();

        Assert.Equal("application/octet-stream", file.ContentType);
        Assert.StartsWith("attachment", controller.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task Preview_UsesTheSameAccessCheck_AsDownload()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId);
        var controller = BuildController(_elecUserId, new[] { "DisciplineEngineer" }, canViewAllDisciplines: false);

        Assert.IsType<ForbidResult>(await controller.Preview(attachmentId));
    }

    // ==================== 2. Authenticated unauthorized user (wrong discipline) → denied ====================
    [Fact]
    public async Task Download_AuthenticatedUser_DifferentDiscipline_ReturnsForbid()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId);
        var controller = BuildController(_elecUserId, new[] { "DisciplineEngineer" }, canViewAllDisciplines: false);

        var result = await controller.Download(attachmentId);

        Assert.IsType<ForbidResult>(result);
    }

    // ==================== 3. Wrong discipline/scope (second, distinct pairing) → denied ====================
    [Fact]
    public async Task Download_WrongScope_SecondDisciplinePairing_ReturnsForbid()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_elecDisciplineId);
        var controller = BuildController(_mechUserId, new[] { "DisciplineEngineer" }, canViewAllDisciplines: false);

        var result = await controller.Download(attachmentId);

        Assert.IsType<ForbidResult>(result);
    }

    // ==================== 4. Authorized user (ViewAllDisciplines) → success ====================
    [Fact]
    public async Task Download_UserWithViewAllDisciplines_ReturnsFile()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_elecDisciplineId);
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var result = await controller.Download(attachmentId);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        await fileResult.FileStream.DisposeAsync();
    }

    // ==================== 5. Revoked access (IsActive=false on attachment) → denied ====================
    [Fact]
    public async Task Download_RevokedAttachment_ReturnsNotFound()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId, attachmentActive: false);
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var result = await controller.Download(attachmentId);

        Assert.IsType<NotFoundResult>(result);
    }

    // ==================== 6. Missing attachment → 404 ====================
    [Fact]
    public async Task Download_NonExistentAttachmentId_ReturnsNotFound()
    {
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var result = await controller.Download(-99999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ==================== 7. Invalid attachment ownership (parent document inactive) → denied ====================
    [Fact]
    public async Task Download_ParentDocumentInactive_ReturnsForbid()
    {
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId, documentActive: false);
        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var result = await controller.Download(attachmentId);

        Assert.IsType<ForbidResult>(result);
    }

    // ==================== 8. Path traversal → rejected ====================
    [Fact]
    public async Task Download_MaliciousFilePathInDatabase_RejectedNotServed()
    {
        var doc = new EngineeringDocument
        {
            DocumentNo = $"FST-DOC-{Guid.NewGuid():N}"[..20],
            Title = "FST-" + Guid.NewGuid(),
            DocumentType = "Datasheet",
            ProjectId = _projectId,
            DisciplineId = null,
            Status = "Draft",
            IsActive = true,
            CreatedDate = DateTime.Now,
            Description = "FST-test"
        };
        _ctx.EngineeringDocuments.Add(doc);
        await _ctx.SaveChangesAsync();

        var attachment = new DocumentAttachment
        {
            DocumentId = doc.DocumentId,
            FileName = "FST-traversal.txt",
            FileExtension = ".txt",
            // طبق بند صریح ممیزی: تلاش برای خروج از ریشه‌ی امن با ../ - هرگز نباید سرویس شود
            FilePath = "../../../../windows/win32.ini",
            FileSize = 1,
            MimeType = "text/plain",
            UploadDate = DateTime.Now,
            IsActive = true,
            AIStatus = "NotApplicable"
        };
        _ctx.DocumentAttachments.Add(attachment);
        await _ctx.SaveChangesAsync();

        var controller = BuildController(_mechUserId, new[] { "EngineeringManager" }, canViewAllDisciplines: true);

        var result = await controller.Download(attachment.AttachmentId);

        Assert.IsType<ForbidResult>(result);
    }

    // ==================== 10. Existing valid attachment → downloadable through authorized endpoint ====================
    [Fact]
    public async Task Download_ValidAttachment_MatchingDiscipline_StreamsCorrectContent()
    {
        const string expectedContent = "FST-real-file-bytes-12345";
        var (_, attachmentId) = await SeedDocumentWithAttachmentAsync(_mechDisciplineId, content: expectedContent);
        var controller = BuildController(_mechUserId, new[] { "DisciplineEngineer" }, canViewAllDisciplines: false);

        var result = await controller.Download(attachmentId);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        using var reader = new StreamReader(fileResult.FileStream);
        var actualContent = await reader.ReadToEndAsync();
        Assert.Equal(expectedContent, actualContent);
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public FakeCurrentUserService(int? userId, IReadOnlyList<string> roles)
        {
            UserId = userId;
            Roles = roles;
        }
        public int? UserId { get; }
        public string? UserName => "test-user";
        public string? FullName => "Test User";
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles { get; }
    }

    private sealed class FakeAccessControlService : IAccessControlService
    {
        private readonly bool _canViewAllDisciplines;
        public FakeAccessControlService(bool canViewAllDisciplines) => _canViewAllDisciplines = canViewAllDisciplines;

        public Task<AccessDecision> AuthorizeForRolesAsync(IEnumerable<string> roles, string resourceName, string actionName) =>
            Task.FromResult(_canViewAllDisciplines
                ? AccessDecision.Allow("Test")
                : AccessDecision.Deny(AccessDecisionReason.DeniedByRole, "test-denied"));

        public Task<AccessDecision> AuthorizeAsync(AccessControlRequest request) => throw new NotImplementedException();
    }

    private sealed class LocalFileStorageProviderTestable : PEMS.Infrastructure.FileStorage.LocalFileStorageProvider
    {
        public LocalFileStorageProviderTestable(string rootPath) : base(rootPath) { }
    }
}
