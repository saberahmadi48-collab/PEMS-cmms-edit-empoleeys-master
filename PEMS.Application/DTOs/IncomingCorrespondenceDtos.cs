namespace PEMS.Application.DTOs;

/// <summary>ورودی ثبت یک مکاتبه‌ی وارده در DCC (فرم دو-پنلی). اعتبارسنجی در <c>IncomingCorrespondenceRules</c> و سرویس انجام می‌شود، نه Controller.</summary>
public class IncomingRegistrationInput
{
    public int? ProjectId { get; set; }
    public int? ContractId { get; set; }

    // یا طرف موجود، یا نام طرف جدید (که «موقت» ساخته و برای تایید سرپرست علامت می‌خورد)
    public int? CorrespondentPartyId { get; set; }
    public string? NewPartyName { get; set; }
    public string? NewPartyType { get; set; }

    public string? SenderReference { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string ReceiptChannel { get; set; } = "OfficialLetter";

    public string Subject { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? Category { get; set; }
    public string ConfidentialityLevel { get; set; } = "Internal";
    public int? DisciplineId { get; set; }

    // «در پاسخ به»: نامه‌ی صادره‌ی ما که این نامه جواب آن است
    public int? ParentLetterId { get; set; }

    // اقدام هنگام ثبت
    public bool ResponseRequired { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public int? OwnerEmployeeId { get; set; }
    public List<int> CcEmployeeIds { get; set; } = new();
    public string? Instruction { get; set; }

    public List<IncomingFileInput> Files { get; set; } = new();
    public List<IncomingLinkInput> Links { get; set; } = new();

    // کاربر هشدار «ثبت تکراری» را دیده و تایید کرده است
    public bool ConfirmDuplicate { get; set; }
}

public class IncomingFileInput
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Length { get; set; }

    // Original (اسکن نامه‌ی اصلی)، Attachment (پیوست نامه)، EmailCopy (نسخه‌ی ایمیل)
    public string Role { get; set; } = "Original";

    // جریان قابل Seek (برای بررسی امضای فایل و هش) - فراخواننده مالک بستن آن است
    public Stream? Content { get; set; }
}

public sealed record IncomingLinkInput(string LinkType, int TargetId);

public class IncomingRegistrationResult
{
    public bool Success { get; set; }
    public int? LetterId { get; set; }
    public string? LetterNumber { get; set; }
    public List<LetterComposeIssue> Issues { get; set; } = new();

    // اگر ثبت تکراری تشخیص داده شد (طرف مکاتبه + شماره‌ی فرستنده) و کاربر هنوز تایید نکرده
    public int? DuplicateLetterId { get; set; }
    public string? DuplicateLetterNumber { get; set; }

    public static IncomingRegistrationResult Fail(params LetterComposeIssue[] issues)
        => new() { Issues = issues.ToList() };
}

public class PartyContractDto
{
    public int ContractId { get; set; }
    public string ContractNo { get; set; } = string.Empty;
}

public class PartySearchItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string PartyType { get; set; } = string.Empty;
    public bool IsProvisional { get; set; }
    public List<PartyContractDto> Contracts { get; set; } = new();
}

public class PartyThreadItemDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = string.Empty;

    // Incoming / Outgoing (از دید ما)
    public string Direction { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SenderReference { get; set; }
    public bool ResponseRequired { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public bool Answered { get; set; }
    public bool Overdue { get; set; }
}

public class PartyThreadDto
{
    public int PartyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PartyType { get; set; } = string.Empty;

    // نامه‌های واردِ پاسخ‌خواه که هنوز جواب نداده‌ایم
    public int AwaitingUs { get; set; }

    // نامه‌های صادره‌ی پاسخ‌خواه که طرف مقابل هنوز جواب نداده است
    public int AwaitingThem { get; set; }

    public DateTime? LastContact { get; set; }
    public int Total { get; set; }
    public List<PartyThreadItemDto> Items { get; set; } = new();
}

public class OpenOutgoingDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? ResponseDueDate { get; set; }

    // پاسخ‌خواه و هنوز بی‌پاسخ
    public bool AwaitingReply { get; set; }
}

public class LinkSearchItemDto
{
    public string LinkType { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public class PersonSearchItemDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? Department { get; set; }
}
