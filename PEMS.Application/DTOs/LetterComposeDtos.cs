namespace PEMS.Application.DTOs;

/// <summary>ورودی فرم نوشتن نامه‌ی رسمی (Internal/Outgoing). LetterId=null یعنی نامه‌ی جدید؛ در غیر این‌صورت ویرایش پیش‌نویس.</summary>
public class LetterComposeInput
{
    public int? LetterId { get; set; }
    public byte[]? RowVersion { get; set; }

    // پاسخ به این نامه (پیش‌نویس جدید یا ویرایش پاسخ)؛ سطح محرمانگی پاسخ هرگز کمتر از نامه‌ی اصلی نمی‌شود
    public int? ParentLetterId { get; set; }

    // Internal | Outgoing
    public string LetterType { get; set; } = "Internal";
    public string? Category { get; set; }
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public string Priority { get; set; } = "Normal";
    public string ConfidentialityLevel { get; set; } = "Internal";
    public DateTime LetterDate { get; set; } = DateTime.Today;
    public string? ReferenceNumber { get; set; }

    public int? ProjectId { get; set; }
    public int? DisciplineId { get; set; }
    public int? RelatedDepartmentId { get; set; }
    public int? SenderDepartmentId { get; set; }

    public List<int> ToEmployeeIds { get; set; } = new();
    public List<int> CcEmployeeIds { get; set; } = new();
    public List<int> BccEmployeeIds { get; set; } = new();
    // فقط Outgoing (گیرنده‌ی بیرونی سازمان)
    public string? ExternalReceiver { get; set; }
    // طرف مکاتبه‌ی ثبت‌شده‌ی گیرنده (اختیاری): نامه‌ی صادره را در «رشته‌ی مکاتبه»ی همان طرف در DCC می‌آورد؛ بدون آن فقط تطبیق نام انجام می‌شود
    public int? CorrespondentPartyId { get; set; }

    public bool ResponseRequired { get; set; }
    public DateTime? ResponseDueDate { get; set; }

    public bool ApprovalRequired { get; set; }
    public List<int> ApproverUserIds { get; set; } = new();

    // فقط با مجوز ManualNumber
    public string? ManualNumber { get; set; }
}

public sealed record LetterComposeActor(int UserId, string? UserName, int? EmployeeId, bool CanManualNumber, bool IsAdmin);

/// <summary>یک خطا/هشدار با کد پایدار (برای ترجمه در UI و تست) + فیلد و جزئیات اختیاری.</summary>
public sealed record LetterComposeIssue(string Code, string? Field = null, string? Detail = null);

public class LetterComposeResult
{
    public bool Success { get; set; }
    public List<LetterComposeIssue> Errors { get; set; } = new();
    public List<LetterComposeIssue> Warnings { get; set; } = new();
    public int LetterId { get; set; }
    public string? LetterNumber { get; set; }
    public byte[]? RowVersion { get; set; }
    public int VersionNo { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

public class EmployeeDirectoryItem
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string? Position { get; set; }
    public string? DepartmentName { get; set; }
    public string? Email { get; set; }
    public string? PersonnelCode { get; set; }
    // غیر کارمند داخلی (پیمانکار/خارجی): هشدار «گیرنده خارج از سازمان»
    public bool IsExternal { get; set; }
}

public class LetterSenderBlockDto
{
    public int? EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string? Title { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    // مسیر تصویر امضا (اختیاری)
    public string? SignaturePath { get; set; }
}

public class ApproverOptionDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? Position { get; set; }
}

public class LetterVersionDto
{
    public int VersionNo { get; set; }
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public string Reason { get; set; } = "";
    public string? SavedByName { get; set; }
    public DateTime SavedDate { get; set; }
}

public class RecipientResolveResult
{
    public List<EmployeeDirectoryItem> Matched { get; set; } = new();
    public List<string> Unmatched { get; set; } = new();
    public bool Truncated { get; set; }
}
