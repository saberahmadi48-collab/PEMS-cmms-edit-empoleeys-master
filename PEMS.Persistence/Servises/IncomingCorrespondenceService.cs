using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// ثبت مکاتبه‌ی وارده در DCC. قبلاً Controller خودش شماره می‌گرفت، نامه را می‌ساخت و سپس فایل را جدا ذخیره می‌کرد (خطای وسط راه = نامه‌ی بدون
/// پیوست یا فایل یتیم) و تاریخ نامه همیشه «اکنون» بود. اینجا: اعتبارسنجی کامل، سپس ذخیره‌ی فایل‌ها، سپس یک SaveChanges برای همه‌ی ردیف‌ها؛
/// اگر ذخیره‌ی پایگاه داده شکست بخورد فایل‌های ذخیره‌شده پاک می‌شوند. فهرست‌برداری معنایی (Semantic Index) مثل LetterService.CreateAsync حفظ شده است.
/// </summary>
public class IncomingCorrespondenceService : IIncomingCorrespondenceService
{
    // نامه‌ی صادره‌ی پاسخ‌خواه با ثبت پاسخ وارده بسته می‌شود (هم‌ارز LetterService.CloseParentIfAnsweredAsync)
    private static readonly string[] ClosableParentStatuses = { "Sent", "Acknowledged", "Issued" };

    private readonly PEMSDbContext _context;
    private readonly ILetterNumberingService _numbering;
    private readonly IFileStorageService _storage;
    private readonly IKnowledgeBaseService _knowledgeBase;
    private readonly INotificationService? _notifications;
    private readonly ILetterRoutingRuleService? _routingRules;

    public IncomingCorrespondenceService(PEMSDbContext context, ILetterNumberingService numbering, IFileStorageService storage,
        IKnowledgeBaseService knowledgeBase, INotificationService? notifications = null, ILetterRoutingRuleService? routingRules = null)
    {
        _context = context;
        _numbering = numbering;
        _storage = storage;
        _knowledgeBase = knowledgeBase;
        _notifications = notifications;
        _routingRules = routingRules;
    }

    // ------------------------------------------------------------------ ثبت

    public async Task<IncomingRegistrationResult> RegisterAsync(IncomingRegistrationInput input, int? actorUserId, int? actorEmployeeId)
    {
        var today = DateTime.Today;
        var issues = IncomingCorrespondenceRules.ValidateStructure(input, today);
        if (issues.Count > 0) return new IncomingRegistrationResult { Issues = issues };

        var received = (input.ReceivedDate ?? today).Date;

        // --- اعتبارسنجی وابسته به پایگاه داده
        if (!await _context.Projects.AnyAsync(p => p.ProjectId == input.ProjectId!.Value && p.IsActive))
            issues.Add(new("ProjectNotFound", "projectId"));

        if (input.ContractId.HasValue
            && !await _context.Contracts.AnyAsync(c => c.ContractId == input.ContractId.Value && c.ProjectId == input.ProjectId!.Value))
            issues.Add(new("ContractInvalid", "contractId"));

        if (input.DisciplineId.HasValue && !await _context.Disciplines.AnyAsync(d => d.DisciplineId == input.DisciplineId.Value && d.IsActive))
            issues.Add(new("DisciplineNotFound", "disciplineId"));

        CorrespondentParty? party = null;
        if (input.CorrespondentPartyId.HasValue)
        {
            party = await _context.CorrespondentParties.FirstOrDefaultAsync(p => p.CorrespondentPartyId == input.CorrespondentPartyId.Value && p.IsActive);
            if (party == null) issues.Add(new("PartyNotFound", "party"));
        }
        else
        {
            // همان نام قبلاً ثبت شده؟ طرف تکراری ساخته نمی‌شود
            var name = input.NewPartyName!.Trim();
            var lowered = name.ToLower();
            party = await _context.CorrespondentParties.FirstOrDefaultAsync(p => p.IsActive && p.Name.ToLower() == lowered);
            if (party == null)
                party = new CorrespondentParty { Name = name, PartyType = input.NewPartyType!, IsProvisional = true, IsActive = true };
        }

        Letter? parent = null;
        if (input.ParentLetterId.HasValue)
        {
            parent = await _context.Letters.FirstOrDefaultAsync(l => l.LetterId == input.ParentLetterId.Value && l.IsActive);
            if (parent == null || parent.LetterType != "Outgoing" || LetterAccessRules.IsPreSend(parent.Status))
                issues.Add(new("ParentInvalid", "parentLetterId"));
            else if (party is { CorrespondentPartyId: > 0 } && parent.CorrespondentPartyId.HasValue && parent.CorrespondentPartyId != party.CorrespondentPartyId)
                issues.Add(new("ParentPartyMismatch", "parentLetterId"));
        }

        Employee? owner = null;
        if (input.OwnerEmployeeId.HasValue)
        {
            owner = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == input.OwnerEmployeeId.Value && e.IsActive);
            if (owner == null) issues.Add(new("OwnerNotFound", "ownerEmployeeId"));
        }

        var ccIds = input.CcEmployeeIds.Distinct().Where(id => id != input.OwnerEmployeeId).ToList();
        if (ccIds.Count > 0)
        {
            var found = await _context.Employees.Where(e => ccIds.Contains(e.EmployeeId) && e.IsActive).Select(e => e.EmployeeId).ToListAsync();
            if (found.Count != ccIds.Count) issues.Add(new("CcNotFound", "ccEmployeeIds"));
        }

        var links = await ResolveLinksAsync(input.Links, issues);

        // ثبت تکراری (طرف مکاتبه + شماره‌ی فرستنده)
        var reference = (input.SenderReference ?? "").Trim();
        if (issues.Count == 0 && !input.ConfirmDuplicate && reference.Length > 0 && party is { CorrespondentPartyId: > 0 })
        {
            var duplicate = await FindDuplicateAsync(party.CorrespondentPartyId, reference);
            if (duplicate != null)
            {
                return new IncomingRegistrationResult
                {
                    Issues = { new("Duplicate", "senderReference") },
                    DuplicateLetterId = duplicate.Value.LetterId,
                    DuplicateLetterNumber = duplicate.Value.LetterNumber
                };
            }
        }

        // امضای فایل و هش (قبل از هر نوشتنی)
        var hashes = new Dictionary<IncomingFileInput, string>();
        foreach (var file in input.Files)
        {
            if (file.Content == null || !file.Content.CanSeek) { issues.Add(new("FileEmpty", "files", file.FileName)); continue; }

            file.Content.Position = 0;
            var head = new byte[PEMS.Application.Uploads.UploadPolicy.HeadLength];
            var read = await file.Content.ReadAsync(head.AsMemory(0, head.Length));
            if (!IncomingCorrespondenceRules.SignatureMatches(file.FileName, head.AsSpan(0, read)))
            {
                issues.Add(new("FileContentMismatch", "files", file.FileName));
                continue;
            }

            file.Content.Position = 0;
            hashes[file] = Convert.ToHexString(await SHA256.HashDataAsync(file.Content));
            file.Content.Position = 0;
        }

        if (issues.Count > 0) return new IncomingRegistrationResult { Issues = issues };

        // --- شماره (شمارنده خودش ذخیره می‌شود؛ در صورت شکست بعدی یک شماره‌ی خالی می‌ماند و استفاده‌نشده است)
        var letterNumber = await _numbering.GenerateNextIncomingNumberAsync(input.ProjectId, received);

        // --- ذخیره‌ی فایل‌ها
        var stagedKeys = new List<string>();
        var attachments = new List<DocumentAttachment>();
        try
        {
            foreach (var file in input.Files)
            {
                file.Content!.Position = 0;
                var key = await _storage.SaveAsync("letters", file.FileName, file.Content);
                stagedKeys.Add(key);

                attachments.Add(new DocumentAttachment
                {
                    FileName = file.FileName,
                    FileExtension = Path.GetExtension(file.FileName),
                    FilePath = key,
                    FileSize = file.Length,
                    MimeType = file.ContentType,
                    FileRole = file.Role,
                    IsOfficial = file.Role == "Original",
                    FileHash = hashes[file],
                    UploadedById = actorEmployeeId,
                    UploadDate = DateTime.Now,
                    IsActive = true,
                    AIStatus = "NotApplicable"
                });
            }
        }
        catch
        {
            await DeleteStagedAsync(stagedKeys);
            throw;
        }

        // --- ساخت نامه و همه‌ی ردیف‌های وابسته
        var firstDocument = links.FirstOrDefault(l => l.LinkType is "EngineeringDocument" or "VdrItem");
        var firstMr = links.FirstOrDefault(l => l.LinkType == "MaterialRequisition");

        var letter = new Letter
        {
            LetterType = "Incoming",
            LetterNumber = letterNumber,
            LetterDate = received == today ? DateTime.Now : received,
            DocumentDate = input.DocumentDate!.Value.Date,
            ReceivedDate = received,
            ReceiptChannel = input.ReceiptChannel,
            SenderReference = reference.Length == 0 ? null : reference,
            Sender = party!.Name,
            CorrespondentParty = party.CorrespondentPartyId > 0 ? null : party,
            CorrespondentPartyId = party.CorrespondentPartyId > 0 ? party.CorrespondentPartyId : null,
            ContractId = input.ContractId,
            Subject = input.Subject.Trim(),
            Body = ToHtml(input.Summary),
            Priority = input.Priority,
            Category = input.Category,
            ConfidentialityLevel = input.ConfidentialityLevel,
            ProjectId = input.ProjectId,
            DisciplineId = input.DisciplineId,
            RelatedDocumentId = firstDocument?.TargetId,
            RelatedMRId = firstMr?.TargetId,
            ParentLetterId = parent?.LetterId,
            ResponseRequired = input.ResponseRequired,
            ResponseDueDate = input.ResponseRequired ? input.ResponseDueDate!.Value.Date : null,
            RegisteredById = actorUserId,
            Status = owner != null ? "Referred" : "Registered",
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        foreach (var a in attachments) letter.Attachments.Add(a);
        foreach (var l in links) letter.Links.Add(l);
        foreach (var id in ccIds) letter.Recipients.Add(new LetterRecipient { RecipientType = "Cc", EmployeeId = id });

        if (owner != null)
        {
            var actorName = await _context.Employees.Where(e => e.EmployeeId == actorEmployeeId)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync();
            letter.Referrals.Add(new LetterReferral
            {
                ReferredToEmployeeId = owner.EmployeeId,
                Instructions = string.IsNullOrWhiteSpace(input.Instruction) ? null : input.Instruction.Trim(),
                DueDate = letter.ResponseDueDate,
                Status = "Pending",
                ReferredByName = actorName ?? "DCC",
                ReferredDate = DateTime.Now
            });
        }

        // ثبت پاسخ وارده، نامه‌ی صادره‌ی پاسخ‌خواه را می‌بندد
        if (parent != null && parent.ResponseRequired && ClosableParentStatuses.Contains(parent.Status))
        {
            parent.Status = "Closed";
            parent.ClosedDate = DateTime.Now;
        }

        _context.Letters.Add(letter);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            await DeleteStagedAsync(stagedKeys);
            throw;
        }

        await NotifyAsync(letter, owner, ccIds);
        // قوانین مسیریابی خودکارِ گیرنده/ارجاع‌گیرنده‌ی نامه‌ی وارده؛ هرگز ثبت را نمی‌شکند
        if (_routingRules != null) await _routingRules.ApplyToArrivedLetterAsync(letter.LetterId);
        await IndexAsync(letter);

        return new IncomingRegistrationResult { Success = true, LetterId = letter.LetterId, LetterNumber = letter.LetterNumber };
    }

    public async Task<string> PeekNextNumberAsync(int? projectId, DateTime receivedDate)
    {
        string? code = null;
        if (projectId.HasValue)
            code = await _context.Projects.Where(p => p.ProjectId == projectId.Value).Select(p => p.ProjectCode).FirstOrDefaultAsync();

        var scope = LetterNumberingService.SanitizeScope(code) + "-IN";
        var year = JalaliDate.Year(receivedDate);
        var last = await _context.LetterNumberSequences.Where(s => s.Scope == scope && s.Year == year).Select(s => (int?)s.LastValue).FirstOrDefaultAsync();
        return LetterNumberingService.Format(scope, year, (last ?? 0) + 1, 4);
    }

    // ------------------------------------------------------------------ پرس‌وجوها

    public async Task<List<PartySearchItemDto>> SearchPartiesAsync(string? query, int? projectId, int take = 20)
    {
        var q = (query ?? "").Trim().ToLower();
        var parties = _context.CorrespondentParties.AsNoTracking().Where(p => p.IsActive);
        if (q.Length > 0) parties = parties.Where(p => p.Name.ToLower().Contains(q) || (p.Code != null && p.Code.ToLower().Contains(q)));

        var list = await parties.OrderBy(p => p.Name).Take(Math.Clamp(take, 1, 50))
            .Select(p => new PartySearchItemDto { Id = p.CorrespondentPartyId, Name = p.Name, Code = p.Code, PartyType = p.PartyType, IsProvisional = p.IsProvisional })
            .ToListAsync();

        var vendorIds = await _context.CorrespondentParties.Where(p => list.Select(x => x.Id).Contains(p.CorrespondentPartyId) && p.VendorId != null)
            .Select(p => new { p.CorrespondentPartyId, p.VendorId }).ToListAsync();
        if (vendorIds.Count > 0)
        {
            var ids = vendorIds.Select(v => v.VendorId!.Value).Distinct().ToList();
            var contractsQuery = _context.Contracts.AsNoTracking().Where(c => ids.Contains(c.VendorId));
            if (projectId.HasValue) contractsQuery = contractsQuery.Where(c => c.ProjectId == projectId.Value);
            var contracts = await contractsQuery.OrderBy(c => c.ContractNo).Select(c => new { c.ContractId, c.ContractNo, c.VendorId }).ToListAsync();

            foreach (var item in list)
            {
                var vendorId = vendorIds.FirstOrDefault(v => v.CorrespondentPartyId == item.Id)?.VendorId;
                if (vendorId == null) continue;
                item.Contracts = contracts.Where(c => c.VendorId == vendorId)
                    .Select(c => new PartyContractDto { ContractId = c.ContractId, ContractNo = c.ContractNo }).ToList();
            }
        }

        return list;
    }

    public async Task<PartyThreadDto?> GetPartyThreadAsync(int partyId, bool canViewConfidential, int? requestingUserId, int take = 30)
    {
        var party = await _context.CorrespondentParties.AsNoTracking().FirstOrDefaultAsync(p => p.CorrespondentPartyId == partyId);
        if (party == null) return null;

        var rows = await PartyLettersAsync(party, canViewConfidential, requestingUserId);
        var today = DateTime.Today;

        var ids = rows.Select(r => r.LetterId).ToList();
        var answeredIds = (await _context.Letters.Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
            .Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();

        var items = rows.Select(r =>
        {
            var answered = answeredIds.Contains(r.LetterId);
            return new PartyThreadItemDto
            {
                LetterId = r.LetterId,
                LetterNumber = r.LetterNumber,
                Direction = r.LetterType,
                Subject = r.Subject,
                Date = r.Date,
                Status = r.Status,
                SenderReference = r.SenderReference,
                ResponseRequired = r.ResponseRequired,
                ResponseDueDate = r.ResponseDueDate,
                Answered = answered,
                Overdue = LetterResponseRules.IsOverdue(r.ResponseRequired, r.ResponseDueDate, answered, r.Status, today)
            };
        }).ToList();

        bool Open(PartyThreadItemDto i) => i.ResponseRequired && !i.Answered && !LetterResponseRules.FinishedStatuses.Contains(i.Status);

        return new PartyThreadDto
        {
            PartyId = party.CorrespondentPartyId,
            Name = party.Name,
            PartyType = party.PartyType,
            AwaitingUs = items.Count(i => i.Direction == "Incoming" && Open(i)),
            AwaitingThem = items.Count(i => i.Direction == "Outgoing" && Open(i)),
            LastContact = items.Count == 0 ? null : items.Max(i => i.Date),
            Total = items.Count,
            Items = items.Take(Math.Clamp(take, 1, 100)).ToList()
        };
    }

    public async Task<List<OpenOutgoingDto>> GetOutgoingForReplyAsync(int partyId, bool canViewConfidential, int? requestingUserId)
    {
        var party = await _context.CorrespondentParties.AsNoTracking().FirstOrDefaultAsync(p => p.CorrespondentPartyId == partyId);
        if (party == null) return new List<OpenOutgoingDto>();

        var rows = (await PartyLettersAsync(party, canViewConfidential, requestingUserId)).Where(r => r.LetterType == "Outgoing").ToList();
        var ids = rows.Select(r => r.LetterId).ToList();
        var answeredIds = (await _context.Letters.Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
            .Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();

        return rows
            .Select(r => new OpenOutgoingDto
            {
                LetterId = r.LetterId,
                LetterNumber = r.LetterNumber,
                Subject = r.Subject,
                Date = r.Date,
                ResponseDueDate = r.ResponseDueDate,
                AwaitingReply = r.ResponseRequired && !answeredIds.Contains(r.LetterId) && !LetterResponseRules.FinishedStatuses.Contains(r.Status)
            })
            .OrderByDescending(r => r.AwaitingReply).ThenByDescending(r => r.Date)
            .Take(50)
            .ToList();
    }

    public async Task<(int LetterId, string LetterNumber)?> FindDuplicateAsync(int partyId, string senderReference)
    {
        var reference = (senderReference ?? "").Trim().ToLower();
        if (reference.Length == 0) return null;

        var hit = await _context.Letters.AsNoTracking()
            .Where(l => l.IsActive && l.LetterType == "Incoming" && l.CorrespondentPartyId == partyId
                        && l.SenderReference != null && l.SenderReference.ToLower() == reference)
            .OrderBy(l => l.LetterId)
            .Select(l => new { l.LetterId, l.LetterNumber })
            .FirstOrDefaultAsync();
        return hit == null ? null : (hit.LetterId, hit.LetterNumber);
    }

    public async Task<List<LinkSearchItemDto>> SearchLinksAsync(string linkType, string? query, int? projectId, int take = 15)
    {
        var q = (query ?? "").Trim().ToLower();
        take = Math.Clamp(take, 1, 50);

        switch (linkType)
        {
            case "EngineeringDocument":
            {
                var docs = _context.EngineeringDocuments.AsNoTracking().Where(d => d.IsActive);
                if (projectId.HasValue) docs = docs.Where(d => d.ProjectId == projectId.Value);
                if (q.Length > 0) docs = docs.Where(d => d.DocumentNo.ToLower().Contains(q) || d.Title.ToLower().Contains(q));
                return await docs.OrderByDescending(d => d.CreatedDate).Take(take)
                    .Select(d => new LinkSearchItemDto { LinkType = linkType, TargetId = d.DocumentId, Number = d.DocumentNo, Title = d.Title }).ToListAsync();
            }
            case "VdrItem":
            {
                var items = _context.VDRItems.AsNoTracking().Where(d => d.IsActive);
                if (projectId.HasValue) items = items.Where(d => d.ProjectId == projectId.Value);
                if (q.Length > 0) items = items.Where(d => d.DocumentNo.ToLower().Contains(q) || d.Title.ToLower().Contains(q));
                return await items.OrderByDescending(d => d.CreatedDate).Take(take)
                    .Select(d => new LinkSearchItemDto { LinkType = linkType, TargetId = d.DocumentId, Number = d.DocumentNo, Title = d.Title }).ToListAsync();
            }
            case "MaterialRequisition":
            {
                var mrs = _context.MaterialRequisitions.AsNoTracking().Where(m => m.IsActive);
                if (projectId.HasValue) mrs = mrs.Where(m => m.ProjectId == projectId.Value);
                if (q.Length > 0) mrs = mrs.Where(m => m.MRNumber.ToLower().Contains(q));
                return await mrs.OrderByDescending(m => m.CreatedDate).Take(take)
                    .Select(m => new LinkSearchItemDto { LinkType = linkType, TargetId = m.MRId, Number = m.MRNumber }).ToListAsync();
            }
            case "Transmittal":
            {
                var ts = _context.Transmittals.AsNoTracking().Where(t => t.IsActive);
                if (projectId.HasValue) ts = ts.Where(t => t.ProjectId == projectId.Value);
                if (q.Length > 0) ts = ts.Where(t => t.TransmittalNo.ToLower().Contains(q) || (t.Description != null && t.Description.ToLower().Contains(q)));
                return await ts.OrderByDescending(t => t.TransmittalDate).Take(take)
                    .Select(t => new LinkSearchItemDto { LinkType = linkType, TargetId = t.TransmittalId, Number = t.TransmittalNo, Title = t.Description }).ToListAsync();
            }
            default:
                return new List<LinkSearchItemDto>();
        }
    }

    public async Task<List<PersonSearchItemDto>> SearchPeopleAsync(string? query, int take = 15)
    {
        var q = (query ?? "").Trim().ToLower();
        var people = _context.Employees.AsNoTracking().Where(e => e.IsActive);
        if (q.Length > 0)
            people = people.Where(e => (e.FirstName + " " + e.LastName).ToLower().Contains(q) || (e.PersonnelCode != null && e.PersonnelCode.ToLower().Contains(q)));

        return await people.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Take(Math.Clamp(take, 1, 50))
            .Select(e => new PersonSearchItemDto
            {
                EmployeeId = e.EmployeeId,
                Name = e.FirstName + " " + e.LastName,
                Position = e.Position,
                Department = e.Department != null ? e.Department.Name : null
            }).ToListAsync();
    }

    public async Task<int> SeedPartiesFromVendorsAsync()
    {
        var existingVendorIds = await _context.CorrespondentParties.Where(p => p.VendorId != null).Select(p => p.VendorId!.Value).ToListAsync();
        var vendors = await _context.Vendors.Where(v => v.IsActive && !existingVendorIds.Contains(v.VendorId)).ToListAsync();

        foreach (var v in vendors)
        {
            _context.CorrespondentParties.Add(new CorrespondentParty
            {
                Name = v.Name,
                Code = v.VendorCode,
                PartyType = "Vendor",
                VendorId = v.VendorId,
                IsProvisional = false,
                IsActive = true
            });
        }

        if (vendors.Count > 0) await _context.SaveChangesAsync();
        return vendors.Count;
    }

    // ------------------------------------------------------------------ کمکی

    private sealed record PartyLetterRow(int LetterId, string LetterNumber, string LetterType, string Subject, DateTime Date, string Status,
        string? SenderReference, bool ResponseRequired, DateTime? ResponseDueDate);

    // نامه‌های ارسال‌شده/دریافت‌شده‌ی یک طرف، با همان قانون محرمانگی فهرست نامه‌ها. نامه‌ی صادره‌ای که طرف مکاتبه‌اش ثبت نشده (قدیمی) با تطبیق نام گیرنده پیدا می‌شود.
    private async Task<List<PartyLetterRow>> PartyLettersAsync(CorrespondentParty party, bool canViewConfidential, int? requestingUserId)
    {
        var name = party.Name.ToLower();
        var pre = LetterAccessRules.PreSendStatuses;

        IQueryable<Letter> query = _context.Letters.AsNoTracking().Where(l => l.IsActive && !pre.Contains(l.Status)
            && (l.LetterType == "Incoming" || l.LetterType == "Outgoing")
            && (l.CorrespondentPartyId == party.CorrespondentPartyId
                || (l.CorrespondentPartyId == null
                    && ((l.LetterType == "Outgoing" && l.Receiver != null && l.Receiver.ToLower() == name)
                        || (l.LetterType == "Incoming" && l.Sender != null && l.Sender.ToLower() == name)))));

        query = await LetterVisibility.ApplyAsync(_context, query, canViewConfidential, requestingUserId);

        return await query.OrderByDescending(l => l.LetterDate).Take(500)
            .Select(l => new PartyLetterRow(l.LetterId, l.LetterNumber, l.LetterType, l.Subject, l.LetterDate, l.Status,
                l.SenderReference, l.ResponseRequired, l.ResponseDueDate))
            .ToListAsync();
    }

    private async Task<List<LetterLink>> ResolveLinksAsync(IEnumerable<IncomingLinkInput> requested, List<LetterComposeIssue> issues)
    {
        var result = new List<LetterLink>();
        foreach (var link in requested.Distinct())
        {
            string? number = link.LinkType switch
            {
                "EngineeringDocument" => await _context.EngineeringDocuments.Where(d => d.DocumentId == link.TargetId && d.IsActive).Select(d => d.DocumentNo).FirstOrDefaultAsync(),
                "VdrItem" => await _context.VDRItems.Where(d => d.DocumentId == link.TargetId && d.IsActive).Select(d => d.DocumentNo).FirstOrDefaultAsync(),
                "MaterialRequisition" => await _context.MaterialRequisitions.Where(m => m.MRId == link.TargetId).Select(m => m.MRNumber).FirstOrDefaultAsync(),
                "Transmittal" => await _context.Transmittals.Where(t => t.TransmittalId == link.TargetId).Select(t => t.TransmittalNo).FirstOrDefaultAsync(),
                _ => null
            };

            if (number == null) { issues.Add(new("LinkNotFound", "links", $"{link.LinkType}:{link.TargetId}")); continue; }
            result.Add(new LetterLink { LinkType = link.LinkType, TargetId = link.TargetId, TargetNumber = number });
        }
        return result;
    }

    private static string? ToHtml(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return null;
        var paragraphs = summary.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => "<p>" + WebUtility.HtmlEncode(p.Trim()) + "</p>");
        return string.Concat(paragraphs);
    }

    private async Task DeleteStagedAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { await _storage.DeleteAsync(key); }
            catch (Exception) { /* پاکسازی بهترین‌تلاش است؛ خطای اصلی مهم‌تر است */ }
        }
    }

    private async Task NotifyAsync(Letter letter, Employee? owner, IReadOnlyCollection<int> ccIds)
    {
        if (_notifications == null) return;

        try
        {
            var message = $"{letter.LetterNumber} - {letter.Subject}"
                + (letter.ResponseRequired && letter.ResponseDueDate.HasValue ? $" (response required by {letter.ResponseDueDate:yyyy-MM-dd})" : "");
            var link = $"/Letter/Details/{letter.LetterId}";

            if (owner != null)
            {
                var ownerUserId = await _context.Users.Where(u => u.EmployeeId == owner.EmployeeId).Select(u => (int?)u.Id).FirstOrDefaultAsync();
                if (ownerUserId.HasValue)
                    await _notifications.CreateAsync(ownerUserId.Value, "نامه‌ای به شما ارجاع شد", message, link, "Letter", letter.LetterId);
            }

            if (ccIds.Count > 0)
            {
                var ccUserIds = await _context.Users.Where(u => u.IsActive && u.EmployeeId != null && ccIds.Contains(u.EmployeeId.Value)).Select(u => u.Id).ToListAsync();
                foreach (var userId in ccUserIds)
                    await _notifications.CreateAsync(userId, "New letter received", message, link, "Letter", letter.LetterId);
            }
        }
        catch (Exception)
        {
            // اعلان بهترین‌تلاش است
        }
    }

    private async Task IndexAsync(Letter letter)
    {
        try
        {
            var plainBody = LetterComposeRules.PlainText(letter.Body);
            var content = string.Join(" ", new[] { letter.LetterNumber, letter.SenderReference, letter.Subject, plainBody }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(content))
                await _knowledgeBase.IndexAsync("Letter", letter.LetterId, content);
        }
        catch (Exception)
        {
            // خطای Indexing نباید ثبت نامه را متوقف کند
        }
    }
}
