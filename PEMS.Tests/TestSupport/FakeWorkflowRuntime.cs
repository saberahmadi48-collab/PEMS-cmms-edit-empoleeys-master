using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// جایگزین سبک IWorkflowRuntime برای تست‌ها. دو دسته استفاده را پوشش می‌دهد: (۱) تست‌های Grid
/// که فقط GetPendingTasksAsync لازم دارند (مثلاً PermitController.Index با
/// pendingMyApproval=true) - از طریق PendingPermitIds/ExplicitAssignedDates. (۲) تست‌های
/// Controller که واقعاً یک گردش‌کار را Start/Execute می‌کنند (IrnReceiptController، مثل
/// QCController) - با یک نمونه‌ی در-حافظه از وضعیت هر Instance، تا GetInstanceAsync بعد از
/// StartInstanceAsync واقعاً چیزی برگرداند.
/// </summary>
public class FakeWorkflowRuntime : IWorkflowRuntime
{
    public HashSet<int> PendingPermitIds { get; set; } = new();

    /// <summary>
    /// وقتی ست شود، به‌جای AssignedDate خودکار (بر اساس ترتیب Enumerate شدن HashSet - که
    /// تضمین‌شده نیست)، برای هر PermitId دقیقاً همین تاریخ استفاده می‌شود - برای تست‌های
    /// مرتب‌سازی قطعی (چند Permit با AssignedDate یکسان) لازم است.
    /// </summary>
    public Dictionary<int, DateTime>? ExplicitAssignedDates { get; set; }

    public bool ThrowOnExecute { get; set; }

    /// <summary>P0 / F-07: شبیه‌سازی شکست شروع گردش‌کار (برای اثبات جبران Disposition).</summary>
    public bool ThrowOnStart { get; set; }
    public string? LastExecutedActionType { get; private set; }

    private int _nextInstanceId = 1;
    private readonly Dictionary<(string BusinessObjectType, int BusinessObjectId), WorkflowInstanceDto> _instances = new();

    public Task<int> StartInstanceAsync(string module, string businessObjectType, int businessObjectId, string workflowCategory,
        int? projectId, int? initiatedByEmployeeId, IReadOnlyList<string>? firstStageParticipantRoles = null)
    {
        if (ThrowOnStart) throw new InvalidOperationException("Workflow engine unavailable.");

        var instance = new WorkflowInstanceDto
        {
            WorkflowInstanceId = _nextInstanceId++,
            Module = module,
            BusinessObjectType = businessObjectType,
            BusinessObjectId = businessObjectId,
            Status = "Running",
            StartDate = DateTime.Now
        };
        _instances[(businessObjectType, businessObjectId)] = instance;
        return Task.FromResult(instance.WorkflowInstanceId);
    }

    public Task ExecuteActionAsync(int workflowInstanceId, string actionType, int? actorEmployeeId,
        IReadOnlyList<string> actorRoles, string? comment, string? ipAddress, string? userAgent,
        IReadOnlyList<string>? nextStageParticipantRoles = null)
    {
        if (ThrowOnExecute) throw new UnauthorizedAccessException("Actor role not permitted for this stage.");

        LastExecutedActionType = actionType;
        var entry = _instances.Values.FirstOrDefault(x => x.WorkflowInstanceId == workflowInstanceId);
        if (entry != null)
        {
            entry.Status = actionType == "Approve" ? "Completed" : "Rejected";
            entry.CompletionDate = DateTime.Now;
        }
        return Task.CompletedTask;
    }

    public Task<WorkflowInstanceDto?> GetInstanceAsync(string businessObjectType, int businessObjectId)
        => Task.FromResult(_instances.TryGetValue((businessObjectType, businessObjectId), out var i) ? i : null);

    public Task<WorkflowInstanceDto?> GetInstanceByIdAsync(int workflowInstanceId)
        => Task.FromResult(_instances.Values.FirstOrDefault(x => x.WorkflowInstanceId == workflowInstanceId));

    public Task<IReadOnlyList<WorkflowHistoryDto>> GetHistoryAsync(int workflowInstanceId)
        => Task.FromResult<IReadOnlyList<WorkflowHistoryDto>>(new List<WorkflowHistoryDto>());

    public Task<IReadOnlyList<PendingTaskDto>> GetPendingTasksAsync(int employeeId, IReadOnlyList<string> actorRoles)
    {
        var tasks = PendingPermitIds.Select((permitId, idx) => new PendingTaskDto
        {
            WorkflowInstanceId = idx + 1,
            WorkflowInstanceStageId = idx + 1,
            Module = "Permit",
            BusinessObjectType = "Permit",
            BusinessObjectId = permitId,
            StageName = "Approval",
            AssignedDate = ExplicitAssignedDates != null && ExplicitAssignedDates.TryGetValue(permitId, out var explicitDate)
                ? explicitDate
                : DateTime.Today.AddMinutes(-idx)
        }).ToList();

        return Task.FromResult<IReadOnlyList<PendingTaskDto>>(tasks);
    }
}
