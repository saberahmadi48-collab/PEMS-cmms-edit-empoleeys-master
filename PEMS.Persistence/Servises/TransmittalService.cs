using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Exceptions;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class TransmittalService : ITransmittalService
{
    private readonly PEMSDbContext _context;

    public TransmittalService(PEMSDbContext context)
    {
        _context = context;
    }

    // طبق «PEMS Phase 2A — EDMS Foundation»: Query هدفمند سمت SQL (نه GetAllAsync + فیلتر در
    // حافظه) - فقط ستون‌های لازم برای یک ردیف خلاصه در تب «Related Transmittals»
    public async Task<List<RelatedTransmittalDto>> GetForDocumentAsync(int documentId)
    {
        return await _context.TransmittalItems
            .Where(x => x.DocumentId == documentId && x.Transmittal != null && x.Transmittal.IsActive)
            .Select(x => new RelatedTransmittalDto
            {
                TransmittalId = x.Transmittal!.TransmittalId,
                TransmittalNo = x.Transmittal.TransmittalNo,
                Status = x.Transmittal.Status,
                TransmittalDate = x.Transmittal.TransmittalDate,
                SentDate = x.Transmittal.SentDate,
                Purpose = x.Transmittal.Purpose,
                RevisionNoAtTransmittal = x.RevisionNo
            })
            .OrderByDescending(x => x.TransmittalDate)
            .ToListAsync();
    }

    public async Task<List<TransmittalDto>> GetAllAsync()
    {
        var transmittals = await _context.Transmittals
            .Include(x => x.Project)
            .Include(x => x.SupersedesTransmittal)
            .OrderByDescending(x => x.TransmittalDate)
            .ToListAsync();

        var supersededByLookup = await _context.Transmittals
            .Where(x => x.SupersedesTransmittalId != null)
            .ToDictionaryAsync(x => x.SupersedesTransmittalId!.Value);

        return transmittals.Select(x => MapHeader(x, supersededByLookup)).ToList();
    }

    // طبق «PEMS — Global Grid Hardening» P1: جایگزین بارگذاری کامل GetAllAsync برای Index -
    // شمارش/مرتب‌سازی/صفحه‌بندی/Projection همه در SQL. ItemsCount و SupersededBy* با Subquery
    // همبسته (نه GroupBy/ToDictionary روی کل جدول) محاسبه می‌شوند. طبق رفتار فعلی GetAllAsync،
    // عمداً فیلتر IsActive اعمال نشده - این جدول امروز هم بدون آن فیلتر خوانده می‌شود.
    public async Task<PagedResult<TransmittalDto>> GetPagedAsync(int page, int pageSize)
    {
        var query = _context.Transmittals.AsNoTracking().AsQueryable();

        var totalCount = await query.CountAsync();

        var rows = await query
            .OrderByDescending(x => x.TransmittalDate)
            .ThenByDescending(x => x.TransmittalId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.TransmittalId,
                x.TransmittalNo,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.ProjectName : "",
                x.From,
                x.To,
                x.Purpose,
                x.Description,
                x.Status,
                x.TransmittalDate,
                x.SentDate,
                ItemsCount = x.Items.Count,
                x.ResponseRequired,
                x.ResponseDueDate,
                x.ResponseReceivedDate,
                x.ResponseNote,
                x.IsResponseOverdue,
                x.IsIssued,
                x.CancelledDate,
                x.CancelledReason,
                x.SupersedesTransmittalId,
                SupersedesTransmittalNo = x.SupersedesTransmittal != null ? x.SupersedesTransmittal.TransmittalNo : null,
                // طبق بررسی EF InMemory (تست‌های Wave C1): دو Subquery همبسته‌ی جدا روی همون
                // جدول (یکی برای Id، یکی برای No) زیر EF InMemory هر دو ردیف را خالی برمی‌گرداند
                // (به‌احتمال زیاد یک محدودیت شناخته‌شده‌ی خودِ Provider، نه SQL Server واقعی) -
                // یک Subquery ترکیبی که هر دو ستون را با هم برمی‌گرداند این مشکل را کاملاً حل
                // می‌کند و تعداد Round-trip را هم کم می‌کند.
                SupersededBy = _context.Transmittals
                    .Where(y => y.SupersedesTransmittalId == x.TransmittalId)
                    .Select(y => new { y.TransmittalId, y.TransmittalNo })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var items = rows.Select(x => new TransmittalDto
        {
            TransmittalId = x.TransmittalId,
            TransmittalNo = x.TransmittalNo,
            ProjectId = x.ProjectId,
            ProjectName = x.ProjectName,
            From = x.From,
            To = x.To,
            Purpose = x.Purpose,
            Description = x.Description,
            Status = x.Status,
            TransmittalDate = x.TransmittalDate,
            SentDate = x.SentDate,
            ItemsCount = x.ItemsCount,
            ResponseRequired = x.ResponseRequired,
            ResponseDueDate = x.ResponseDueDate,
            ResponseReceivedDate = x.ResponseReceivedDate,
            ResponseNote = x.ResponseNote,
            IsResponseOverdue = x.IsResponseOverdue,
            IsIssued = x.IsIssued,
            CancelledDate = x.CancelledDate,
            CancelledReason = x.CancelledReason,
            SupersedesTransmittalId = x.SupersedesTransmittalId,
            SupersedesTransmittalNo = x.SupersedesTransmittalNo,
            SupersededByTransmittalId = x.SupersededBy?.TransmittalId,
            SupersededByTransmittalNo = x.SupersededBy?.TransmittalNo
        }).ToList();

        return new PagedResult<TransmittalDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<TransmittalDto?> GetByIdAsync(int id)
    {
        var t = await _context.Transmittals
            .Include(x => x.Project)
            .Include(x => x.SupersedesTransmittal)
            .Include(x => x.Items).ThenInclude(i => i.Document)
            .Include(x => x.Recipients)
            .FirstOrDefaultAsync(x => x.TransmittalId == id);

        if (t == null) return null;

        var supersededBy = await _context.Transmittals
            .Where(x => x.SupersedesTransmittalId == id)
            .FirstOrDefaultAsync();

        var dto = MapHeader(t, supersededBy != null
            ? new Dictionary<int, Transmittal> { [id] = supersededBy }
            : new Dictionary<int, Transmittal>());

        dto.Items = t.Items.Select(i => new TransmittalItemDto
        {
            TransmittalItemId = i.TransmittalItemId,
            DocumentId = i.DocumentId,
            DocumentNo = i.Document?.DocumentNo ?? "",
            DocumentTitle = i.Document?.Title ?? "",
            RevisionNo = i.RevisionNo,
            Remarks = i.Remarks,
            DocumentRevisionId = i.DocumentRevisionId,
            SnapshotFileName = i.SnapshotFileName,
            SnapshotFileHash = i.SnapshotFileHash,
            SnapshotFileSize = i.SnapshotFileSize
        }).ToList();

        var acknowledgerIds = t.Recipients.Where(r => r.AcknowledgedById.HasValue).Select(r => r.AcknowledgedById!.Value).Distinct().ToList();
        var acknowledgerNames = acknowledgerIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Employees.Where(e => acknowledgerIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FirstName + " " + e.LastName);

        dto.Recipients = t.Recipients.Where(r => r.IsActive).Select(r => new TransmittalRecipientDto
        {
            TransmittalRecipientId = r.TransmittalRecipientId,
            Name = r.Name,
            RecipientType = r.RecipientType,
            Organization = r.Organization,
            ResponseRequired = r.ResponseRequired,
            AcknowledgedAt = r.AcknowledgedAt,
            AcknowledgedByName = r.AcknowledgedById.HasValue ? acknowledgerNames.GetValueOrDefault(r.AcknowledgedById.Value) : null,
            AcknowledgementNote = r.AcknowledgementNote
        }).ToList();

        return dto;
    }

    public async Task<int> CreateAsync(string transmittalNo, int projectId, string from, string to,
        string purpose, string? description, List<int> documentIds,
        bool responseRequired, List<TransmittalRecipientInput> recipients)
    {
        if (string.IsNullOrWhiteSpace(transmittalNo))
            throw new PemsException(PemsErrorKind.Validation, "transmittal-code-required", "Transmittal code is required.");

        if (await _context.Transmittals.AnyAsync(x => x.TransmittalNo == transmittalNo))
            throw new ConflictException("transmittal-code-duplicate", $"Transmittal code '{transmittalNo}' already exists.");

        // طبق بخش M (تعامل Hold با Transmittal): مدرکی که هنوز Hold است نباید توزیع رسمی شود -
        // این بررسی در لحظه‌ی Create (نه فقط Send) انجام می‌شود چون کاربر باید همین‌جا بفهمد.
        var heldDocs = await _context.Documents
            .Where(d => documentIds.Contains(d.DocumentId) && d.IsOnHold)
            .Select(d => d.DocumentNo)
            .ToListAsync();

        if (heldDocs.Count > 0)
            throw new ConflictException("transmittal-documents-held",
                $"Documents on hold cannot be transmitted: {string.Join(", ", heldDocs)}");

        var transmittal = new Transmittal
        {
            TransmittalNo = transmittalNo,
            ProjectId = projectId,
            From = from,
            To = to,
            Purpose = purpose,
            Description = description,
            Status = "Draft",
            ResponseRequired = responseRequired,
            TransmittalDate = DateTime.Now,
            CreatedDate = DateTime.Now
        };

        foreach (var docId in documentIds.Distinct())
        {
            var doc = await _context.Documents.FirstOrDefaultAsync(x => x.DocumentId == docId);
            var revisionNo = doc?.RevisionNo ?? "00";

            // طبق یافته‌ی G-21: هویت رویژن/فایل رسمی همین‌جا (لحظه‌ی افزودن آیتم، دقیقاً همان
            // لحظه‌ای که RevisionNo هم ثبت می‌شود) Snapshot می‌شود - نه دیرتر، نه در لحظه‌ی Send.
            // اگر مدرک هنوز هیچ DocumentRevision واقعی ندارد (مدارک قدیمی‌تر)، این فیلدها null
            // می‌مانند - حدس زده نمی‌شوند.
            var revision = await _context.DocumentRevisions
                .Where(r => r.DocumentId == docId && r.RevisionNo == revisionNo)
                .OrderByDescending(r => r.RevisionId)
                .FirstOrDefaultAsync();

            DocumentAttachment? officialAttachment = null;
            if (revision != null)
            {
                officialAttachment = await _context.DocumentAttachments
                    .Where(a => a.DocumentRevisionId == revision.RevisionId && a.IsOfficial && a.IsActive)
                    .OrderByDescending(a => a.UploadDate)
                    .FirstOrDefaultAsync();
            }

            transmittal.Items.Add(new TransmittalItem
            {
                DocumentId = docId,
                RevisionNo = revisionNo,
                DocumentRevisionId = revision?.RevisionId,
                DocumentAttachmentId = officialAttachment?.AttachmentId,
                SnapshotFileName = officialAttachment?.FileName,
                SnapshotFileHash = officialAttachment?.FileHash,
                SnapshotFileSize = officialAttachment?.FileSize
            });
        }

        foreach (var r in recipients.Where(r => !string.IsNullOrWhiteSpace(r.Name)))
        {
            transmittal.Recipients.Add(new TransmittalRecipient
            {
                Name = r.Name.Trim(),
                RecipientType = string.IsNullOrWhiteSpace(r.RecipientType) ? "External" : r.RecipientType,
                Organization = r.Organization,
                ResponseRequired = r.ResponseRequired,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        // To آزاد برای سازگاری نمایشی با کدهای قدیمی، از روی گیرندگان ساختاریافته خلاصه می‌شود -
        // اگر گیرنده‌ای وارد نشده (فرم قدیمی)، همان مقدار دستی to حفظ می‌شود
        if (transmittal.Recipients.Count > 0)
            transmittal.To = string.Join("; ", transmittal.Recipients.Select(r => r.Name));

        await _context.Transmittals.AddAsync(transmittal);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // طبق بخش G (بند ۲۲ - Numbering Regression): پنجره‌ی Race بین AnyAsync بالا و این
            // SaveChanges - اگر دو درخواست هم‌زمان با همین شماره برسند، ایندکس Unique سطح
            // دیتابیس (TransmittalConfiguration) دومی را رد می‌کند؛ این‌جا فقط پیام دوستانه می‌دهیم.
            throw new ConflictException("transmittal-code-duplicate", $"Transmittal code '{transmittalNo}' already exists.", ex);
        }

        return transmittal.TransmittalId;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    public async Task MarkSentAsync(int id, int responseDueWorkingDays = 10)
    {
        var t = await _context.Transmittals.Include(x => x.Items).FirstOrDefaultAsync(x => x.TransmittalId == id);
        if (t == null) throw new InvalidOperationException("ترانسمیتال یافت نشد.");

        if (t.Status != "Draft")
            throw new InvalidOperationException($"ترانسمیتال {t.TransmittalNo} از قبل صادر شده ({t.Status}) - قابل صدور دوباره نیست.");

        if (!t.Items.Any())
            throw new InvalidOperationException("ترانسمیتال بدون مدرک قابل صدور نیست.");

        if (!t.Recipients.Any() && string.IsNullOrWhiteSpace(t.To))
            throw new InvalidOperationException("ترانسمیتال باید حداقل یک گیرنده داشته باشد.");

        // طبق بخش ۵ (Response Required Rules): نباید حالت ناسازگار ResponseRequired=true و
        // ResponseDueDate=null اجازه داده شود - این‌جا دقیقاً همان لحظه‌ای است که محاسبه می‌شود
        if (t.ResponseRequired)
            t.ResponseDueDate = WorkingDayCalculator.AddWorkingDays(DateTime.Now, responseDueWorkingDays);

        t.Status = "Sent";
        t.SentDate = DateTime.Now;

        await SaveWithConcurrencyCheckAsync();
    }

    public async Task CancelAsync(int id, string reason, int? actorEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("دلیل لغو (Cancel Reason) الزامی است.");

        var t = await _context.Transmittals.FirstOrDefaultAsync(x => x.TransmittalId == id);
        if (t == null) throw new InvalidOperationException("ترانسمیتال یافت نشد.");

        if (t.Status == "Cancelled")
            throw new InvalidOperationException($"ترانسمیتال {t.TransmittalNo} از قبل لغو شده است.");

        // طبق بخش ۸: Cancel یک وضعیت جدید است، نه پاک‌کردن/بازنویسی رکورد صادرشده - تاریخچه‌ی
        // Sent/SentDate/گیرندگان/مدارک دست‌نخورده می‌ماند
        t.Status = "Cancelled";
        t.CancelledDate = DateTime.Now;
        t.CancelledReason = reason;
        t.CancelledById = actorEmployeeId;

        await SaveWithConcurrencyCheckAsync();
    }

    public async Task<int> ReissueAsync(int id, int? actorEmployeeId)
    {
        var original = await _context.Transmittals
            .Include(x => x.Items)
            .Include(x => x.Recipients)
            .FirstOrDefaultAsync(x => x.TransmittalId == id);

        if (original == null) throw new InvalidOperationException("ترانسمیتال یافت نشد.");

        if (original.Status != "Cancelled")
            throw new InvalidOperationException("فقط ترانسمیتال لغوشده قابل صدور مجدد (Reissue) است.");

        var reissued = new Transmittal
        {
            // طبق بخش ۸: باید شماره‌ی جدید بگیرد (نه بازاستفاده از شماره‌ی رسمی قدیمی) - کاربر
            // آن را در فرم ویرایش/تکمیل می‌کند، این‌جا فقط یک Placeholder موقت تا ذخیره شود
            TransmittalNo = $"{original.TransmittalNo}-R{DateTime.Now:HHmmss}",
            ProjectId = original.ProjectId,
            From = original.From,
            To = original.To,
            Purpose = original.Purpose,
            Description = original.Description,
            Status = "Draft",
            ResponseRequired = original.ResponseRequired,
            TransmittalDate = DateTime.Now,
            CreatedDate = DateTime.Now,
            SupersedesTransmittalId = original.TransmittalId
        };

        foreach (var item in original.Items)
        {
            // طبق یافته‌ی G-21: دقیقاً همان الگوی موجود RevisionNo - کپی عیناً، نه Resolve دوباره.
            // یک Reissue باید همان چیزی را که قبلاً Cancel شده دوباره پیشنهاد بدهد، نه نسخه‌ی
            // تازه‌ی رویژن/فایل که ممکن است از وقت Cancel به بعد تغییر کرده باشد.
            reissued.Items.Add(new TransmittalItem
            {
                DocumentId = item.DocumentId,
                RevisionNo = item.RevisionNo,
                Remarks = item.Remarks,
                DocumentRevisionId = item.DocumentRevisionId,
                DocumentAttachmentId = item.DocumentAttachmentId,
                SnapshotFileName = item.SnapshotFileName,
                SnapshotFileHash = item.SnapshotFileHash,
                SnapshotFileSize = item.SnapshotFileSize
            });
        }

        foreach (var r in original.Recipients.Where(r => r.IsActive))
        {
            reissued.Recipients.Add(new TransmittalRecipient
            {
                Name = r.Name,
                RecipientType = r.RecipientType,
                Organization = r.Organization,
                ResponseRequired = r.ResponseRequired,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await _context.Transmittals.AddAsync(reissued);
        await SaveWithConcurrencyCheckAsync();

        return reissued.TransmittalId;
    }

    public async Task RecordResponseAsync(int id, string? responseNote)
    {
        var t = await _context.Transmittals.FirstOrDefaultAsync(x => x.TransmittalId == id);
        if (t == null) throw new InvalidOperationException("ترانسمیتال یافت نشد.");

        if (!t.ResponseRequired)
            throw new InvalidOperationException("این ترانسمیتال اصلاً پاسخ لازم ندارد.");

        if (!t.IsIssued)
            throw new InvalidOperationException("پاسخ فقط برای ترانسمیتال صادرشده قابل ثبت است.");

        t.ResponseReceivedDate = DateTime.Now;
        t.ResponseNote = responseNote;

        await SaveWithConcurrencyCheckAsync();
    }

    public async Task AcknowledgeAsync(int transmittalRecipientId, string? note, int? actorEmployeeId)
    {
        var recipient = await _context.TransmittalRecipients
            .Include(r => r.Transmittal)
            .FirstOrDefaultAsync(r => r.TransmittalRecipientId == transmittalRecipientId);

        if (recipient == null) throw new InvalidOperationException("گیرنده یافت نشد.");
        if (recipient.Transmittal == null) throw new InvalidOperationException("ترانسمیتال یافت نشد.");

        if (!recipient.Transmittal.IsIssued)
            throw new InvalidOperationException("فقط گیرندگان یک ترانسمیتال صادرشده (Sent) قابل تایید دریافت هستند.");

        if (recipient.AcknowledgedAt.HasValue)
            throw new InvalidOperationException($"گیرنده «{recipient.Name}» قبلاً تایید دریافت کرده است ({recipient.AcknowledgedAt:yyyy-MM-dd}).");

        recipient.AcknowledgedAt = DateTime.Now;
        recipient.AcknowledgedById = actorEmployeeId;
        recipient.AcknowledgementNote = note;

        // طبق بند ۱۵ (Partial Acknowledgement): فقط وقتی همه‌ی گیرندگان فعال تایید کرده باشند،
        // خودِ Transmittal هم Acknowledged می‌شود - هرگز به‌خاطر یک گیرنده. "Acknowledged" از قبل
        // یک مقدار معتبر Status بود (Transmittal.cs) اما هیچ کدی تاکنون آن را واقعاً تنظیم نمی‌کرد.
        var activeRecipients = await _context.TransmittalRecipients
            .Where(r => r.TransmittalId == recipient.TransmittalId && r.IsActive)
            .ToListAsync();

        if (activeRecipients.Count > 0 && activeRecipients.All(r => r.AcknowledgedAt.HasValue) && recipient.Transmittal.Status == "Sent")
            recipient.Transmittal.Status = "Acknowledged";

        await SaveWithConcurrencyCheckAsync();
    }

    private async Task SaveWithConcurrencyCheckAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "این ترانسمیتال هم‌زمان توسط کاربر دیگری تغییر کرده است. لطفاً صفحه را رفرش کنید و دوباره تلاش کنید.");
        }
    }

    private static TransmittalDto MapHeader(Transmittal x, Dictionary<int, Transmittal> supersededByLookup)
    {
        supersededByLookup.TryGetValue(x.TransmittalId, out var supersededBy);

        return new TransmittalDto
        {
            TransmittalId = x.TransmittalId,
            TransmittalNo = x.TransmittalNo,
            ProjectId = x.ProjectId,
            ProjectName = x.Project?.ProjectName ?? "",
            From = x.From,
            To = x.To,
            Purpose = x.Purpose,
            Description = x.Description,
            Status = x.Status,
            TransmittalDate = x.TransmittalDate,
            SentDate = x.SentDate,
            ItemsCount = x.Items.Count,
            ResponseRequired = x.ResponseRequired,
            ResponseDueDate = x.ResponseDueDate,
            ResponseReceivedDate = x.ResponseReceivedDate,
            ResponseNote = x.ResponseNote,
            IsResponseOverdue = x.IsResponseOverdue,
            IsIssued = x.IsIssued,
            CancelledDate = x.CancelledDate,
            CancelledReason = x.CancelledReason,
            SupersedesTransmittalId = x.SupersedesTransmittalId,
            SupersedesTransmittalNo = x.SupersedesTransmittal?.TransmittalNo,
            SupersededByTransmittalId = supersededBy?.TransmittalId,
            SupersededByTransmittalNo = supersededBy?.TransmittalNo
        };
    }
}
