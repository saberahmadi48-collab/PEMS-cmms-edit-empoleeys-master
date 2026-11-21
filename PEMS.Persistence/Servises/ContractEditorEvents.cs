using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// P0-10 / D-OPEN-05 (تصمیم مالک ۲۰۲۶-۱۰-۱۴: «کسی که قرارداد را ویرایش کرده نباید همان را تأیید کند»): رویداد
/// «Contracts/Edit/Allow» در AuthorizationAuditLog که سیاست SoD موجود Contracts.Edit↔Contracts.Approve (AccessControlService.
/// DecideForUserAsync) به آن تکیه دارد و تا پیش از این هیچ مسیری آن را نمی‌نوشت. فقط ویرایش‌های محتوایی قرارداد (ارسال برای
/// بازبینی، افزودن سند، درخواست تغییر ارزش) رویداد می‌نویسند؛ ثبت بازبینی رشته‌ای عمداً «ویرایش» حساب نمی‌شود (بازبینی مستقل است).
/// رویداد به Context اضافه می‌شود و با همان SaveChanges تغییر کسب‌وکاری ذخیره می‌شود (همه یا هیچ). تکرار برای SoD بی‌ضرر است.
/// </summary>
public static class ContractEditorEvents
{
    public static void Add(PEMSDbContext context, int contractId, int? userId, string? ipAddress, string reason)
    {
        // بدون کاربر شناخته‌شده رویدادی قابل‌انتساب نیست (SoD با UserId کار می‌کند)
        if (!userId.HasValue) return;

        context.AuthorizationAuditLogs.Add(new AuthorizationAuditLog
        {
            UserId = userId.Value,
            ResourceName = "Contracts",
            ActionName = "Edit",
            ResourceId = contractId.ToString(),
            Decision = AccessDecisionReason.Allow.ToString(),
            Reason = reason,
            IpAddress = ipAddress,
            Timestamp = DateTime.Now
        });
    }
}
