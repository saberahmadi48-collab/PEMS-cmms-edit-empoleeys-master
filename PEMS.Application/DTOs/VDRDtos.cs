namespace PEMS.Application.DTOs;

public class VDRItemDto
{
    public int VDRItemId { get; set; }
    public int SequenceNo { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string? DisciplineCodeRaw { get; set; }
    public bool IsMultiDiscipline { get; set; }
    public string? MRNumber { get; set; }
    public int VendorId { get; set; }
    public string VendorName { get; set; } = "";
    public int? EquipmentId { get; set; }
    public string? EquipmentTagNumber { get; set; }
    public string? PoNumber { get; set; }
    public string? PackageDescription { get; set; }
    public string DocumentTypeCode { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string? DocumentDescription { get; set; }
    public string? MaterialCode { get; set; }
    public string? TypologyCode { get; set; }
    public string? VendorFormatDocumentNumber { get; set; }
    public string? ResponsiblePartyCode { get; set; }
    public int? DocumentClass { get; set; }
    public DateTime? PlannedFirstIssueDate { get; set; }
    public string WorkflowStatus { get; set; } = "";
    public int RevisionCount { get; set; }
    public int? LatestRevisionNo { get; set; }
    public string? LatestReviewCategory { get; set; }
    public DateTime? LatestVendorActualIssuedDate { get; set; }
    public bool IsOverdue { get; set; }
    public int? DelayDays { get; set; }

    // SCRUM-559: the More actions menu (soft delete and hold live on the Document base)
    public bool IsActive { get; set; } = true;
    public bool IsOnHold { get; set; }
    public string? HoldReason { get; set; }
    public DateTime? HoldDate { get; set; }

    public List<VDRSubmissionDto> Submissions { get; set; } = new();
}

public class VDRSubmissionDto
{
    public int VDRSubmissionId { get; set; }
    public int RevisionNo { get; set; }
    public string? VendorTransmittalNo { get; set; }
    public string? ClientTransmittalNo { get; set; }
    public int? ClientTransmittalId { get; set; }
    public int? AttachmentId { get; set; }
    public string? MainFilePath { get; set; }
    public DateTime? VendorActualIssuedDate { get; set; }
    public string? PurposeOfIssueCode { get; set; }
    public string? ClientCommentSheetNo { get; set; }
    public DateTime? ClientCommentDate { get; set; }
    public string? ReviewCategory { get; set; }
    public string? ReplySheetNo { get; set; }
    public DateTime? ReplySheetDate { get; set; }
    public bool RequiresResubmission { get; set; }

    // طبق بخش ۵.۶ سند (SLA بازبینی ۱۰/۱۵ روز کاری، تعطیل هفتگی=جمعه)
    public DateTime? ContractorCommentDueDate { get; set; }
    public bool IsContractorCommentOverdue { get; set; }
    public DateTime? VendorReturnDueDate { get; set; }
    public bool IsVendorReturnOverdue { get; set; }

    public DateTime? FtpUploadedDate { get; set; }
    public string? FtpRemotePath { get; set; }

    // طبق درخواست: پیوست واقعی فایل Comment Sheet و Reply Sheet - نه فقط شماره‌ی متنی
    public int? CommentSheetAttachmentId { get; set; }
    public string? CommentSheetFileName { get; set; }
    public string? CommentSheetFilePath { get; set; }

    public int? ReplySheetAttachmentId { get; set; }
    public string? ReplySheetFileName { get; set; }
    public string? ReplySheetFilePath { get; set; }
}

public class VDRDashboardDto
{
    public int TotalItems { get; set; }
    public int ClosedItems { get; set; }
    public int PendingResubmissionItems { get; set; }
    public int UnderReviewItems { get; set; }
    public int OverdueItems { get; set; }

    // طبق بخش ۵.۶ سند: تعداد VDR Item هایی که چرخه‌ی بازبینی فعال‌شان از مهلت ۱۰/۱۵ روز کاری گذشته
    public int SlaOverdueItems { get; set; }
    public double OnTimePercentage { get; set; }
    public Dictionary<string, int> ByWorkflowStatus { get; set; } = new();
    public Dictionary<string, int> ByReviewCategory { get; set; } = new();
    public List<VendorDelayDto> TopDelayedVendors { get; set; } = new();
}

public class VendorDelayDto
{
    public string VendorName { get; set; } = "";
    public double AverageDelayDays { get; set; }
    public int OverdueCount { get; set; }
    public string RiskLevel { get; set; } = "";
}
