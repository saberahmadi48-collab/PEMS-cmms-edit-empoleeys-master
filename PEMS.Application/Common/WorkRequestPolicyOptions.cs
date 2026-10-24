namespace PEMS.Application.Common;

/// <summary>
/// A3: تنظیم تفکیک وظایف Work Request. پیش‌فرض روشن است (درخواست‌دهنده ≠ بازبین/تأییدکننده، تأییدکننده ≠ بازبین).
/// مقدار از پیکربندی «Cmms:WorkRequest:EnforceSegregationOfDuties» خوانده می‌شود؛ SystemAdmin فقط با ثبت ممیزی می‌تواند Override کند.
/// </summary>
public sealed class WorkRequestPolicyOptions
{
    public bool EnforceSegregationOfDuties { get; set; } = true;
}
