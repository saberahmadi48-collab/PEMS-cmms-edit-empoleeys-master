using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// ماژول مکاتبات رسمی (Official Correspondence). یک Entity واحد برای هر سه نوع (Incoming،
/// Outgoing، Internal Memo) با فیلد LetterType - به‌جای سه جدول تقریباً یکسان، طبق همان اصل
/// یکپارچه‌سازی که در ProcurementPackage/Document Core استفاده شد.
/// </summary>
public class Letter : BaseEntity
{
    [Key]
    public int LetterId { get; set; }

    // Incoming, Outgoing, Internal
    public string LetterType { get; set; } = string.Empty;

    public string LetterNumber { get; set; } = string.Empty;

    public DateTime LetterDate { get; set; } = DateTime.Now;

    // برای Incoming: فرستنده‌ی بیرونی؛ برای Outgoing: گیرنده‌ی بیرونی؛ برای Internal هردو آزاد است
    public string? Sender { get; set; }

    public string? Receiver { get; set; }

    // برای مکاتبات داخلی، ارتباط مستقیم با گیرنده‌ی داخلی (اختیاری)
    public int? ReceiverDepartmentId { get; set; }
    public Department? ReceiverDepartment { get; set; }

    public int? ReceiverEmployeeId { get; set; }
    public Employee? ReceiverEmployee { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string? Body { get; set; }

    // Low, Normal, High, Urgent
    public string Priority { get; set; } = "Normal";

    // Public, Internal, Confidential, Restricted, HighlyConfidential
    public string ConfidentialityLevel { get; set; } = "Internal";

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? DisciplineId { get; set; }
    public Discipline? Discipline { get; set; }

    // پیوند مستقیم دوطرفه با یک مدرک مهندسی/Vendor مشخص (نه فقط پروژه/دیسیپلین مشترک)
    public int? RelatedDocumentId { get; set; }
    public Document? RelatedDocument { get; set; }

    // پیوند مستقیم دوطرفه با یک Material Requisition مشخص
    public int? RelatedMRId { get; set; }
    public MaterialRequisition? RelatedMR { get; set; }

    // Incoming: Registered, UnderReview, Referred, Closed
    // Outgoing: Draft, PendingApproval, Approved, Rejected, Issued
    // Internal: Sent, Acknowledged, Closed
    public string Status { get; set; } = "Registered";

    public int? RegisteredById { get; set; }

    public DateTime? ApprovedDate { get; set; }
    public int? ApprovedById { get; set; }

    // پیوند به نامه‌ی والد در یک زنجیره‌ی پاسخ (Thread View) - طبق نمونه‌ی کارتابل مرجع
    public int? ParentLetterId { get; set; }
    public Letter? ParentLetter { get; set; }

    public DateTime? IssuedDate { get; set; }

    public DateTime? ClosedDate { get; set; }

    // تایید الکترونیکی سبک (نام تایپ‌شده + مهر زمانی) - نه امضای PKI واقعی
    public string? SignedByName { get; set; }
    public DateTime? SignedDate { get; set; }

    // ==============================================================================
    // طبق «PEMS Letter Details — Controlled Lifecycle» بند ۴-۱۱: برگشت برای اصلاح (Returned
    // For Revision) یک وضعیت کسب‌وکاری کاملاً جدا از Rejected است - نامه رد نشده، فقط نیاز به
    // اصلاح دارد و باید توسط همان نویسنده‌ی اصلی (RegisteredById) دوباره ویرایش و ارسال شود.
    // این سه فیلد فقط Cache آخرین برگشت هستند (دقیقاً همون الگوی DocumentRevision.
    // ClientReviewStatus از فاز ۲ EDMS) - تاریخچه‌ی کامل چند-دوره‌ای در LetterReview است.
    //
    // *UserId عمداً بدون Navigation/FK پیکربندی‌شده - دقیقاً همون قرارداد موجود RegisteredById/
    // ApprovedById در همین Entity (شناسه‌ی ApplicationUser، نه Employee - نامش هم برای وضوح
    // همین الان اصلاح شد).
    // ==============================================================================
    public int? ReturnedByUserId { get; set; }
    public DateTime? ReturnedDate { get; set; }
    public string? ReturnReason { get; set; }

    public int? RejectedByUserId { get; set; }
    public DateTime? RejectedDate { get; set; }
    public string? RejectReason { get; set; }

    // هر بار نویسنده‌ی اصلی بعد از برگشت، اصلاح و دوباره ارسال کند، یکی افزایش می‌یابد - برای
    // نمایش «رویژن N» به کاربر، بدون نیاز به شمارش دستی ردیف‌های LetterReview
    public int RevisionCount { get; set; }

    // پاسخ‌خواهی (Phase 2): آیا این نامه نیاز به پاسخ دارد و تا چه تاریخی. «پاسخ‌داده‌شده» ذخیره نمی‌شود، از وجود نامه‌ی
    // فرزند صادرشده (ParentLetterId = این نامه) محاسبه می‌شود تا هیچ پرچم دستی با واقعیت ناسازگار نشود؛
    // «معوق» هم محاسبه‌شده است (ResponseRequired && ResponseDueDate گذشته && هنوز بی‌پاسخ && نامه بسته نشده).
    public bool ResponseRequired { get; set; }

    public DateTime? ResponseDueDate { get; set; }

    // ---- بازطراحی مکاتبات داخلی (Formal Internal Letter) ----
    // نوع محتوایی نامه (Information, Request, Instruction, Approval, Recommendation, Notice, StatusReport) - جدا از LetterType
    // که «جهت» نامه است (Incoming/Outgoing/Internal).
    [MaxLength(30)]
    public string? Category { get; set; }

    // شماره‌ی مرجع نامه‌ی مرتبط (اختیاری)
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    // بلوک فرستنده به‌صورت «عکس لحظه‌ای» (Snapshot) در زمان نوشتن: ارتقای بعدیِ سمت/انتقال دپارتمان فرستنده نباید متن
    // نامه‌ی قبلاً صادرشده را عوض کند.
    public int? SenderEmployeeId { get; set; }

    [MaxLength(150)]
    public string? SenderTitle { get; set; }

    public int? SenderDepartmentId { get; set; }

    public int? RelatedDepartmentId { get; set; }

    // آیا قبل از ارسال، زنجیره‌ی تایید لازم است (برای Outgoing همیشه true؛ برای Internal انتخابی). مراحل در LetterApprovalStep.
    public bool ApprovalRequired { get; set; }

    // وضعیت Archived با این تاریخ (آرشیو دستی بعد از ارسال، یا خودکار پس از بی‌فعالیتیِ طولانی). حذف فیزیکی ندارد.
    public DateTime? ArchivedDate { get; set; }

    // ---- ثبت مکاتبه‌ی وارده (DCC) ----
    // شماره‌ی نامه‌ی فرستنده (مرجع خودِ طرف مقابل) - کلید تشخیص ثبت تکراری همراه با طرف مکاتبه
    [MaxLength(100)]
    public string? SenderReference { get; set; }

    // تاریخ روی نامه (DocumentDate) و تاریخ دریافت در DCC (ReceivedDate) دو چیز جدا هستند؛ LetterDate برای Incoming همان تاریخ دریافت است
    public DateTime? DocumentDate { get; set; }

    public DateTime? ReceivedDate { get; set; }

    // OfficialLetter, Email, Portal, Hand - قبلاً به‌صورت «[منبع: …]» در ابتدای متن نامه ثبت می‌شد
    [MaxLength(30)]
    public string? ReceiptChannel { get; set; }

    public int? CorrespondentPartyId { get; set; }
    public CorrespondentParty? CorrespondentParty { get; set; }

    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public ICollection<LetterReview> Reviews { get; set; } = new List<LetterReview>();

    /// <summary>
    /// این نامه یک «ارجاع» است: نامه‌ی داخلی تازه‌ای که ParentLetterId آن نامه‌ی ارجاع‌شده است، موضوعش همان موضوع نامه‌ی اصلی است و فقط برچسب «ارجاع» می‌گیرد.
    /// هنگام ارسال برای هر گیرنده‌ی «To» یک LetterReferral روی نامه‌ی اصلی ثبت می‌شود (پیگیری مهلت/معوق‌ها همان قبلی می‌ماند) که با
    /// LetterReferral.ReferralLetterId به همین نامه وصل است؛ ارجاع و پاسخ هر دو در یک رشته‌ی مکاتبه دیده می‌شوند.
    /// </summary>
    public bool IsReferral { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public ICollection<DocumentAttachment> Attachments { get; set; } = new List<DocumentAttachment>();

    public ICollection<LetterReferral> Referrals { get; set; } = new List<LetterReferral>();

    public ICollection<LetterRecipient> Recipients { get; set; } = new List<LetterRecipient>();

    public ICollection<LetterTag> Tags { get; set; } = new List<LetterTag>();

    public ICollection<LetterLink> Links { get; set; } = new List<LetterLink>();
}
