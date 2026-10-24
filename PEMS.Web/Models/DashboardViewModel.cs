namespace PEMS.Web.Models;

public class DashboardViewModel
{
    // لیست کارت‌های KPI (جایگزین کامل KpiLinks)
    public List<KpiCardViewModel> KpiCards { get; set; } = new();

    // مقادیر خام (استفاده شده در Controller برای پر کردن KpiCards)
    public int ProjectsCount { get; set; }
    public int DocumentsCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReturnedWithCommentsCount { get; set; }
    public int ActiveContractsCount { get; set; }
    public int OpenRevisionsCount { get; set; }
    public int OpenWorkOrdersCount { get; set; }
    public int AIAlertsCount { get; set; }
    public int EquipmentCount { get; set; }
    public int OpenMRCount { get; set; }
    public int TransmittalsCount { get; set; }

    // ماژول‌هایی که هنوز پیاده‌سازی نشده‌اند
    public bool ModulesPending { get; set; } = true;

    // چارت وضعیت اسناد
    public Dictionary<string, int> DocumentsByStatus { get; set; } = new();
}

// طبق PEMS-UI-04: خلاصه‌ی "چه چیزی نیاز به اقدام من دارد" - هر دسته فقط وقتی نمایش داده می‌شود
// که نقش کاربر اصلاً برای آن دسته مرتبط باشد (نه صرفاً چون شمارش صفر است).
public class MyWorkSummary
{
    public bool ShowPermitApprovals { get; set; }
    public int PendingPermitApprovals { get; set; }

    public bool ShowWorkOrders { get; set; }
    public int MyWorkOrdersCount { get; set; }

    public bool ShowReferrals { get; set; }
    public int MyReferralsCount { get; set; }

    // طبق «PEMS Phase 2A — EDMS Foundation» بخش ۳: قبلاً هیچ‌کدام از Engineering Documents/VDR/
    // TechnicalQuery/MOC در My Work نبودند (فقط Permit/WorkOrder/Letters) - این اولین مورد
    // (EDMS)، دقیقاً همون قانون دیسیپلین/ViewAllDisciplines موجود، بدون هیچ موتور Task جدید
    public bool ShowDocumentReviews { get; set; }
    public int PendingDocumentReviewsCount { get; set; }

    // سایر تأییدهای گردش‌کار (همه‌ی ماژول‌ها به‌جز Permit/مدارک مهندسی) - فقط وقتی کاربر واقعاً کاری منتظر اقدام دارد نمایش داده می‌شود
    public bool ShowOtherApprovals { get; set; }
    public int OtherApprovalsCount { get; set; }

    public bool HasAnyCategory => ShowPermitApprovals || ShowWorkOrders || ShowReferrals || ShowDocumentReviews || ShowOtherApprovals;

    public bool HasAnyActionable =>
        (ShowPermitApprovals && PendingPermitApprovals > 0) ||
        (ShowWorkOrders && MyWorkOrdersCount > 0) ||
        (ShowReferrals && MyReferralsCount > 0) ||
        (ShowDocumentReviews && PendingDocumentReviewsCount > 0) ||
        (ShowOtherApprovals && OtherApprovalsCount > 0);
}