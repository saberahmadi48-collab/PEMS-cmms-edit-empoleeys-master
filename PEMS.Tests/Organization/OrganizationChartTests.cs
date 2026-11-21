using Microsoft.EntityFrameworkCore;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Organization;

/// <summary>Organization Chart DTO: گره = سمت؛ دارنده از EmployeeAssignment؛ خالی‌ها دیده می‌شوند؛ ساختار ساختگی ساخته نمی‌شود.</summary>
public class OrganizationChartTests
{
    private static PEMSDbContext Db() => PemsDbContextFactory.CreateInMemory("chart_" + Guid.NewGuid().ToString("N"));
    private static OrganizationHierarchyService Svc(PEMSDbContext db) => new(db, new OrganizationValidationService(db));

    private static async Task Emps(PEMSDbContext db, params int[] ids)
    {
        foreach (var id in ids) db.Employees.Add(new Employee { EmployeeId = id, FirstName = "E" + id, LastName = "T", IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Chart_BuildsPositionHierarchy_WithHolders_AndSingleRootNeedsNoContainer()
    {
        var db = Db(); await Emps(db, 1, 2, 3);
        var ps = await OrgSeed.ChainAsync(db, 1, 2, 3);                 // 1 → 2 → 3(root)
        var dto = await Svc(db).GetChartAsync();
        Assert.Equal(1, dto.RootCount);
        Assert.DoesNotContain(dto.Nodes, n => n.NodeKind == "container");
        var leaf = dto.Nodes.Single(n => n.PositionId == ps[0]); var root = dto.Nodes.Single(n => n.PositionId == ps[2]);
        Assert.Equal("p" + ps[1], leaf.ParentId); Assert.Equal("position", leaf.NodeKind); Assert.Equal("E1 T", leaf.EmployeeName);
        Assert.Equal("", root.ParentId); Assert.Equal("root", root.NodeKind); Assert.Equal(1, root.ChildCount == 1 ? 1 : 0);
        Assert.Empty(dto.Warnings);
    }

    [Fact]
    public async Task Chart_VacantPositions_StayVisible_AndAreNotAnError()
    {
        var db = Db(); await Emps(db, 1, 2);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        var a = await db.EmployeeAssignments.SingleAsync(x => x.EmployeeId == 1); a.IsActive = false; a.EndDate = DateTime.Now; await db.SaveChangesAsync();
        var dto = await Svc(db).GetChartAsync();
        var vacant = dto.Nodes.Single(n => n.PositionId == ps[0]);
        Assert.True(vacant.IsVacant); Assert.Null(vacant.EmployeeName); Assert.Empty(dto.Warnings);
    }

    [Fact]
    public async Task Chart_MultipleRoots_GetOnlyANeutralVisualContainer_NotAFakeManager()
    {
        var db = Db(); await Emps(db, 1, 2);
        await OrgSeed.ChainAsync(db, 1); await OrgSeed.ChainAsync(db, 2);
        var dto = await Svc(db).GetChartAsync();
        Assert.Equal(2, dto.RootCount);
        var container = Assert.Single(dto.Nodes, n => n.NodeKind == "container");
        Assert.Null(container.PositionId); Assert.Null(container.EmployeeName);
        Assert.All(dto.Nodes.Where(n => n.NodeKind == "root"), n => Assert.Equal("org-root", n.ParentId));
        // نمودار هیچ رکوردی در DB نمی‌سازد
        Assert.Equal(2, await db.Positions.CountAsync());
    }

    [Fact]
    public async Task Chart_InvalidHierarchy_IsShownSafely_AsOrphanOrCycle_WithWarnings()
    {
        var db = Db(); await Emps(db, 1, 2, 3, 4);
        var ps = await OrgSeed.ChainAsync(db, 1, 2);
        var inactiveParent = new Position { Title = "ip", IsActive = false }; db.Positions.Add(inactiveParent); await db.SaveChangesAsync();
        var orphan = new Position { Title = "orphan", IsActive = true, ParentPositionId = inactiveParent.PositionId }; db.Positions.Add(orphan);
        var c1 = new Position { Title = "c1", IsActive = true }; var c2 = new Position { Title = "c2", IsActive = true }; db.Positions.AddRange(c1, c2); await db.SaveChangesAsync();
        c1.ParentPositionId = c2.PositionId; c2.ParentPositionId = c1.PositionId; await db.SaveChangesAsync();

        var dto = await Svc(db).GetChartAsync();                        // نباید پرتاب کند
        Assert.Equal("orphan", dto.Nodes.Single(n => n.PositionId == orphan.PositionId).NodeKind);
        Assert.All(new[] { c1.PositionId, c2.PositionId }, id => Assert.Equal("cycle", dto.Nodes.Single(n => n.PositionId == id).NodeKind));
        Assert.Contains(dto.Warnings, w => w.Code == "ORPHAN_POSITIONS" && w.Count == 1);
        Assert.Contains(dto.Warnings, w => w.Code == "HIERARCHY_CYCLE" && w.Count == 2);
        // هر گره دقیقاً یک بار؛ والدِ هر گره یا ریشه/ظرف است یا گره‌ی موجود (تا d3.stratify خطا ندهد)
        var ids = dto.Nodes.Select(n => n.Id).ToHashSet();
        Assert.Equal(dto.Nodes.Count, ids.Count);
        Assert.All(dto.Nodes.Where(n => n.ParentId != ""), n => Assert.Contains(n.ParentId, ids));
        Assert.Single(dto.Nodes, n => n.ParentId == "");
    }

    [Fact]
    public async Task Chart_Dto_ExposesNoSecurityOrEntityFields()
    {
        var props = typeof(OrganizationChartNodeDto).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.DoesNotContain(props, p => p.Contains("password") || p.Contains("role") || p.Contains("email") || p.Contains("phone") || p.Contains("username") || p.Contains("rowversion"));
        await Task.CompletedTask;
    }

    [Fact]
    public void ChartEndpoints_AreSystemAdminOnly_AndReadOnly()
    {
        // SCRUM-639: the chart rows are a read operation for SystemAdmin only (no write operation reads or writes through it)
        var operations = PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly);
        Assert.Contains(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, operations.Get("Organization", "ChartRows")!.Roles);
        // JS هرگز چیزی نمی‌نویسد: فقط GET (fetch بدون method/body)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web/wwwroot/js/pems-org-position-chart.js"))) dir = dir.Parent;
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web/wwwroot/js/pems-org-position-chart.js"));
        Assert.DoesNotContain("method:", js); Assert.DoesNotContain("POST", js); Assert.DoesNotContain("XMLHttpRequest", js);
    }
}
