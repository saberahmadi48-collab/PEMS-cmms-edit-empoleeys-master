namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق ممیزی Material Management (Gap A/B) + Step 2/3: چرخه‌ی عمر کامل IRN -
/// Draft→Submitted→PendingApproval→Closed (+Cancelled، +بازگشت به Submitted روی Reject تایید).
/// طبق اصل «Received ≠ Available»: RecordDispositionAsync فقط مقدار Accepted/Rejected/Hold را
/// روی خطوط ثبت می‌کند و گردش‌کار تایید را (در Controller، از طریق IWorkflowRuntime) شروع
/// می‌کند - هیچ InventoryTransaction/MaterialStock ای تا CompleteDispositionAsync (بعد از تایید
/// واقعی QA) ساخته نمی‌شود، و فقط برای مقدار Accepted، نه Rejected/Hold.
/// </summary>
public interface IIrnReceiptService
{
    [PEMS.Application.Common.PemsOperation("Irn", "Submit", "IRN.Create")]
    Task<(bool Success, string? Error)> SubmitAsync(int irnReceiptId, string? reason, int? changedByEmployeeId);

    [PEMS.Application.Common.PemsOperation("Irn", "Cancel", "IRN.Create")]
    Task<(bool Success, string? Error)> CancelAsync(int irnReceiptId, string? reason, int? changedByEmployeeId);

    Task<(bool Success, string? Error, int? DispositionRecordId)> RecordDispositionAsync(
        int irnReceiptId,
        IReadOnlyList<(int LineId, decimal Accepted, decimal Rejected, decimal Hold)> lineDispositions,
        string? notes, int? dispositionedByEmployeeId);

    Task<(bool Success, string? Error)> CompleteDispositionAsync(int irnReceiptId, bool approved, int? actorEmployeeId);

    /// <summary>
    /// SCRUM-473 / D7: resolves the Hold quantity of one line of a Closed IRN. The accepted part is posted to stock (an Acceptance
    /// transaction, atomically with the resolution); the rejected part is never posted. accepted + rejected must equal the Hold quantity.
    /// Error texts are resource keys (Rcv_Err_*). Returns the IRN id.
    /// </summary>
    Task<(bool Success, string? Error, int? IrnReceiptId)> ResolveHoldAsync(
        int irnReceiptLineId, decimal accepted, decimal rejected, string? notes, string? userName, int? actorEmployeeId);
}
