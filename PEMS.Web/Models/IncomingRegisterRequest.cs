namespace PEMS.Web.Models;

/// <summary>
/// فرم ثبت مکاتبه‌ی وارده (multipart). تاریخ‌ها همیشه به‌صورت میلادی ISO (yyyy-MM-dd) می‌آیند؛ انتخابگر جلالی در مرورگر تبدیل را انجام می‌دهد.
/// Files و FileRoles موازی‌اند (نقش فایل i در FileRoles[i]).
/// </summary>
public class IncomingRegisterRequest
{
    public int? ProjectId { get; set; }
    public int? ContractId { get; set; }
    public int? CorrespondentPartyId { get; set; }
    public string? NewPartyName { get; set; }
    public string? NewPartyType { get; set; }

    public string? SenderReference { get; set; }
    public string? DocumentDate { get; set; }
    public string? ReceivedDate { get; set; }
    public string? ReceiptChannel { get; set; }

    public string? Subject { get; set; }
    public string? Summary { get; set; }
    public string? Priority { get; set; }
    public string? Category { get; set; }
    public string? ConfidentialityLevel { get; set; }
    public int? DisciplineId { get; set; }
    public int? ParentLetterId { get; set; }

    public bool ResponseRequired { get; set; }
    public string? ResponseDueDate { get; set; }
    public int? OwnerEmployeeId { get; set; }
    public List<int>? CcEmployeeIds { get; set; }
    public string? Instruction { get; set; }

    // آرایه‌ی JSON از {linkType, targetId}
    public string? Links { get; set; }

    public bool ConfirmDuplicate { get; set; }

    public List<IFormFile>? Files { get; set; }
    public List<string>? FileRoles { get; set; }
}
