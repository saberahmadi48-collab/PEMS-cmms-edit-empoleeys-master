using PEMS.Application.Common;
using PEMS.UI.Abstractions;

namespace PEMS.Web.ViewModels;

/// <summary>
/// مدل کمکی برای کامپوننت مشترک Pagination (_Pagination.cshtml, which hands over to the PEMS.UI partial _PemsPagination). BaseUrl معمولاً همان
/// Request.Path است؛ QueryString فعلی (بدون پارامتر page/pageSize) حفظ می‌شود تا فیلترهای
/// دیگر هنگام تغییر صفحه/اندازه‌ی صفحه از بین نروند.
///
/// طبق سند «PEMS — Global Data Grid Standard»: PageSize/AllowedPageSizes/GridKey اختیاری‌اند
/// (پیش‌فرض صفر/خالی) تا Grid هایی که هنوز به استاندارد جدید مهاجرت نکرده‌اند (و صرفاً از
/// Page/TotalPages/TotalCount موجود استفاده می‌کنند) بدون تغییر کار کنند - این افزونه است،
/// نه یک شکست‌دهنده‌ی سازگاری.
/// </summary>
public class PaginationViewModel : PaginationModel
{
    public PaginationViewModel() => AllowedPageSizes = DataGridPageSize.Allowed;
}
