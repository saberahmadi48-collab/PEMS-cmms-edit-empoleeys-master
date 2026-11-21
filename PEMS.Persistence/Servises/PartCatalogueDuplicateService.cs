using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.PartCatalogue;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// جستجوی قطعه‌ی موجود + کاندیدهای تکراری بر پایه‌ی داده‌ی واقعی: درخواست‌های «تکمیل‌شده»ی کاتالوگ (قطعه‌ی تأییدشده با
/// AssignedPartNumber) و Material Master. عمداً جدول Alias ندارد: شناسه‌های جایگزین موجود
/// (ManufacturerPartNumber / SupplierPartNumber / OldManufacturerPartNumber) روی همین رکوردها نگهداری می‌شوند و مقایسه
/// بر پایه‌ی نرمال‌سازی (حذف فاصله و - _ / .) در SQL انجام می‌شود؛ جدول Alias فقط وقتی لازم است که یک قطعه بیش از یک
/// MPN فعال هم‌زمان داشته باشد (Supersession/Cross-reference) که امروز مدل‌سازی نشده است (طرح مرحله‌ی بعد در سند).
/// هیچ ادغام خودکاری وجود ندارد؛ در ابهام فقط فهرست کاندیدها برمی‌گردد.
/// </summary>
public class PartCatalogueDuplicateService : IPartCatalogueDuplicateService
{
    private const int MaxTake = 50;
    private readonly PEMSDbContext _ctx;

    public PartCatalogueDuplicateService(PEMSDbContext ctx) => _ctx = ctx;

    private IQueryable<PartCatalogueRequest> ApprovedParts(int? excludeRequestId) =>
        _ctx.PartCatalogueRequests.AsNoTracking().Where(r =>
            r.IsActive && r.Status == PartCatalogueStatuses.Completed && r.AssignedPartNumber != null
            && (excludeRequestId == null || r.PartCatalogueRequestId != excludeRequestId));

    private sealed record RequestRow(
        int Id, string PartNumber, string? ManufacturerName, string? Mpn, string? SupplierPn,
        string? OldMpn, string? Description, string? Unit = null);

    private sealed record MaterialRow(int Id, string Code, string? ManufacturerName, string? Mpn, string? Description, string? Unit = null);

    // ------------------------------------------------------------------ قطعه‌ی ثبت‌شده با شماره‌ی دقیق (SCRUM-428)

    public async Task<PcExistingPartHit?> FindRegisteredPartAsync(string? partNumber)
    {
        var qn = PartCatalogueRules.NormalizePartNumber(partNumber?.Trim() ?? "");
        if (qn.Length == 0) return null;

        // شماره‌ی قطعه کوتاه و نرمال است؛ نامزدها با همان عبارت Replace سمت SQL محدود و در حافظه دقیق مقایسه می‌شوند
        var request = (await ApprovedParts(null)
                .Where(r => r.AssignedPartNumber!.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == qn)
                .OrderByDescending(r => r.CompletedDate)
                .Select(r => new RequestRow(
                    r.PartCatalogueRequestId, r.AssignedPartNumber!,
                    r.Manufacturer != null ? r.Manufacturer.Name : r.NewManufacturerName,
                    r.ManufacturerPartNumber, r.SupplierPartNumber, r.OldManufacturerPartNumber,
                    r.Attributes.Where(a => a.Name == PartCatalogueRequestAttribute.FreeFormatText).Select(a => a.Value).FirstOrDefault(),
                    r.UnitOfMeasure != null ? r.UnitOfMeasure.Code : null))
                .Take(5).ToListAsync())
            .FirstOrDefault(r => PartCatalogueRules.NormalizePartNumber(r.PartNumber) == qn);
        if (request != null)
            return new PcExistingPartHit
            {
                Source = "CatalogueRequest", SourceId = request.Id, PartNumber = request.PartNumber, Description = request.Description?.Trim(),
                ManufacturerName = request.ManufacturerName, ManufacturerPartNumber = request.Mpn, SupplierPartNumber = request.SupplierPn,
                Unit = request.Unit, MatchedOn = "PartNumber", MatchKind = "Exact"
            };

        var material = (await _ctx.MaterialItems.AsNoTracking()
                .Where(m => m.IsActive && m.MaterialCode.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == qn)
                .Select(m => new MaterialRow(m.MaterialItemId, m.MaterialCode, m.Manufacturer, m.ManufacturerPartNumber, m.Description, m.Uom != null ? m.Uom.Code : null))
                .Take(5).ToListAsync())
            .FirstOrDefault(m => PartCatalogueRules.NormalizePartNumber(m.Code) == qn);
        return material == null ? null : new PcExistingPartHit
        {
            Source = "MaterialItem", SourceId = material.Id, PartNumber = material.Code, Description = material.Description,
            ManufacturerName = material.ManufacturerName, ManufacturerPartNumber = material.Mpn, Unit = material.Unit,
            MatchedOn = "PartNumber", MatchKind = "Exact"
        };
    }

    // ------------------------------------------------------------------ جستجوی آزاد

    public async Task<List<PcExistingPartHit>> SearchExistingPartsAsync(string? q, int take = 20)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length < 2) return new List<PcExistingPartHit>();
        take = Math.Clamp(take, 1, MaxTake);
        var fetch = take * 3;

        var qn = PartCatalogueRules.NormalizePartNumber(q);
        var qu = q.ToUpper();
        var useId = qn.Length >= 2;

        var requestRows = await ApprovedParts(null)
            .Where(r =>
                (useId && (
                    r.AssignedPartNumber!.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn)
                    || (r.ManufacturerPartNumber != null && r.ManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn))
                    || (r.SupplierPartNumber != null && r.SupplierPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn))
                    || (r.OldManufacturerPartNumber != null && r.OldManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn))))
                || r.Attributes.Any(a => a.Name == PartCatalogueRequestAttribute.FreeFormatText && a.Value != null && a.Value.ToUpper().Contains(qu)))
            .OrderByDescending(r => r.CompletedDate)
            .Take(fetch)
            .Select(r => new RequestRow(
                r.PartCatalogueRequestId, r.AssignedPartNumber!,
                r.Manufacturer != null ? r.Manufacturer.Name : r.NewManufacturerName,
                r.ManufacturerPartNumber, r.SupplierPartNumber, r.OldManufacturerPartNumber,
                r.Attributes.Where(a => a.Name == PartCatalogueRequestAttribute.FreeFormatText).Select(a => a.Value).FirstOrDefault(),
                r.UnitOfMeasure != null ? r.UnitOfMeasure.Code : null))
            .ToListAsync();

        var materialRows = await _ctx.MaterialItems.AsNoTracking()
            .Where(m => m.IsActive && (
                (useId && (
                    m.MaterialCode.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn)
                    || (m.ManufacturerPartNumber != null && m.ManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper().Contains(qn))))
                || m.Description.ToUpper().Contains(qu)
                || (m.LongDescription != null && m.LongDescription.ToUpper().Contains(qu))))
            .OrderBy(m => m.MaterialCode)
            .Take(fetch)
            .Select(m => new MaterialRow(m.MaterialItemId, m.MaterialCode, m.Manufacturer, m.ManufacturerPartNumber, m.Description, m.Uom != null ? m.Uom.Code : null))
            .ToListAsync();

        var hits = new List<PcExistingPartHit>();
        foreach (var r in requestRows)
        {
            var (on, kind) = Classify(qn, qu, useId,
                ("PartNumber", r.PartNumber), ("ManufacturerPartNumber", r.Mpn), ("SupplierPartNumber", r.SupplierPn),
                ("OldManufacturerPartNumber", r.OldMpn), ("Description", r.Description));
            hits.Add(new PcExistingPartHit
            {
                Source = "CatalogueRequest", SourceId = r.Id, PartNumber = r.PartNumber, Description = r.Description?.Trim(),
                ManufacturerName = r.ManufacturerName, ManufacturerPartNumber = r.Mpn, SupplierPartNumber = r.SupplierPn,
                Unit = r.Unit, MatchedOn = on, MatchKind = kind
            });
        }

        var requestCodes = requestRows.Select(r => r.PartNumber.Trim().ToUpperInvariant()).ToHashSet();
        foreach (var m in materialRows)
        {
            // همان قطعه از دو منبع: Material Master که هم‌کد (MaterialCode = AssignedPartNumber، همان قاعده‌ی ResolveExistingPartAsync) درخواست تکمیل‌شده است دوباره شمرده نمی‌شود
            if (requestCodes.Contains(m.Code.Trim().ToUpperInvariant())) continue;
            var (on, kind) = Classify(qn, qu, useId,
                ("PartNumber", m.Code), ("ManufacturerPartNumber", m.Mpn), ("Description", m.Description));
            hits.Add(new PcExistingPartHit
            {
                Source = "MaterialItem", SourceId = m.Id, PartNumber = m.Code, Description = m.Description,
                ManufacturerName = m.ManufacturerName, ManufacturerPartNumber = m.Mpn, Unit = m.Unit, MatchedOn = on, MatchKind = kind
            });
        }

        return hits
            .OrderBy(h => h.MatchKind == "Exact" ? 0 : 1)
            .ThenBy(h => h.PartNumber, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static (string MatchedOn, string Kind) Classify(string qn, string qu, bool useId, params (string Field, string? Value)[] fields)
    {
        if (useId)
        {
            foreach (var (field, value) in fields)
            {
                if (field == "Description" || string.IsNullOrWhiteSpace(value)) continue;
                if (PartCatalogueRules.NormalizePartNumber(value) == qn) return (field, "Exact");
            }
            foreach (var (field, value) in fields)
            {
                if (field == "Description" || string.IsNullOrWhiteSpace(value)) continue;
                if (PartCatalogueRules.NormalizePartNumber(value).Contains(qn, StringComparison.Ordinal)) return (field, "Partial");
            }
        }
        return ("Description", "Partial");
    }

    // ------------------------------------------------------------------ کاندیدهای تکراری

    public async Task<PcDuplicateResult> FindDuplicateCandidatesAsync(PcDuplicateProbe probe)
    {
        var result = new PcDuplicateResult();
        var mpn = PartCatalogueRules.IsMatchablePartNumber(probe.ManufacturerPartNumber)
            ? PartCatalogueRules.NormalizePartNumber(probe.ManufacturerPartNumber) : null;
        var spn = PartCatalogueRules.IsMatchablePartNumber(probe.SupplierPartNumber)
            ? PartCatalogueRules.NormalizePartNumber(probe.SupplierPartNumber) : null;
        if (mpn == null && spn == null) return result;

        var probeMfr = probe.ManufacturerId.HasValue
            ? await _ctx.PartCatalogueManufacturers.AsNoTracking().Where(m => m.ManufacturerId == probe.ManufacturerId)
                .Select(m => m.Name).FirstOrDefaultAsync()
            : probe.NewManufacturerName;
        var probeMfrKey = PartCatalogueRules.NormalizeName(probeMfr);

        var requestRows = await ApprovedParts(probe.ExcludeRequestId)
            .Where(r =>
                (mpn != null && (
                    (r.ManufacturerPartNumber != null && r.ManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == mpn)
                    || (r.OldManufacturerPartNumber != null && r.OldManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == mpn)))
                || (spn != null && r.SupplierPartNumber != null
                    && r.SupplierPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == spn))
            .Take(100)
            .Select(r => new RequestRow(
                r.PartCatalogueRequestId, r.AssignedPartNumber!,
                r.Manufacturer != null ? r.Manufacturer.Name : r.NewManufacturerName,
                r.ManufacturerPartNumber, r.SupplierPartNumber, r.OldManufacturerPartNumber,
                r.Attributes.Where(a => a.Name == PartCatalogueRequestAttribute.FreeFormatText).Select(a => a.Value).FirstOrDefault(),
                r.UnitOfMeasure != null ? r.UnitOfMeasure.Code : null))
            .ToListAsync();

        var materialRows = mpn == null ? new List<MaterialRow>() : await _ctx.MaterialItems.AsNoTracking()
            .Where(m => m.IsActive && m.ManufacturerPartNumber != null
                && m.ManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == mpn)
            .Take(100)
            .Select(m => new MaterialRow(m.MaterialItemId, m.MaterialCode, m.Manufacturer, m.ManufacturerPartNumber, m.Description, m.Uom != null ? m.Uom.Code : null))
            .ToListAsync();

        string MpnKind(string? hitMfr)
        {
            var hitKey = PartCatalogueRules.NormalizeName(hitMfr);
            if (probeMfrKey.Length == 0 || hitKey.Length == 0) return "SameMpnManufacturerUnknown";
            return hitKey == probeMfrKey ? "SameMpnSameManufacturer" : "SameMpnOtherManufacturer";
        }

        foreach (var r in requestRows)
        {
            var mpnHit = mpn != null && (
                PartCatalogueRules.NormalizePartNumber(r.Mpn) == mpn || PartCatalogueRules.NormalizePartNumber(r.OldMpn) == mpn);
            var spnHit = spn != null && PartCatalogueRules.NormalizePartNumber(r.SupplierPn) == spn;
            if (!mpnHit && !spnHit) continue;

            result.Candidates.Add(new PcExistingPartHit
            {
                Source = "CatalogueRequest", SourceId = r.Id, PartNumber = r.PartNumber, Description = r.Description?.Trim(),
                ManufacturerName = r.ManufacturerName, ManufacturerPartNumber = r.Mpn, SupplierPartNumber = r.SupplierPn,
                MatchedOn = mpnHit ? (PartCatalogueRules.NormalizePartNumber(r.Mpn) == mpn ? "ManufacturerPartNumber" : "OldManufacturerPartNumber") : "SupplierPartNumber",
                MatchKind = mpnHit ? MpnKind(r.ManufacturerName) : "SameSupplierPartNumber"
            });
        }

        var requestCodes = requestRows.Select(r => r.PartNumber.Trim().ToUpperInvariant()).ToHashSet();
        foreach (var m in materialRows)
        {
            if (requestCodes.Contains(m.Code.Trim().ToUpperInvariant())) continue;
            result.Candidates.Add(new PcExistingPartHit
            {
                Source = "MaterialItem", SourceId = m.Id, PartNumber = m.Code, Description = m.Description,
                ManufacturerName = m.ManufacturerName, ManufacturerPartNumber = m.Mpn,
                MatchedOn = "ManufacturerPartNumber", MatchKind = MpnKind(m.ManufacturerName)
            });
        }

        // ابهام: بیش از یک «قطعه‌ی متمایز» با MPN و سازنده‌ی یکسان - هیچ انتخاب/ادغام خودکاری انجام نمی‌شود
        var strongParts = result.Candidates
            .Where(c => c.MatchKind == "SameMpnSameManufacturer")
            .Select(c => c.PartNumber.Trim().ToUpperInvariant()).Distinct().Count();
        result.IsAmbiguous = strongParts > 1;

        // درخواست‌های در جریان با همان MPN: فقط شمارش (محتوا ممکن است متعلق به کاربر دیگری باشد)
        if (mpn != null)
        {
            result.InFlightRequestsWithSameMpn = await _ctx.PartCatalogueRequests.AsNoTracking().CountAsync(r =>
                r.IsActive && r.Status != PartCatalogueStatuses.Completed && r.Status != PartCatalogueStatuses.Cancelled
                && (probe.ExcludeRequestId == null || r.PartCatalogueRequestId != probe.ExcludeRequestId)
                && r.ManufacturerPartNumber != null
                && r.ManufacturerPartNumber.Replace(" ", "").Replace("-", "").Replace("/", "").Replace(".", "").Replace("_", "").ToUpper() == mpn);
        }

        result.Candidates = result.Candidates
            .OrderBy(c => c.MatchKind == "SameMpnSameManufacturer" ? 0 : c.MatchKind == "SameMpnManufacturerUnknown" ? 1 : c.MatchKind == "SameMpnOtherManufacturer" ? 2 : 3)
            .ThenBy(c => c.PartNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result;
    }
}
