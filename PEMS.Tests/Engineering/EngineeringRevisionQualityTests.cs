using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Repositories;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Engineering;

/// <summary>
/// طبق Phase E-05C (E-05B §9 Quality Model): تست‌های EngineeringDocumentRepository.
/// GetRevisionQualityAsync - شامل RevisionCount (منهای رویژن ۰۰)، RejectionCount، ادغام
/// First-Pass Approval (Gate اعتبارسنجی‌شده)، محدودیت دیسیپلین (همون الگوی دقیق E-03) و
/// فیلتر MinRevisionCount هم روی خودِ Quality Query هم روی GetPagedAsync (Drilldown، ADR-5).
/// SQL Server واقعی لازم است چون FirstPassApproved به همون Query تایید-شده در
/// FirstPassApprovalPrototypeTests وابسته است.
/// </summary>
public class EngineeringRevisionQualityTests
{
    private static async Task<(EngineeringDocumentRepository repo, PEMS.Persistence.Context.PEMSDbContext ctx, Project project, Discipline disc)> SetupAsync(string tag)
    {
        var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await EngineeringDocumentWorkflowSeeder.SeedAsync(services.BuildServiceProvider());

        // SCRUM-558: the random part must survive the cut (it was cut off, so every run left a repeating code in the shared database)
        var project = new Project { ProjectName = $"RQ Test {tag}", ProjectCode = $"RQ{Guid.NewGuid():N}".Substring(0, 10) };
        var discipline = new Discipline { Name = $"RQ Disc {tag}", Code = $"RQD{tag}{Guid.NewGuid():N}".Substring(0, 6) };
        ctx.Projects.Add(project);
        ctx.Disciplines.Add(discipline);
        await ctx.SaveChangesAsync();

        return (new EngineeringDocumentRepository(ctx), ctx, project, discipline);
    }

    // طبق پذیرش ممیزی G/M/F (بخش B - "Existing test-data failures") + P0 Security Containment
    // §B: همان دلیل SeedEmployeeAsync در FirstPassApprovalPrototypeTests - EmployeeId=1 و
    // DisciplineId=1 دیگر در PEMSDB مشترک تضمین‌شده نیستند؛ به‌جای آن از همون Discipline
    // خودساخته‌ی SetupAsync (disc.DisciplineId) استفاده می‌شود، نه یک شناسه‌ی ثابت فرضی.
    private static async Task<int> SeedEmployeeAsync(PEMS.Persistence.Context.PEMSDbContext ctx, string tag, int disciplineId)
    {
        // Name هم مثل Code یک ایندکس Unique دارد (IX_Departments_Name) - باید هردو واقعاً یکتا
        // باشند، نه فقط بر مبنای tag کوتاه که بین اجراهای مختلف تست تکرار می‌شود.
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var department = new Department { Code = $"RQ{tag}{uniqueSuffix}"[..10], Name = $"RQ Test Dept {tag} {uniqueSuffix}" };
        ctx.Departments.Add(department);
        await ctx.SaveChangesAsync();

        var employee = new Employee
        {
            FirstName = "RQ", LastName = $"Test{tag}",
            DepartmentId = department.DepartmentId, DisciplineId = disciplineId
        };
        ctx.Employees.Add(employee);
        await ctx.SaveChangesAsync();

        return employee.EmployeeId;
    }

    private static EngineeringDocument NewDoc(Project project, Discipline? discipline, string tag) => new()
    {
        DocumentNo = $"RQ-{tag}-{Guid.NewGuid():N}".Substring(0, 20),
        Title = $"Doc {tag}", DocumentType = "Datasheet", ProjectId = project.ProjectId,
        DisciplineId = discipline?.DisciplineId, Status = "Draft"
    };

    [Fact]
    public async Task RevisionCount_ExcludesTheAlwaysCreatedRev00()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(RevisionCount_ExcludesTheAlwaysCreatedRev00));

        var docNoExtraRevisions = NewDoc(project, disc, "A");
        var docWithOneExtra = NewDoc(project, disc, "B");
        ctx.EngineeringDocuments.AddRange(docNoExtraRevisions, docWithOneExtra);
        await ctx.SaveChangesAsync();

        // هر دو سند رویژن ۰۰ (اولیه) دارند - این خودش نباید در RevisionCount شمرده شود
        ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = docNoExtraRevisions.DocumentId, RevisionNo = "00", Status = "Draft", RevisionDate = DateTime.Now, IsActive = true });
        ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = docWithOneExtra.DocumentId, RevisionNo = "00", Status = "Superseded", RevisionDate = DateTime.Now, IsActive = true });
        ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = docWithOneExtra.DocumentId, RevisionNo = "01", Status = "Draft", RevisionDate = DateTime.Now, IsActive = true });
        await ctx.SaveChangesAsync();

        var results = await repo.GetRevisionQualityAsync(false, null, null, project.ProjectId);

        Assert.Equal(0, results.Single(d => d.DocumentId == docNoExtraRevisions.DocumentId).RevisionCount);
        Assert.Equal(1, results.Single(d => d.DocumentId == docWithOneExtra.DocumentId).RevisionCount);
    }

    [Fact]
    public async Task RejectionCount_CountsRejectDecisionsAcrossInstances()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(RejectionCount_CountsRejectDecisionsAcrossInstances));
        var runtime = new WorkflowRuntimeService(ctx);
        var workflow = new DocumentWorkflowService(ctx, runtime, new FakeLetterService(), new AccessControlService(ctx));

        var doc = NewDoc(project, disc, "C");
        ctx.EngineeringDocuments.Add(doc);
        await ctx.SaveChangesAsync();

        var employeeId = await SeedEmployeeAsync(ctx, "C", disc.DisciplineId);

        // رد اول
        await workflow.SubmitAsync(doc.DocumentId, null, employeeId, new[] { "EngineeringManager" });
        await workflow.RejectAsync(doc.DocumentId, "r1", employeeId, new[] { "EngineeringSupervisor" });
        // رد دوم
        await workflow.SubmitAsync(doc.DocumentId, null, employeeId, new[] { "EngineeringManager" });
        await workflow.RejectAsync(doc.DocumentId, "r2", employeeId, new[] { "EngineeringSupervisor" });
        // بار سوم تایید و صادر می‌شود
        await workflow.SubmitAsync(doc.DocumentId, null, employeeId, new[] { "EngineeringManager" });
        await workflow.CheckAsync(doc.DocumentId, null, employeeId, new[] { "EngineeringSupervisor" });
        await workflow.ApproveAsync(doc.DocumentId, null, employeeId, new[] { "EngineeringManager" });
        await workflow.IssueAsync(doc.DocumentId, null, employeeId, new[] { "DocumentController" }, DocumentWorkflowService.DeliveryUnitInternalDcc);

        var results = await repo.GetRevisionQualityAsync(false, null, null, project.ProjectId);
        var row = results.Single(d => d.DocumentId == doc.DocumentId);

        Assert.Equal(2, row.RejectionCount);
        Assert.False(row.FirstPassApproved); // زودترین Instance Rejected بود
    }

    [Fact]
    public async Task FirstPassApproved_IsNull_WhenDocumentNeverSubmitted()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(FirstPassApproved_IsNull_WhenDocumentNeverSubmitted));
        var doc = NewDoc(project, disc, "D");
        ctx.EngineeringDocuments.Add(doc);
        await ctx.SaveChangesAsync();

        var results = await repo.GetRevisionQualityAsync(false, null, null, project.ProjectId);

        Assert.Null(results.Single(d => d.DocumentId == doc.DocumentId).FirstPassApproved);
    }

    [Fact]
    public async Task DisciplineRestriction_EmployeeWithNoDiscipline_SeesOnlyNullDisciplineDocs()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(DisciplineRestriction_EmployeeWithNoDiscipline_SeesOnlyNullDisciplineDocs));
        var withDiscipline = NewDoc(project, disc, "E1");
        var withoutDiscipline = NewDoc(project, null, "E2");
        ctx.EngineeringDocuments.AddRange(withDiscipline, withoutDiscipline);
        await ctx.SaveChangesAsync();

        var results = await repo.GetRevisionQualityAsync(restrictToDiscipline: true, disciplineId: null, minRevisionCount: null, projectId: project.ProjectId);

        Assert.Single(results);
        Assert.Equal(withoutDiscipline.DocumentId, results[0].DocumentId);
    }

    [Fact]
    public async Task DisciplineRestriction_EmployeeWithDiscipline_SeesOwnAndNullOnly()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(DisciplineRestriction_EmployeeWithDiscipline_SeesOwnAndNullOnly));
        var otherDisc = new Discipline { Name = "Other", Code = $"OTH{Guid.NewGuid():N}".Substring(0, 6) };
        ctx.Disciplines.Add(otherDisc);
        await ctx.SaveChangesAsync();

        var mine = NewDoc(project, disc, "F1");
        var other = NewDoc(project, otherDisc, "F2");
        var unassigned = NewDoc(project, null, "F3");
        ctx.EngineeringDocuments.AddRange(mine, other, unassigned);
        await ctx.SaveChangesAsync();

        var results = await repo.GetRevisionQualityAsync(true, disc.DisciplineId, null, project.ProjectId);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, d => d.DocumentId == other.DocumentId);
    }

    [Fact]
    public async Task MinRevisionCount_FiltersOutDocumentsBelowThreshold()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(MinRevisionCount_FiltersOutDocumentsBelowThreshold));
        var lowRevision = NewDoc(project, disc, "G1");
        var highRevision = NewDoc(project, disc, "G2");
        ctx.EngineeringDocuments.AddRange(lowRevision, highRevision);
        await ctx.SaveChangesAsync();

        ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = lowRevision.DocumentId, RevisionNo = "00", RevisionDate = DateTime.Now, IsActive = true });
        for (var i = 0; i <= 3; i++)
            ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = highRevision.DocumentId, RevisionNo = i.ToString("00"), RevisionDate = DateTime.Now, IsActive = true });
        await ctx.SaveChangesAsync();

        var results = await repo.GetRevisionQualityAsync(false, null, minRevisionCount: 2, projectId: project.ProjectId);

        Assert.Single(results);
        Assert.Equal(highRevision.DocumentId, results[0].DocumentId);
    }

    [Fact]
    public async Task GetPagedAsync_MinRevisionCountFilter_DrilldownWiring_Works()
    {
        var (repo, ctx, project, disc) = await SetupAsync(nameof(GetPagedAsync_MinRevisionCountFilter_DrilldownWiring_Works));
        var lowRevision = NewDoc(project, disc, "H1");
        var highRevision = NewDoc(project, disc, "H2");
        ctx.EngineeringDocuments.AddRange(lowRevision, highRevision);
        await ctx.SaveChangesAsync();

        ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = lowRevision.DocumentId, RevisionNo = "00", RevisionDate = DateTime.Now, IsActive = true });
        for (var i = 0; i <= 2; i++)
            ctx.DocumentRevisions.Add(new DocumentRevision { DocumentId = highRevision.DocumentId, RevisionNo = i.ToString("00"), RevisionDate = DateTime.Now, IsActive = true });
        await ctx.SaveChangesAsync();

        var filter = new PEMS.Application.DTOs.EngineeringDocumentListFilter { MinRevisionCount = 1, ProjectId = project.ProjectId };
        var paged = await repo.GetPagedAsync(filter, false, null);

        Assert.Single(paged.Items);
        Assert.Equal(highRevision.DocumentId, paged.Items[0].DocumentId);
    }
}
