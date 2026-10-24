namespace PEMS.Application.Common;

/// <summary>
/// B8: برای کارمند در تاریخ کار نرخ ساعتی (عادی یا اضافه‌کاری) تعریف نشده است. قبلاً هزینه بی‌صدا صفر ثبت می‌شد و هزینه‌ی Work Order کمتر از واقع گزارش می‌شد.
/// کنترلر این خطا را می‌گیرد و پیام + لینک تنظیم نرخ (پروفایل کارمند) را نشان می‌دهد؛ الگوی Snapshot نرخ (ذخیره در رکورد ساعت‌کار) دست‌نخورده است.
/// </summary>
public sealed class LaborRateMissingException : Exception
{
    public int EmployeeId { get; }
    public string RateType { get; }

    public LaborRateMissingException(int employeeId, string rateType, DateTime workDate)
        : base(Build(employeeId, rateType, workDate))
    {
        EmployeeId = employeeId;
        RateType = rateType;
    }

    private static string Build(int employeeId, string rateType, DateTime workDate)
    {
        var fa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        var typeFa = rateType == "Overtime" ? "اضافه‌کاری" : "عادی";
        return fa
            ? $"برای کارمند (شناسه {employeeId}) در تاریخ {workDate:yyyy-MM-dd} نرخ ساعتی {typeFa} تعریف نشده است؛ ثبت ساعت‌کار با نرخ صفر مجاز نیست. ابتدا نرخ را در پروفایل کارمند ثبت کنید."
            : $"No {rateType.ToLowerInvariant()} hourly rate is defined for employee {employeeId} on {workDate:yyyy-MM-dd}; labor cannot be recorded at a zero rate. Set the rate in the employee profile first.";
    }
}
