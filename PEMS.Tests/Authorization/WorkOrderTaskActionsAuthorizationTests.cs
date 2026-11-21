using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Tests.TestSupport;
using PEMS.Web.Services;

namespace PEMS.Tests.Authorization;

/// <summary>
/// SCRUM-764: the task check-off is an operation (WorkOrderTaskOperations); the permission (WorkOrder.Edit) and the section guard are the action definition's (see WorkOrderSectionGuardTests).
/// Here: both ids must belong together, a task of another work order is never touched (audit P1-01).
/// </summary>
public class WorkOrderTaskActionsAuthorizationTests
{
    private static WorkOrderTaskOperations Ops(PEMSDbContext ctx) => new(ctx, null!, null!, null!);

    private static PEMSDbContext Db(string name) => PemsDbContextFactory.CreateInMemory($"WOTaskAuth_{name}");

    [Fact]
    public async Task CompleteTask_MatchingWorkOrderId_Completes()
    {
        var ctx = Db(nameof(CompleteTask_MatchingWorkOrderId_Completes));
        ctx.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 100, TaskNumber = 1, Title = "T", IsCompleted = false });
        await ctx.SaveChangesAsync();

        var result = await Ops(ctx).CompleteTaskAsync(500, 100, "tester");

        Assert.True(result.Success);
        var task = ctx.WorkOrderTasks.Single();
        Assert.True(task.IsCompleted);
        Assert.Equal("tester", task.CompletedBy);
    }

    [Fact]
    public async Task ReopenTask_MatchingWorkOrderId_Reopens()
    {
        var ctx = Db(nameof(ReopenTask_MatchingWorkOrderId_Reopens));
        ctx.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 100, TaskNumber = 1, Title = "T", IsCompleted = true, CompletedDate = DateTime.Now, CompletedBy = "someone" });
        await ctx.SaveChangesAsync();

        var result = await Ops(ctx).ReopenTaskAsync(500, 100);

        Assert.True(result.Success);
        var task = ctx.WorkOrderTasks.Single();
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedBy);
        Assert.Null(task.CompletedDate);
    }

    [Fact]
    public async Task CompleteTask_MismatchedWorkOrderId_Fails_DoesNotModifyAnyTask()
    {
        var ctx = Db(nameof(CompleteTask_MismatchedWorkOrderId_Fails_DoesNotModifyAnyTask));
        ctx.WorkOrderTasks.AddRange(
            new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "Target", IsCompleted = false },
            new WorkOrderTask { WorkOrderTaskId = 501, WorkOrderId = 100, TaskNumber = 1, Title = "Unrelated in claimed WorkOrder", IsCompleted = false },
            new WorkOrderTask { WorkOrderTaskId = 502, WorkOrderId = 300, TaskNumber = 1, Title = "Unrelated elsewhere", IsCompleted = false });
        await ctx.SaveChangesAsync();

        // the request claims task 500 belongs to work order 100, but it belongs to 200
        var result = await Ops(ctx).CompleteTaskAsync(500, 100, "tester");

        Assert.False(result.Success);
        Assert.All(ctx.WorkOrderTasks, t => Assert.False(t.IsCompleted));
    }

    [Fact]
    public async Task ReopenTask_MismatchedWorkOrderId_Fails_DoesNotModifyTask()
    {
        var ctx = Db(nameof(ReopenTask_MismatchedWorkOrderId_Fails_DoesNotModifyTask));
        ctx.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "T", IsCompleted = true, CompletedDate = DateTime.Now, CompletedBy = "someone" });
        await ctx.SaveChangesAsync();

        var result = await Ops(ctx).ReopenTaskAsync(500, 100);

        Assert.False(result.Success);
        var unchanged = ctx.WorkOrderTasks.Single();
        Assert.True(unchanged.IsCompleted);
        Assert.Equal("someone", unchanged.CompletedBy);
    }
}
