using System.Linq.Expressions;
using PEMS.Domain.Entities;

namespace PEMS.Application.Common;

/// <summary>
/// تعریف واحد گروه‌های وضعیت مدارک مهندسی برای داشبوردها (D20 قبلاً فقط در لایه‌ی وب بود؛ اینجا آمد تا سرویس داشبورد مدیریتی هم از همان تعریف استفاده کند و دو
/// عدد متفاوت برای «رد شده» ساخته نشود). Models/DashboardDefinitions در وب همین عبارت‌ها را بازاستفاده می‌کند.
/// گردش‌کار: Draft → Submitted (Check) → IDC Review → Checked (Approval) → Approved (Issue) → Issued؛ کارفرما: Client Approved یا Rejected (CO/AC/RE).
/// </summary>
public static class EngineeringDocumentDefinitions
{
    public static readonly string[] ApprovedStatuses = { "Approved", "Issued", "Client Approved" };
    public static readonly string[] InReviewStatuses = { "Submitted", "Checked", "IDC Review" };

    public const string BucketApproved = "Approved";
    public const string BucketInReview = "InReview";
    public const string BucketReturned = "Returned";
    public const string BucketDraft = "Draft";
    public const string BucketOther = "Other";

    public static string BucketOf(string? status)
    {
        if (status == null) return BucketOther;
        if (ApprovedStatuses.Contains(status)) return BucketApproved;
        if (InReviewStatuses.Contains(status)) return BucketInReview;
        if (status == "Rejected") return BucketReturned;
        if (status == "Draft") return BucketDraft;
        return BucketOther;
    }

    /// <summary>منتظر اقدام در گردش‌کار داخلی (Submitted یا Checked).</summary>
    public static readonly Expression<Func<EngineeringDocument, bool>> PendingApproval = x => x.Status == "Submitted" || x.Status == "Checked";

    /// <summary>ردشده‌ی واقعی: وضعیت Rejected و پاسخ کارفرما RE (یا بدون کد پاسخ، یعنی رد داخلی).</summary>
    public static readonly Expression<Func<EngineeringDocument, bool>> Rejected = x => x.Status == "Rejected" && (x.CommentStatus == null || x.CommentStatus == "" || x.CommentStatus == "RE");

    /// <summary>بازگشته با کامنت کارفرما (CO یا AC): نیاز به اصلاح و ارسال مجدد دارد ولی «رد» نیست.</summary>
    public static readonly Expression<Func<EngineeringDocument, bool>> ReturnedWithComments = x => x.Status == "Rejected" && (x.CommentStatus == "CO" || x.CommentStatus == "AC");

    /// <summary>قراردادهای در اجرا: Effective، یا Active میراثی (پیش از معماری گردش‌کار).</summary>
    public static readonly Expression<Func<Contract, bool>> ActiveContract = c => c.Status == "Effective" || c.Status == "Active";
}
