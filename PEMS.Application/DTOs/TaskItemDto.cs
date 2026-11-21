namespace PEMS.Application.DTOs;

public class TaskItemDto
{
    public int TaskItemId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AssignedToEmployeeName { get; set; }
    public string? AssignedByName { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = "Open";
    public string? RelatedType { get; set; }
    public int? RelatedId { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? CompletionNote { get; set; }
    public bool IsOverdue { get; set; }
    // Task Manager: وظیفه‌ی فضا (WorkSpaceId != null) وضعیتش را از طرح وضعیت فضا می‌گیرد و فقط از صفحه‌ی جزئیات تغییر می‌کند
    public int? WorkSpaceId { get; set; }
    public string? ItemKey { get; set; }
    public string? StatusName { get; set; }
    public string? StatusNameFa { get; set; }
}
