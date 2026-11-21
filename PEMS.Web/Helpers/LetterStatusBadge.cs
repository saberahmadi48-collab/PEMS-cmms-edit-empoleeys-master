namespace PEMS.Web.Helpers;

/// <summary>
/// طبق «PEMS Correspondence Workspace» بند ۳۳: نگاشت Status→رنگ Badge قبلاً در سه View جدا
/// (Index/Details/MyReferrals) کپی‌شده بود - دقیقاً همون الگوی DocumentStatusBadge برای این
/// واژگان وضعیت هم استفاده می‌شود تا هر سه همیشه هم‌زمان و یکسان به‌روز بمانند.
/// </summary>
public static class LetterStatusBadge
{
    public static string CssClass(string? status) => status switch
    {
        "Draft" or "Archived" => "bg-secondary",
        "PendingApproval" or "UnderReview" => "bg-warning text-dark",
        "ReturnedForRevision" => "bg-orange text-dark",
        "Approved" => "bg-info text-dark",
        "Issued" or "Sent" or "Registered" => "bg-primary",
        "Closed" or "Acknowledged" => "bg-success",
        "Rejected" => "bg-danger",
        "Referred" => "bg-purple text-white",
        _ => "bg-info text-dark"
    };
}
