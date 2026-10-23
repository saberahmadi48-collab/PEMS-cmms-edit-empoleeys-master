namespace PEMS.Application.DTOs;

/// <summary>بیننده‌ی نامه: کاربر، کارمند مرتبط (برای تشخیص گیرنده‌بودن) و اینکه «همه‌چیز را ببیند» (SystemAdmin یا Approve) یا نه.</summary>
public sealed record LetterViewer(int? UserId, int? EmployeeId, bool CanSeeAll, string? UserName = null);

public enum LetterAccessOutcome { Ok, NotFound, Forbidden }

public class LetterDetailAccessResult
{
    public LetterAccessOutcome Outcome { get; set; }
    public LetterDetailDto? Detail { get; set; }
}

public class LetterApprovalStepDto
{
    public int StepOrder { get; set; }
    public int ApproverUserId { get; set; }
    public string ApproverName { get; set; } = "";
    // Pending | Approved | Rejected | Returned
    public string Status { get; set; } = "Pending";
    public DateTime? DecidedDate { get; set; }
    public string? Comment { get; set; }
    // مرحله‌ی جاری = اولین Pending وقتی نامه PendingApproval است
    public bool IsCurrent { get; set; }
}
