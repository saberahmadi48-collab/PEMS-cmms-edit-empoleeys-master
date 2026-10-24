using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>D20: تعریف واحد شاخص‌های مدارک: «Rejected» دیگر شامل «بازگشته با کامنت (CO/AC)» نیست؛ Pending فقط Submitted/Checked؛ قرارداد فعال = Effective یا Active میراثی.</summary>
public class DashboardDefinitionsTests
{
    [Fact]
    public async Task Rejected_returned_with_comments_and_pending_are_disjoint_and_defined_once()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory("dash-" + Guid.NewGuid());
        EngineeringDocument Doc(string no, string status, string? comment) => new() { DocumentNo = no, Title = no, Status = status, CommentStatus = comment };
        ctx.EngineeringDocuments.AddRange(
            Doc("D1", "Submitted", null), Doc("D2", "Checked", null),                    // pending ×2
            Doc("D3", "Rejected", "RE"), Doc("D4", "Rejected", null), Doc("D5", "Rejected", ""),   // truly rejected ×3
            Doc("D6", "Rejected", "CO"), Doc("D7", "Rejected", "AC"), Doc("D8", "Rejected", "AC"), // returned with comments ×3
            Doc("D9", "Approved", "AP"), Doc("D10", "Draft", null));
        ctx.Contracts.AddRange(new Contract { ContractNo = "C-A", Status = "Effective" }, new Contract { ContractNo = "C-B", Status = "Active" }, new Contract { ContractNo = "C-C", Status = "Signed" }, new Contract { ContractNo = "C-D", Status = "Draft" });
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await ctx.EngineeringDocuments.CountAsync(DashboardDefinitions.PendingApproval));
        Assert.Equal(3, await ctx.EngineeringDocuments.CountAsync(DashboardDefinitions.Rejected));
        Assert.Equal(3, await ctx.EngineeringDocuments.CountAsync(DashboardDefinitions.ReturnedWithComments));
        Assert.Equal(6, await ctx.EngineeringDocuments.CountAsync(d => d.Status == "Rejected"));        // مجموع = همان عدد قدیمی داشبورد
        Assert.Equal(2, await ctx.Contracts.CountAsync(DashboardDefinitions.ActiveContract));
    }

    [Fact]
    public void Discipline_progress_percent_is_zero_safe()
    {
        Assert.Equal(0, new DisciplinePerformanceRow("X", 0, 0, 0, 0).ApprovedPercent);
        Assert.Equal(33, new DisciplinePerformanceRow("X", 3, 1, 2, 0).ApprovedPercent);
    }
}
