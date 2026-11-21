namespace PEMS.Application.DTOs;

/// <summary>
/// P0.4 - نتیجه‌ی ساخت دستیِ قرارداد (ContractController.Create → IContractService.CreateWithCreatorEventAsync). روی SQL Server قرارداد، AuditLogهای عمومی و رویداد
/// «Contracts/Create/Allow» یک تراکنش‌اند. Success=true یعنی هر سه Commit شده‌اند. Committed=false با Success=false یعنی چیزی ذخیره نشد - مگر OutcomeUnknown=true
/// که یعنی Commit نامطمئن بود و بررسی بعدی هم وضعیت را روشن نکرد (هرگز «ساخته نشد» گزارش نمی‌شود).
/// AuthorizationDenied / AuthorizationIndeterminate: مجوز رد شد یا قابل‌ارزیابی نبود؛ هیچ قراردادی ساخته نشد (پیش از هر درج بررسی می‌شود).
/// </summary>
public sealed record ContractCreateResult(
    bool Success,
    string? Error,
    int? ContractId,
    bool Committed = false,
    ContractCreatorEventStatus CreatorEvent = ContractCreatorEventStatus.NotApplicable,
    bool OutcomeUnknown = false,
    bool RecoveredAfterUncertainCommit = false,
    bool AuthorizationDenied = false,
    bool AuthorizationIndeterminate = false);
