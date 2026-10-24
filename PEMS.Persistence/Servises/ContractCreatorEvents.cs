using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// P0.1 / P0.3 / P0.4 - رویداد «Contracts/Create/Allow» در AuthorizationAuditLog که SoD «ایجادکننده ≠ تاییدکننده» (AccessControlService.DecideForUserAsync) به آن
/// تکیه دارد. نوشتن، بررسی وجود و ترمیم رویداد برای هر دو مسیر ساخت قرارداد (مناقصه و دستی) این‌جا مشترک است. هیچ قاعده‌ی یکتایی در دیتابیس وجود ندارد؛
/// هم‌زمانی فقط با قفل کاربردی به‌ازای قرارداد (ContractCreationLocks.CreatorEventResource) در همین مسیرها هماهنگ می‌شود، و تکرار برای SoD بی‌ضرر است
/// (SoD فقط «وجود» رویداد را می‌پرسد).
/// </summary>
public static class ContractCreatorEvents
{
    public static Task<bool> ExistsAsync(PEMSDbContext context, int contractId)
    {
        var resourceId = contractId.ToString();
        var allow = AccessDecisionReason.Allow.ToString();
        return context.AuthorizationAuditLogs.AsNoTracking().AnyAsync(l =>
            l.ResourceName == "Contracts" && l.ActionName == "Create" && l.ResourceId == resourceId && l.Decision == allow);
    }

    /// <summary>
    /// رویداد را به Context اضافه می‌کند (دقیقاً همان شکل ردیفی که AccessControlService.AuthorizeAsync می‌نویسد). ذخیره‌اش با فراخواننده است تا در همان
    /// تراکنش قرارداد بماند. با رکورد گارد Tender.Approve (ResourceId = شناسه‌ی مناقصه) و با ردیف پیش‌بررسی مجوز مسیر دستی (ResourceId = null) متمایز است.
    /// </summary>
    public static void Add(PEMSDbContext context, int contractId, int userId, string? ipAddress, string reason,
        string? scopeType = null, string? scopeValue = null)
    {
        context.AuthorizationAuditLogs.Add(new AuthorizationAuditLog
        {
            UserId = userId,
            ResourceName = "Contracts",
            ActionName = "Create",
            ResourceId = contractId.ToString(),
            ScopeType = scopeType,
            ScopeValue = scopeValue,
            Decision = AccessDecisionReason.Allow.ToString(),
            Reason = reason,
            IpAddress = ipAddress,
            Timestamp = DateTime.Now
        });
    }

    /// <summary>
    /// هسته‌ی ترمیم/تضمین رویداد؛ داخل تراکنش جاری اجرا می‌شود و استثنا را نمی‌بلعد. فقط قفل رویداد را می‌گیرد. اگر رویداد از قبل هست (یا قرارداد نیست
    /// یا ایجادکننده شناسایی نمی‌شود) چیزی نمی‌نویسد. ایجادکننده فقط از Contract.CreatedBy (نوشته‌شده هنگام ساخت) تعیین می‌شود، هرگز از کاربری که اکنون ترمیم می‌کند.
    /// </summary>
    public static async Task<(ContractCreatorEventStatus Status, bool Wrote)> EnsureCoreAsync(
        PEMSDbContext context, ContractCreationScope scope, ContractCreationScope.State state, ILogger? logger, int contractId, string? ipAddress)
    {
        await scope.AcquireLockAsync(ContractCreationLocks.CreatorEventResource(contractId));

        var contract = await context.Contracts.AsNoTracking()
            .Where(x => x.ContractId == contractId)
            .Select(x => new { x.ContractId, x.CreatedBy })
            .FirstOrDefaultAsync();
        if (contract == null) return (ContractCreatorEventStatus.ContractNotFound, false);

        if (await ExistsAsync(context, contractId))
            return (ContractCreatorEventStatus.AlreadyRecorded, false);

        int? creatorUserId = null;
        if (!string.IsNullOrWhiteSpace(contract.CreatedBy))
        {
            var matches = await context.Users.AsNoTracking()
                .Where(u => u.UserName == contract.CreatedBy)
                .Select(u => u.Id)
                .Take(2)
                .ToListAsync();
            if (matches.Count == 1) creatorUserId = matches[0];
        }

        if (creatorUserId == null)
        {
            logger?.LogWarning("Contract {ContractId} has no Contracts.Create event and its creator could not be identified uniquely; no event was written.", contractId);
            return (ContractCreatorEventStatus.CreatorUnidentified, false);
        }

        state.EventWriteAttempted = true;
        Add(context, contractId, creatorUserId.Value, ipAddress, "Creator attribution recovered from Contract.CreatedBy (the original event was not recorded)");
        await context.SaveChangesAsync();
        return (ContractCreatorEventStatus.Repaired, true);
    }

    /// <summary>ترمیم مستقل (تراکنش + قفل + طبقه‌بندی شکست) برای هر قرارداد، چه از مناقصه چه دستی.</summary>
    public static async Task<ContractCreatorEventStatus> EnsureAsync(
        PEMSDbContext context, ContractCreationScope scope, ILogger? logger, int contractId, string? ipAddress)
    {
        var state = new ContractCreationScope.State();
        try
        {
            return await scope.RunAsync(state, async () =>
            {
                var r = await EnsureCoreAsync(context, scope, state, logger, contractId, ipAddress);
                return (r.Status, r.Wrote);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Contract {ContractId}: repairing the Contracts.Create event reported an error (commitAttempted={CommitAttempted}).", contractId, state.CommitAttempted);

            // P0.5 / H-1: پس از «درخواست Commit» فقط خواندنِ زیر قفل رویدادِ همان قرارداد (که تراکنش ترمیم تا پایانش نگه می‌داشت) معتبر است؛ روی دیتابیس با
            // READ_COMMITTED_SNAPSHOT=ON یک SELECT ساده می‌تواند نسخه‌ی قبل از Commit را ببیند. قفل/خواندن ناموفق = نامشخص، نه «ثبت نشد».
            if (state.CommitAttempted)
            {
                var check = await scope.TryReadUnderLockAsync(ContractCreationLocks.CreatorEventResource(contractId), () => ExistsAsync(context, contractId));
                if (!check.Verified) return ContractCreatorEventStatus.OutcomeUnknown;
                return check.Value ? ContractCreatorEventStatus.Repaired : ContractCreatorEventStatus.NotRecorded;
            }

            // درخواست Commit صادر نشد ⇒ Rollback قطعی؛ این خواندن فقط وضعیت رویداد را برای پیام می‌سازد. روی Provider بدون تراکنش ممکن است ردیف رویداد ذخیره
            // شده و فقط AuditLog عمومی‌اش شکست خورده باشد (آن‌گاه «ترمیم‌شده»)؛ روی SQL Server رویدادِ موجود از ترمیم هم‌زمان دیگری است.
            try
            {
                if (await ExistsAsync(context, contractId))
                    return !state.Transactional && state.EventWriteAttempted ? ContractCreatorEventStatus.Repaired : ContractCreatorEventStatus.AlreadyRecorded;
                return ContractCreatorEventStatus.NotRecorded;
            }
            catch (Exception verifyEx) when (verifyEx is not OperationCanceledException)
            {
                logger?.LogError(verifyEx, "Contract {ContractId}: could not verify the Contracts.Create event after an error.", contractId);
                return ContractCreatorEventStatus.NotRecorded;
            }
        }
    }
}
