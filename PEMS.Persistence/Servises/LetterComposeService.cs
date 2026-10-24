using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// نوشتن نامه‌ی رسمی داخلی/خارجی. قواعد خالص در LetterComposeRules، دسترسی در LetterAccessRules و گردش‌کار تایید/ارسال در LetterService است؛
/// این‌جا فقط ساخت/ویرایش پیش‌نویس (با بلوک فرستنده‌ی Snapshot، گیرندگان To/Cc/Bcc، زنجیره‌ی تایید و تاریخچه‌ی نسخه) انجام می‌شود.
/// </summary>
public class LetterComposeService : ILetterComposeService
{
    private static readonly string[] EditableStatuses = { "Draft", "ReturnedForRevision" };
    private static readonly string[] DoneTaskStatuses = { "Done", "Completed", "Closed", "Cancelled" };
    private const string DirectoryCacheKey = "letter:employee-directory";

    private readonly PEMSDbContext _context;
    private readonly IRichTextSanitizerService _sanitizer;
    private readonly ILetterNumberingService _numbering;
    private readonly ICacheService _cache;

    public LetterComposeService(PEMSDbContext context, IRichTextSanitizerService sanitizer, ILetterNumberingService numbering, ICacheService cache)
    {
        _context = context;
        _sanitizer = sanitizer;
        _numbering = numbering;
        _cache = cache;
    }

    // ------------------------------------------------------------------ helpers

    private static string FullName(Employee? e) => e == null ? "" : (e.FirstName + " " + e.LastName).Trim();
    private static string? TitleOf(Employee? e) => e == null ? null : (e.PositionRef?.Title ?? e.Position);
    private static bool IsExternal(Employee e) => !string.Equals(e.PersonnelType, "Internal", StringComparison.OrdinalIgnoreCase) || e.VendorId.HasValue;

    private sealed class DirectoryCheck
    {
        public List<LetterComposeIssue> Errors { get; } = new();
        public List<LetterComposeIssue> Warnings { get; } = new();
        public Dictionary<int, Employee> Employees { get; set; } = new();
    }

    /// <summary>وجود/فعال‌بودن گیرندگان و تاییدکنندگان + هشدارهای «خارج از سازمان» و «کار/مهلت معوق».</summary>
    private async Task<DirectoryCheck> DirectoryChecksAsync(LetterComposeInput input)
    {
        var check = new DirectoryCheck();
        var ids = LetterComposeRules.AllRecipientIds(input).Distinct().ToList();

        if (ids.Count > 0)
        {
            check.Employees = await _context.Employees.AsNoTracking().Include(e => e.Department).Where(e => ids.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId);

            foreach (var id in ids)
            {
                if (!check.Employees.TryGetValue(id, out var e)) check.Errors.Add(new("RecipientNotFound", "recipients", id.ToString()));
                else if (!e.IsActive) check.Errors.Add(new("RecipientInactive", "recipients", FullName(e)));
            }

            var externals = check.Employees.Values.Where(e => e.IsActive && IsExternal(e)).Select(FullName).ToList();
            if (externals.Count > 0) check.Warnings.Add(new("ExternalRecipient", "recipients", string.Join(", ", externals)));

            var now = DateTime.Now;
            var activeIds = check.Employees.Values.Where(e => e.IsActive).Select(e => e.EmployeeId).ToList();
            var overdueReferrals = await _context.LetterReferrals.AsNoTracking()
                .Where(r => r.ReferredToEmployeeId != null && activeIds.Contains(r.ReferredToEmployeeId.Value)
                    && r.Status != "Completed" && r.DueDate != null && r.DueDate < now)
                .GroupBy(r => r.ReferredToEmployeeId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync();
            var overdueTasks = await _context.TaskItems.AsNoTracking()
                .Where(t => t.AssignedToEmployeeId != null && activeIds.Contains(t.AssignedToEmployeeId.Value)
                    && t.DueDate != null && t.DueDate < now && t.CompletedDate == null && !DoneTaskStatuses.Contains(t.Status))
                .GroupBy(t => t.AssignedToEmployeeId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync();

            foreach (var id in activeIds)
            {
                var n = (overdueReferrals.FirstOrDefault(x => x.Id == id)?.Count ?? 0) + (overdueTasks.FirstOrDefault(x => x.Id == id)?.Count ?? 0);
                if (n > 0) check.Warnings.Add(new("RecipientPendingWork", "recipients", $"{FullName(check.Employees[id])}: {n}"));
            }
        }

        if (input.ApproverUserIds.Count > 0)
        {
            var allowed = (await LetterDirectoryQueries.ApproverUserIdsAsync(_context)).ToHashSet();
            var users = await _context.Users.AsNoTracking().Where(u => input.ApproverUserIds.Contains(u.Id)).Select(u => new { u.Id, u.IsActive }).ToListAsync();
            foreach (var id in input.ApproverUserIds.Distinct())
            {
                var u = users.FirstOrDefault(x => x.Id == id);
                if (u == null || !u.IsActive || !allowed.Contains(id)) check.Errors.Add(new("ApproverNotAllowed", "approvers", id.ToString()));
            }
        }

        return check;
    }

    // ------------------------------------------------------------------ draft save

    public async Task<LetterComposeResult> SaveDraftAsync(LetterComposeInput input, LetterComposeActor actor, bool autoSave = false)
    {
        var result = new LetterComposeResult();
        result.Errors.AddRange(LetterComposeRules.ValidateStructure(input));
        if (input.ApproverUserIds.Contains(actor.UserId)) result.Errors.Add(new("ApproverIsAuthor", "approvers"));

        var check = await DirectoryChecksAsync(input);
        result.Errors.AddRange(check.Errors);
        result.Warnings.AddRange(check.Warnings);
        result.Warnings.AddRange(LetterComposeRules.StaticWarnings(input, actor.EmployeeId));
        if (result.Errors.Count > 0) return result;

        var employee = actor.EmployeeId.HasValue
            ? await _context.Employees.Include(e => e.PositionRef).Include(e => e.Department).FirstOrDefaultAsync(e => e.EmployeeId == actor.EmployeeId)
            : null;

        // پاسخ: نامه‌ی اصلی باید وجود داشته باشد و پاسخ نباید محرمانگی کمتری از آن داشته باشد (وگرنه پاسخ یک نامه‌ی محرمانه برای همه باز می‌شد)
        var parentId = input.LetterId.HasValue
            ? await _context.Letters.Where(l => l.LetterId == input.LetterId).Select(l => l.ParentLetterId).FirstOrDefaultAsync()
            : input.ParentLetterId;
        Letter? parent = null;
        if (input.IsReferral && !parentId.HasValue) { result.Errors.Add(new("ReferralNeedsParent", "parentLetterId")); return result; }
        if (parentId.HasValue)
        {
            parent = await _context.Letters.FirstOrDefaultAsync(l => l.LetterId == parentId && l.IsActive);
            if (parent == null) { result.Errors.Add(new("ParentNotFound")); return result; }
            if (LetterComposeRules.ConfidentialityRank(input.ConfidentialityLevel) < LetterComposeRules.ConfidentialityRank(parent.ConfidentialityLevel))
            {
                result.Errors.Add(new("ConfidentialityBelowParent", "confidentialityLevel", parent.ConfidentialityLevel));
                return result;
            }
        }

        // طرف مکاتبه (فقط نامه‌ی صادره): باید فعال و موجود باشد؛ پیش از ساختن/تغییر هر ردیفی بررسی می‌شود
        int? partyId = null;
        if (input.LetterType == "Outgoing" && input.CorrespondentPartyId.HasValue)
        {
            partyId = input.CorrespondentPartyId.Value;
            if (!await _context.CorrespondentParties.AnyAsync(p => p.CorrespondentPartyId == partyId && p.IsActive))
            {
                result.Errors.Add(new("PartyNotFound", "correspondentPartyId"));
                return result;
            }
        }

        Letter letter;
        if (!input.LetterId.HasValue)
        {
            var departmentId = input.SenderDepartmentId ?? employee?.DepartmentId;
            string number;
            if (actor.CanManualNumber && !string.IsNullOrWhiteSpace(input.ManualNumber))
            {
                number = input.ManualNumber.Trim();
                if (await _context.Letters.AnyAsync(l => l.LetterNumber == number))
                {
                    result.Errors.Add(new("NumberInUse", "manualNumber"));
                    return result;
                }
            }
            else
            {
                number = await _numbering.GenerateNextNumberAsync(input.LetterType, input.ProjectId, departmentId);
            }

            letter = new Letter
            {
                LetterType = input.LetterType,
                LetterNumber = number,
                Status = "Draft",
                RegisteredById = actor.UserId,
                IsActive = true,
                CreatedDate = DateTime.Now,
                SenderEmployeeId = employee?.EmployeeId,
                SenderTitle = TitleOf(employee),
                SenderDepartmentId = departmentId,
                Sender = FullName(employee),
                ParentLetterId = parent?.LetterId
            };
            _context.Letters.Add(letter);
            // مثل ReplyAsync قدیمی: نامه‌ی ثبت‌شده‌ای که پاسخ می‌گیرد «ارجاع‌شده» می‌شود
            if (parent is { Status: "Registered" }) parent.Status = "Referred";
        }
        else
        {
            var existing = await _context.Letters.FirstOrDefaultAsync(l => l.LetterId == input.LetterId && l.IsActive);
            if (existing == null) { result.Errors.Add(new("LetterNotFound")); return result; }
            if (existing.RegisteredById != actor.UserId) { result.Errors.Add(new("NotAuthor")); return result; }
            if (!EditableStatuses.Contains(existing.Status)) { result.Errors.Add(new("NotEditable", null, existing.Status)); return result; }

            if (input.RowVersion != null) _context.Entry(existing).Property(x => x.RowVersion).OriginalValue = input.RowVersion;
            letter = existing;
            if (input.SenderDepartmentId.HasValue) letter.SenderDepartmentId = input.SenderDepartmentId;
        }

        // ارجاع: موضوع همان موضوع نامه‌ی اصلی است (هر مقدار ارسالی نادیده گرفته می‌شود)، تایید ندارد و دسته‌اش «دستور» است
        letter.IsReferral = input.IsReferral && parent != null && input.LetterType == "Internal";
        letter.Category = letter.IsReferral ? "Instruction" : input.Category;
        letter.Subject = letter.IsReferral ? parent!.Subject : (input.Subject ?? "").Trim();
        letter.Body = _sanitizer.Sanitize(input.Body);
        letter.Priority = input.Priority;
        letter.ConfidentialityLevel = input.ConfidentialityLevel;
        letter.LetterDate = input.LetterDate;
        letter.ReferenceNumber = string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim();
        letter.ProjectId = input.ProjectId;
        letter.DisciplineId = input.DisciplineId;
        letter.RelatedDepartmentId = input.RelatedDepartmentId;
        letter.ResponseRequired = input.ResponseRequired;
        letter.ResponseDueDate = input.ResponseRequired ? input.ResponseDueDate!.Value.Date : null;
        // نامه‌ی خارجی (Outgoing) همیشه تایید می‌خواهد؛ داخلی انتخابی است
        letter.ApprovalRequired = !letter.IsReferral && (input.LetterType == "Outgoing" || input.ApprovalRequired);

        var toNames = input.ToEmployeeIds.Where(check.Employees.ContainsKey).Select(id => FullName(check.Employees[id])).ToList();
        letter.Receiver = input.LetterType == "Outgoing" ? input.ExternalReceiver?.Trim() : string.Join(", ", toNames);
        letter.ReceiverEmployeeId = input.LetterType == "Internal" ? input.ToEmployeeIds.Cast<int?>().FirstOrDefault() : null;
        letter.CorrespondentPartyId = partyId;

        try
        {
            await _context.SaveChangesAsync();   // شناسه‌ی نامه‌ی جدید را برای ردیف‌های وابسته می‌گیرد
            await SyncRecipientsAsync(letter, input);
            await SyncApprovalStepsAsync(letter, input);
            if (letter.IsReferral) await EnsureReferralTagAsync(letter, actor.UserId);
            var version = await LetterVersioning.SnapshotAsync(_context, letter, actor.UserId, autoSave ? "AutoSave" : "Draft");
            await _context.SaveChangesAsync();

            result.Success = true;
            result.LetterId = letter.LetterId;
            result.LetterNumber = letter.LetterNumber;
            result.RowVersion = letter.RowVersion;
            result.VersionNo = version;
            result.SavedAt = DateTime.Now;
        }
        catch (DbUpdateConcurrencyException)
        {
            result.Errors.Add(new("Concurrency"));
        }

        return result;
    }

    private async Task SyncRecipientsAsync(Letter letter, LetterComposeInput input)
    {
        var desired = input.ToEmployeeIds.Select(id => (id, type: "To"))
            .Concat(input.CcEmployeeIds.Select(id => (id, type: "Cc")))
            .Concat(input.BccEmployeeIds.Select(id => (id, type: "Bcc"))).ToList();

        var existing = await _context.LetterRecipients.Where(r => r.LetterId == letter.LetterId && r.EmployeeId != null).ToListAsync();

        foreach (var row in existing.Where(r => !desired.Any(d => d.id == r.EmployeeId && d.type == r.RecipientType)))
            _context.LetterRecipients.Remove(row);   // فقط پیش‌نویس ویرایش می‌شود؛ هنوز کسی چیزی «نخوانده/نخوانده‌نشده» نکرده است

        foreach (var d in desired.Where(d => !existing.Any(r => r.EmployeeId == d.id && r.RecipientType == d.type)))
            _context.LetterRecipients.Add(new LetterRecipient { LetterId = letter.LetterId, EmployeeId = d.id, RecipientType = d.type, CreatedDate = DateTime.Now });
    }

    private async Task SyncApprovalStepsAsync(Letter letter, LetterComposeInput input)
    {
        var existing = await _context.LetterApprovalSteps.Where(s => s.LetterId == letter.LetterId).OrderBy(s => s.StepOrder).ToListAsync();
        var desired = letter.ApprovalRequired ? input.ApproverUserIds : new List<int>();

        var same = existing.Count == desired.Count && existing.Select(s => s.ApproverUserId).SequenceEqual(desired);
        if (same) return;

        _context.LetterApprovalSteps.RemoveRange(existing);
        await _context.SaveChangesAsync();   // آزادسازی ایندکس یکتای (LetterId, StepOrder) پیش از درج ردیف‌های تازه

        var order = 1;
        foreach (var userId in desired)
            _context.LetterApprovalSteps.Add(new LetterApprovalStep { LetterId = letter.LetterId, StepOrder = order++, ApproverUserId = userId, Status = "Pending" });
    }

    // ------------------------------------------------------------------ load / validate

    public async Task<LetterComposeInput?> LoadForEditAsync(int letterId, LetterComposeActor actor)
    {
        var letter = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == letterId && l.IsActive);
        if (letter == null || letter.RegisteredById != actor.UserId || !EditableStatuses.Contains(letter.Status)) return null;
        return await MapAsync(letter);
    }

    private async Task<LetterComposeInput> MapAsync(Letter letter)
    {
        var recipients = await _context.LetterRecipients.AsNoTracking().Where(r => r.LetterId == letter.LetterId && r.EmployeeId != null).ToListAsync();
        var steps = await _context.LetterApprovalSteps.AsNoTracking().Where(s => s.LetterId == letter.LetterId).OrderBy(s => s.StepOrder).ToListAsync();

        return new LetterComposeInput
        {
            LetterId = letter.LetterId,
            RowVersion = letter.RowVersion,
            ParentLetterId = letter.ParentLetterId,
            IsReferral = letter.IsReferral,
            LetterType = letter.LetterType,
            Category = letter.Category,
            Subject = letter.Subject,
            Body = letter.Body,
            Priority = letter.Priority,
            ConfidentialityLevel = letter.ConfidentialityLevel,
            LetterDate = letter.LetterDate,
            ReferenceNumber = letter.ReferenceNumber,
            ProjectId = letter.ProjectId,
            DisciplineId = letter.DisciplineId,
            RelatedDepartmentId = letter.RelatedDepartmentId,
            SenderDepartmentId = letter.SenderDepartmentId,
            ToEmployeeIds = recipients.Where(r => r.RecipientType == "To").Select(r => r.EmployeeId!.Value).ToList(),
            CcEmployeeIds = recipients.Where(r => r.RecipientType == "Cc").Select(r => r.EmployeeId!.Value).ToList(),
            BccEmployeeIds = recipients.Where(r => r.RecipientType == "Bcc").Select(r => r.EmployeeId!.Value).ToList(),
            ExternalReceiver = letter.LetterType == "Outgoing" ? letter.Receiver : null,
            CorrespondentPartyId = letter.LetterType == "Outgoing" ? letter.CorrespondentPartyId : null,
            ResponseRequired = letter.ResponseRequired,
            ResponseDueDate = letter.ResponseDueDate,
            ApprovalRequired = letter.ApprovalRequired,
            ApproverUserIds = steps.Select(s => s.ApproverUserId).ToList()
        };
    }

    public async Task<LetterComposeResult> ValidateForSubmitAsync(int letterId, LetterComposeActor actor)
    {
        var result = new LetterComposeResult { LetterId = letterId };
        var letter = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == letterId && l.IsActive);
        if (letter == null) { result.Errors.Add(new("LetterNotFound")); return result; }

        var input = await MapAsync(letter);
        result.Errors.AddRange(LetterComposeRules.ValidateForSubmit(input, letter.RegisteredById));

        var check = await DirectoryChecksAsync(input);
        result.Errors.AddRange(check.Errors);
        result.Warnings.AddRange(check.Warnings);
        result.Warnings.AddRange(LetterComposeRules.StaticWarnings(input, letter.SenderEmployeeId ?? actor.EmployeeId));

        result.Success = result.Errors.Count == 0;
        result.LetterNumber = letter.LetterNumber;
        return result;
    }

    // ------------------------------------------------------------------ directory

    public async Task<LetterComposeInput?> BuildReplyInputAsync(int parentLetterId, LetterComposeActor actor)
    {
        var parent = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == parentLetterId && l.IsActive);
        if (parent == null) return null;

        var internalReply = parent.LetterType == "Internal";
        var input = new LetterComposeInput
        {
            ParentLetterId = parent.LetterId,
            LetterType = internalReply ? "Internal" : "Outgoing",
            Category = LetterComposeRules.Categories.Contains(parent.Category ?? "") ? parent.Category : null,
            Subject = ReplySubject(parent.Subject),
            Priority = LetterComposeRules.Priorities.Contains(parent.Priority) ? parent.Priority : "Normal",
            ConfidentialityLevel = LetterComposeRules.Confidentialities.Contains(parent.ConfidentialityLevel) ? parent.ConfidentialityLevel : "Internal",
            ReferenceNumber = parent.LetterNumber,
            ProjectId = parent.ProjectId,
            DisciplineId = parent.DisciplineId,
            RelatedDepartmentId = parent.RelatedDepartmentId,
            ApprovalRequired = !internalReply
        };

        if (!internalReply)
        {
            input.ExternalReceiver = parent.LetterType == "Incoming" ? parent.Sender : parent.Receiver;
            input.CorrespondentPartyId = parent.CorrespondentPartyId;
            return input;
        }

        // پاسخ به نویسنده‌ی نامه؛ اگر پاسخ‌دهنده خودِ نویسنده است، به گیرندگان «To» همان نامه
        var senderEmployeeId = parent.SenderEmployeeId
            ?? (parent.RegisteredById.HasValue
                ? await _context.Users.Where(u => u.Id == parent.RegisteredById.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync()
                : null);

        var targets = new List<int>();
        if (senderEmployeeId.HasValue && senderEmployeeId != actor.EmployeeId) targets.Add(senderEmployeeId.Value);
        else
            targets.AddRange(await _context.LetterRecipients.AsNoTracking()
                .Where(r => r.LetterId == parent.LetterId && r.RecipientType == "To" && r.EmployeeId != null && r.EmployeeId != actor.EmployeeId)
                .Select(r => r.EmployeeId!.Value).ToListAsync());

        var active = (await LoadDirectoryAsync()).Select(d => d.EmployeeId).ToHashSet();
        input.ToEmployeeIds = targets.Distinct().Where(active.Contains).ToList();
        return input;
    }

    public async Task<LetterComposeInput?> BuildReferralInputAsync(int parentLetterId, LetterComposeActor actor)
    {
        var parent = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == parentLetterId && l.IsActive);
        if (parent == null) return null;

        var senderName = parent.SenderEmployeeId.HasValue
            ? await _context.Employees.Where(e => e.EmployeeId == parent.SenderEmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync()
            : null;
        var from = System.Net.WebUtility.HtmlEncode(senderName ?? parent.Sender ?? "");
        var number = System.Net.WebUtility.HtmlEncode(parent.LetterNumber);

        return new LetterComposeInput
        {
            ParentLetterId = parent.LetterId,
            IsReferral = true,
            LetterType = "Internal",
            Category = "Instruction",
            Subject = parent.Subject,
            Priority = LetterComposeRules.Priorities.Contains(parent.Priority) ? parent.Priority : "Normal",
            ConfidentialityLevel = LetterComposeRules.Confidentialities.Contains(parent.ConfidentialityLevel) ? parent.ConfidentialityLevel : "Internal",
            ReferenceNumber = parent.LetterNumber,
            ProjectId = parent.ProjectId,
            DisciplineId = parent.DisciplineId,
            RelatedDepartmentId = parent.RelatedDepartmentId,
            ApprovalRequired = false,
            // دستورالعمل ارجاع بالا، نقل نامه‌ی اصلی پایین (نقل با ReferralInstructionText از دستورالعمل جدا می‌شود)
            Body = "<p></p><blockquote><p><strong>" + number + "</strong> &middot; " + parent.LetterDate.ToString("yyyy-MM-dd") + (from.Length > 0 ? " &middot; " + from : "") + "</p>" + (parent.Body ?? "") + "</blockquote>"
        };
    }

    /// <summary>برچسب سازمانی «ارجاع» را (اگر نبود) می‌سازد و روی نامه می‌زند؛ تکراری نمی‌سازد.</summary>
    private async Task EnsureReferralTagAsync(Letter letter, int userId)
    {
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == LetterComposeRules.ReferralTagName && t.OwnerUserId == null && t.ParentTagId == null);
        if (tag == null)
        {
            tag = new Tag { Name = LetterComposeRules.ReferralTagName, Color = "warning", CreatedById = userId, IsActive = true, CreatedDate = DateTime.Now };
            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();
        }
        else if (!tag.IsActive) { tag.IsActive = true; await _context.SaveChangesAsync(); }

        if (!await _context.LetterTags.AnyAsync(lt => lt.LetterId == letter.LetterId && lt.TagId == tag.TagId))
            _context.LetterTags.Add(new LetterTag { LetterId = letter.LetterId, TagId = tag.TagId, CreatedDate = DateTime.Now });
    }

    private static string ReplySubject(string? subject)
    {
        var text = (subject ?? "").Trim();
        if (!text.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)) text = "Re: " + text;
        return text.Length > LetterComposeRules.MaxSubjectLength ? text[..LetterComposeRules.MaxSubjectLength].TrimEnd() : text;
    }

    public async Task<LetterSenderBlockDto> GetSenderBlockAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FullName, u.UserName, u.Email, u.PhoneNumber, u.EmployeeId }).FirstOrDefaultAsync();
        if (user == null) return new LetterSenderBlockDto();

        var e = user.EmployeeId.HasValue
            ? await _context.Employees.AsNoTracking().Include(x => x.PositionRef).Include(x => x.Department).FirstOrDefaultAsync(x => x.EmployeeId == user.EmployeeId)
            : null;

        return new LetterSenderBlockDto
        {
            EmployeeId = e?.EmployeeId,
            FullName = e != null ? FullName(e) : (user.FullName ?? user.UserName ?? ""),
            Title = TitleOf(e),
            DepartmentId = e?.DepartmentId,
            DepartmentName = e?.Department?.Name,
            Email = e?.Email ?? user.Email,
            Phone = e?.Phone ?? user.PhoneNumber,
            SignaturePath = e?.SignaturePath
        };
    }

    private Task<List<EmployeeDirectoryItem>> LoadDirectoryAsync()
        => _cache.GetOrCreateAsync(DirectoryCacheKey, async () =>
            (await _context.Employees.AsNoTracking().Include(e => e.PositionRef).Include(e => e.Department).Where(e => e.IsActive)
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync())
            .Select(e => new EmployeeDirectoryItem
            {
                EmployeeId = e.EmployeeId, FullName = FullName(e), Position = TitleOf(e), DepartmentName = e.Department?.Name,
                Email = e.Email, PersonnelCode = e.PersonnelCode, IsExternal = IsExternal(e)
            }).ToList(), TimeSpan.FromMinutes(5));

    public async Task<List<EmployeeDirectoryItem>> GetDirectoryItemsAsync(IEnumerable<int> employeeIds)
    {
        var directory = (await LoadDirectoryAsync()).ToDictionary(d => d.EmployeeId);
        return employeeIds.Distinct().Where(directory.ContainsKey).Select(id => directory[id]).ToList();
    }

    public async Task<List<EmployeeDirectoryItem>> SearchDirectoryAsync(string? query, int take = 20)
    {
        var all = await LoadDirectoryAsync();
        take = Math.Clamp(take, 1, 100);
        var q = (query ?? "").Trim();
        if (q.Length == 0) return all.Take(take).ToList();

        return all.Where(e => Contains(e.FullName, q) || Contains(e.Position, q) || Contains(e.DepartmentName, q)
                || Contains(e.Email, q) || Contains(e.PersonnelCode, q))
            .Take(take).ToList();

        static bool Contains(string? hay, string needle) => hay != null && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<RecipientResolveResult> ResolveRecipientsAsync(string? text, int max = 500)
    {
        var result = new RecipientResolveResult();
        max = Math.Clamp(max, 1, LetterComposeRules.MaxRecipients);

        var tokens = (text ?? "").Split(new[] { '\r', '\n', ';', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim('"')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tokens.Count > max) { result.Truncated = true; tokens = tokens.Take(max).ToList(); }

        var directory = await LoadDirectoryAsync();
        var seen = new HashSet<int>();
        foreach (var token in tokens)
        {
            var hits = directory.Where(e =>
                string.Equals(e.PersonnelCode, token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Email, token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.FullName, token, StringComparison.OrdinalIgnoreCase)).ToList();

            if (hits.Count == 1) { if (seen.Add(hits[0].EmployeeId)) result.Matched.Add(hits[0]); }
            else result.Unmatched.Add(hits.Count == 0 ? token : token + " (ambiguous)");
        }

        return result;
    }

    public async Task<List<ApproverOptionDto>> GetApproverOptionsAsync(int? authorUserId = null)
    {
        var eligible = (await LetterDirectoryQueries.ApproverUserIdsAsync(_context)).ToHashSet();
        var staff = await (from u in _context.Users.AsNoTracking()
                           join e in _context.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId
                           where u.IsActive && e.IsActive
                           select u.Id).ToListAsync();
        var ids = eligible.Union(staff).Where(id => id != authorUserId).ToList();

        var users = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).ToListAsync();
        var empIds = users.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).ToList();
        var employees = await _context.Employees.AsNoTracking().Include(e => e.PositionRef).Include(e => e.Department)
            .Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId);

        // مافوق‌های نویسنده از چارت سازمانی (سمت اصلی ← سمت مافوق ← دارنده)، تا پنج سطح
        var managerLevelByEmployee = new Dictionary<int, int>();
        var authorEmployeeId = authorUserId.HasValue ? users.FirstOrDefault(u => u.Id == authorUserId)?.EmployeeId
            ?? await _context.Users.AsNoTracking().Where(u => u.Id == authorUserId).Select(u => u.EmployeeId).FirstOrDefaultAsync() : null;
        if (authorEmployeeId.HasValue)
            for (var level = 1; level <= 5; level++)
            {
                var chain = await OrganizationManagerChain.WalkAsync(_context, authorEmployeeId.Value, level, DateTime.Now);
                if (!chain.Resolved) break;
                foreach (var holder in chain.HolderEmployeeIds) managerLevelByEmployee.TryAdd(holder, level);
            }

        return users.Select(u =>
        {
            employees.TryGetValue(u.EmployeeId ?? 0, out var e);
            var level = u.EmployeeId.HasValue && managerLevelByEmployee.TryGetValue(u.EmployeeId.Value, out var l) ? l : 0;
            return new ApproverOptionDto
            {
                UserId = u.Id,
                FullName = string.IsNullOrWhiteSpace(u.FullName) ? (u.UserName ?? "") : u.FullName,
                Position = e == null ? null : TitleOf(e),
                Department = e?.Department?.Name,
                ManagerLevel = level,
                Group = level > 0 ? "Manager" : eligible.Contains(u.Id) ? "Eligible" : "Other"
            };
        })
        .OrderBy(o => o.Group == "Manager" ? 0 : o.Group == "Eligible" ? 1 : 2).ThenBy(o => o.ManagerLevel).ThenBy(o => o.FullName)
        .ToList();
    }

    // ------------------------------------------------------------------ versions / discard

    public async Task<List<LetterVersionDto>> GetVersionsAsync(int letterId)
    {
        var versions = await _context.LetterVersions.AsNoTracking().Where(v => v.LetterId == letterId).OrderByDescending(v => v.VersionNo).ToListAsync();
        var ids = versions.Where(v => v.SavedByUserId.HasValue).Select(v => v.SavedByUserId!.Value).Distinct().ToList();
        var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "-");

        return versions.Select(v => new LetterVersionDto
        {
            VersionNo = v.VersionNo, Subject = v.Subject, Body = v.Body, Reason = v.Reason, SavedDate = v.SavedDate,
            SavedByName = v.SavedByUserId.HasValue && names.TryGetValue(v.SavedByUserId.Value, out var n) ? n : null
        }).ToList();
    }

    public async Task<(bool Success, string? Error)> DiscardDraftAsync(int letterId, LetterComposeActor actor)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(l => l.LetterId == letterId && l.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");
        if (letter.RegisteredById != actor.UserId && !actor.IsAdmin) return (false, "فقط نویسنده‌ی نامه می‌تواند پیش‌نویس را دور بیندازد.");
        if (letter.Status != "Draft") return (false, $"فقط پیش‌نویس (Draft) قابل دور انداختن است - وضعیت فعلی «{letter.Status}» است.");

        letter.IsActive = false;
        await _context.AuditLogs.AddAsync(new AuditLog
        {
            EntityName = nameof(Letter), EntityKeyValue = letter.LetterId.ToString(), Action = "Discard",
            ChangedFieldsSummary = $"IsActive: True -> False\nStatus: {letter.Status}\nNumber: {letter.LetterNumber}",
            UserId = actor.UserId, UserName = actor.UserName, Timestamp = DateTime.Now
        });
        await _context.SaveChangesAsync();
        return (true, null);
    }
}

/// <summary>پرس‌وجوهای مشترک سرویس‌های نامه.</summary>
internal static class LetterDirectoryQueries
{
    /// <summary>کاربران فعال نقش‌هایی که در ماتریس مجوز، Approve ماژول Correspondence را دارند.</summary>
    public static async Task<List<int>> ApproverUserIdsAsync(PEMSDbContext context)
    {
        var roleNames = context.RolePermissions.Where(p => p.ModuleName == "Correspondence" && p.CanApprove).Select(p => p.RoleName);

        return await (from ur in context.UserRoles
                      join r in context.Roles on ur.RoleId equals r.Id
                      join u in context.Users on ur.UserId equals u.Id
                      where roleNames.Contains(r.Name!) && u.IsActive
                      select u.Id).Distinct().ToListAsync();
    }
}

/// <summary>
/// تاریخچه‌ی نسخه‌ی نامه. ذخیره‌ی خودکار/پیش‌نویس پشت‌سرهم از همان کاربر در پنجره‌ی ۵ دقیقه همان ردیف را به‌روز می‌کند؛ محتوای بدون تغییر نسخه‌ی
/// تازه نمی‌سازد. ارسال برای تایید/ارسال مجدد/ارسال همیشه یک ردیف مستقل ثبت می‌کند (رویداد رسمی). تغییرات SaveChanges را فراخواننده می‌زند.
/// </summary>
internal static class LetterVersioning
{
    private static readonly TimeSpan AutoSaveMergeWindow = TimeSpan.FromMinutes(5);

    public static async Task<int> SnapshotAsync(PEMSDbContext context, Letter letter, int? userId, string reason)
    {
        var last = await context.LetterVersions.Where(v => v.LetterId == letter.LetterId).OrderByDescending(v => v.VersionNo).FirstOrDefaultAsync();
        var subject = string.IsNullOrEmpty(letter.Subject) ? "" : letter.Subject;
        var isDraftish = reason is "AutoSave" or "Draft";

        if (last != null && isDraftish)
        {
            if (last.Subject == subject && last.Body == letter.Body) return last.VersionNo;   // بدون تغییر

            // فقط «ذخیره‌ی خودکار» داخل «ذخیره‌ی خودکار قبلی» ادغام می‌شود؛ ذخیره‌ی صریح همیشه نسخه‌ی مستقل می‌سازد و هرگز با ذخیره‌ی خودکار بازنویسی نمی‌شود
            if (reason == "AutoSave" && last.Reason == "AutoSave" && last.SavedByUserId == userId && DateTime.Now - last.SavedDate < AutoSaveMergeWindow)
            {
                last.Subject = subject;
                last.Body = letter.Body;
                last.SavedDate = DateTime.Now;
                return last.VersionNo;
            }
        }

        var no = (last?.VersionNo ?? 0) + 1;
        context.LetterVersions.Add(new LetterVersion
        {
            LetterId = letter.LetterId, VersionNo = no, Subject = subject, Body = letter.Body, Reason = reason, SavedByUserId = userId, SavedDate = DateTime.Now
        });
        return no;
    }
}
