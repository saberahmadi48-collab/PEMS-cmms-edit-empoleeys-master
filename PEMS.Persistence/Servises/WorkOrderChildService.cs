using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>The child-row edits of a Work Order, moved out of WorkOrderController (SCRUM-437); see <see cref="IWorkOrderChildService"/>. Messages keep the wording the controller showed.</summary>
public sealed class WorkOrderChildService(PEMSDbContext context, IWarehouseIntakeService? warehouseIntake = null) : IWorkOrderChildService
{
    private static bool IsFa() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
    private static string L(string fa, string en) => IsFa() ? fa : en;

    public async Task<(bool Success, string? Error)> AddSpareUsageAsync(int workOrderId, int materialItemId, decimal quantity, decimal? unitCost, int? actorEmployeeId)
    {
        if (quantity <= 0) return (false, "تعداد مصرف باید بزرگ‌تر از صفر باشه.");
        if (unitCost.HasValue && unitCost.Value < 0) return (false, "قیمت واحد نمی‌تونه منفی باشه.");

        var materialItem = await context.MaterialItems.FirstOrDefaultAsync(x => x.MaterialItemId == materialItemId);
        if (materialItem == null) return (false, L("قلم کالا پیدا نشد یا غیرفعال است.", "The material item was not found or is inactive."));

        // طبق سند Inventory بخش ۹-۱۰: بدون انتخاب انبار در فرم فعلی، موجودترین انبار برای این متریال انتخاب می‌شه - Transaction است که رکورد واقعی رو نگه می‌داره، نه این انتخاب.
        // Phase 2 / P4: ترتیب قطعی (در تساوی کمترین MaterialStockId) و رد ردیف مبهم
        var stock = await context.MaterialStocks
            .Where(x => x.MaterialItemId == materialItemId && x.IsActive)
            .OrderByDescending(x => x.OnHandQuantity - x.ReservedQuantity)
            .ThenBy(x => x.MaterialStockId)
            .FirstOrDefaultAsync();

        if (stock != null)
        {
            var exact = await StockRowResolver.FindExactAsync(context, materialItemId, stock.WarehouseId, stock.StorageLocationId, activeOnly: true);
            if (exact.IsAmbiguous) return (false, StockRowResolver.AmbiguousMessage(materialItemId, stock.WarehouseId, stock.StorageLocationId, exact.CandidateIds));
        }

        if (stock == null || stock.AvailableQuantity < quantity)
            return (false, $"موجودی کافی نیست - موجودی قابل‌مصرف «{materialItem.Description}»: {stock?.AvailableQuantity ?? 0}.");

        await context.MaintenanceSpareUsages.AddAsync(new MaintenanceSpareUsage
        {
            WorkOrderId = workOrderId, MaterialItemId = materialItemId, Quantity = quantity, UnitCost = unitCost,
            TotalCost = unitCost.HasValue ? unitCost.Value * quantity : null, UsageDate = DateTime.Now,
            IsActive = true, CreatedDate = DateTime.Now
        });
        await context.InventoryTransactions.AddAsync(new InventoryTransaction
        {
            MaterialItemId = materialItemId, WarehouseId = stock.WarehouseId, StorageLocationId = stock.StorageLocationId,
            TransactionType = "Issue", Quantity = quantity, ReferenceType = "WorkOrder", ReferenceId = workOrderId,
            WorkOrderId = workOrderId, EmployeeId = actorEmployeeId, TransactionDate = DateTime.Now,
            Reason = $"مصرف در WorkOrder #{workOrderId}"
        });
        stock.OnHandQuantity -= quantity;
        stock.LastUpdated = DateTime.Now;

        try
        {
            // یک SaveChanges (یک تراکنش): مصرف + تراکنش Issue + کسر موجودی با هم ذخیره می‌شوند یا هیچ‌کدام
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return (false, "موجودی هم‌زمان توسط عملیات دیگری تغییر کرد و مصرف ثبت نشد؛ هیچ تغییری ذخیره نشد. دوباره تلاش کنید.");
        }

        return (true, stock.IsLowStock ? "مصرف قطعه ثبت شد - هشدار: موجودی این متریال کمه." : null);
    }

    public async Task<(bool Success, string? Error)> ReturnSpareUsageAsync(int maintenanceSpareUsageId, int workOrderId, decimal returnQuantity, string? reason, int? actorEmployeeId)
    {
        if (returnQuantity <= 0) return (false, "مقدار برگشتی باید بزرگ‌تر از صفر باشه.");
        // Phase 2 / P4: مقصد برگشت از تراکنش Issue همان مصرف (انبار/محل مبدأ) خوانده می‌شود؛ Usage و موجودی و تراکنش در یک SaveChanges
        var (success, error, notFound) = await WorkOrderSpareReturn.ReturnAsync(context, maintenanceSpareUsageId, workOrderId, returnQuantity, reason, actorEmployeeId);
        if (notFound) return (false, L("مصرف قطعه پیدا نشد.", "The spare usage was not found."));
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> AddRequiredSkillAsync(int workOrderId, int skillId, string? minimumProficiencyLevel, decimal? plannedHours)
    {
        if (await context.RequiredSkills.AnyAsync(x => x.WorkOrderId == workOrderId && x.SkillId == skillId))
            return (false, "این مهارت از قبل به‌عنوان پیش‌نیاز این WorkOrder ثبت شده.");

        await context.RequiredSkills.AddAsync(new RequiredSkill
        {
            WorkOrderId = workOrderId, SkillId = skillId, MinimumProficiencyLevel = minimumProficiencyLevel, PlannedHours = plannedHours, CreatedDate = DateTime.Now
        });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, string? Before)> RemoveRequiredSkillAsync(int requiredSkillId, int workOrderId)
    {
        // H27: رکورد باید به همین Work Order تعلق داشته باشد
        var requiredSkill = await context.RequiredSkills.FirstOrDefaultAsync(x => x.RequiredSkillId == requiredSkillId && x.WorkOrderId == workOrderId);
        string? before = null;
        if (requiredSkill != null)
        {
            before = $"SkillId={requiredSkill.SkillId}; Level={requiredSkill.MinimumProficiencyLevel}; PlannedHours={requiredSkill.PlannedHours}";
            context.RequiredSkills.Remove(requiredSkill);
            await context.SaveChangesAsync();
        }
        return (true, null, before);
    }

    public async Task<(bool Success, string? Error)> AddRequiredMaterialAsync(int workOrderId, int materialItemId, decimal requiredQuantity)
    {
        // SCRUM-554: an unknown or inactive item (stale page, crafted post) is a message, not a foreign-key 500
        if (!await context.MaterialItems.AnyAsync(m => m.MaterialItemId == materialItemId && m.IsActive))
            return (false, L("قلم کالا پیدا نشد یا غیرفعال است.", "The material item was not found or is inactive."));
        if (await context.WorkOrderMaterials.AnyAsync(x => x.WorkOrderId == workOrderId && x.MaterialItemId == materialItemId))
            return (false, "این متریال از قبل به‌عنوان نیاز این WorkOrder ثبت شده.");

        await context.WorkOrderMaterials.AddAsync(new WorkOrderMaterial
        {
            WorkOrderId = workOrderId, MaterialItemId = materialItemId, RequiredQuantity = requiredQuantity, CreatedDate = DateTime.Now
        });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, string? Before)> RemoveRequiredMaterialAsync(int workOrderMaterialId, int workOrderId)
    {
        // SCRUM-472: a line a live store requisition already supplies cannot be removed (the requisition would lose its origin)
        if (warehouseIntake != null && !await warehouseIntake.CanRemoveMaterialAsync(workOrderMaterialId))
            return (false, L("یک درخواست انبار این ردیف را تأمین می‌کند. اول آن درخواست را لغو کنید.", "A store requisition already supplies this line. Cancel that requisition first."), null);

        // H27: رکورد باید به همین Work Order تعلق داشته باشد
        var requiredMaterial = await context.WorkOrderMaterials.FirstOrDefaultAsync(x => x.WorkOrderMaterialId == workOrderMaterialId && x.WorkOrderId == workOrderId);
        string? before = null;
        if (requiredMaterial != null)
        {
            before = $"MaterialItemId={requiredMaterial.MaterialItemId}; RequiredQuantity={requiredMaterial.RequiredQuantity}";
            context.WorkOrderMaterials.Remove(requiredMaterial);
            await context.SaveChangesAsync();
        }
        return (true, null, before);
    }
}
