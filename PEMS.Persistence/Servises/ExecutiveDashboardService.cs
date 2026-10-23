using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// داشبورد مدیریتی. مدارک مهندسی مستقیم و با Query های گروه‌بندی (نه بارگذاری همه‌ی مدارک در حافظه) محاسبه می‌شوند؛ VDR، خرید، CMMS و مکاتبات از همان سرویس‌های گزارش
/// موجود می‌آیند تا عدد داشبورد با صفحه‌ی گزارش آن ماژول یکی باشد. قاعده‌ی دیسیپلین عیناً قاعده‌ی لیست مدارک مهندسی است.
/// </summary>
public class ExecutiveDashboardService : IExecutiveDashboardService
{
    private const int TopDisciplines = 10;
    private const int TopProjects = 5;
    private const int TopResponseCodes = 8;
    private const int TopPressure = 8;

    private readonly PEMSDbContext _context;
    private readonly IVDRReportService _vdr;
    private readonly IProcurementReportService _procurement;
    private readonly ICmmsReportService _cmms;
    private readonly ICorrespondenceReportService _correspondence;
    private readonly ILogger<ExecutiveDashboardService> _logger;

    public ExecutiveDashboardService(PEMSDbContext context, IVDRReportService vdr, IProcurementReportService procurement,
        ICmmsReportService cmms, ICorrespondenceReportService correspondence, ILogger<ExecutiveDashboardService> logger)
    {
        _context = context;
        _vdr = vdr;
        _procurement = procurement;
        _cmms = cmms;
        _correspondence = correspondence;
        _logger = logger;
    }

    public async Task<ExecutiveDashboardDto> GetAsync(ExecutiveDashboardRequest request)
    {
        var dto = new ExecutiveDashboardDto { ProjectId = request.ProjectId, GeneratedAt = DateTime.Now };

        if (request.CanViewEngineering)
        {
            await Section(dto, "Engineering", async () =>
            {
                dto.Engineering = await BuildEngineeringAsync(request);
                dto.Projects = await BuildProjectOptionsAsync(request);
            });
        }

        if (request.CanViewVdr) await Section(dto, "Vdr", async () => dto.Vdr = await BuildVdrAsync(request));
        if (request.CanViewProcurement)
        {
            await Section(dto, "Procurement", async () => dto.Procurement = await BuildProcurementAsync(request));
            await Section(dto, "Transmittals", async () => dto.Transmittals = await BuildTransmittalsAsync(request));
        }
        if (request.CanViewCmms) await Section(dto, "Operations", async () => dto.Operations = await BuildOperationsAsync());
        if (request.CanViewCorrespondence) await Section(dto, "Correspondence", async () => dto.Correspondence = await BuildCorrespondenceAsync(request));

        dto.Attention = BuildAttention(dto);
        return dto;
    }

    private async Task Section(ExecutiveDashboardDto dto, string name, Func<Task> build)
    {
        try { await build(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Executive dashboard section {Section} failed", name);
            dto.FailedSections.Add(name);
        }
    }

    // ------------------------------------------------------------------ engineering

    private IQueryable<EngineeringDocument> Docs(ExecutiveDashboardRequest r)
    {
        var q = _context.EngineeringDocuments.AsNoTracking().Where(x => x.IsActive);
        if (r.ProjectId.HasValue) q = q.Where(x => x.ProjectId == r.ProjectId.Value);

        // کارمند بدون ViewAllDisciplines: دیسیپلین خودش + مدارک بدون دیسیپلین (نه نامحدود، نه صفر)
        if (r.RestrictToOwnDiscipline)
        {
            var own = r.OwnDisciplineId;
            q = q.Where(x => x.DisciplineId == own || x.DisciplineId == null);
        }
        return q;
    }

    private static int Percent(int part, int whole) => whole <= 0 ? 0 : (int)Math.Round(100.0 * part / whole);

    private async Task<DashboardEngineeringDto> BuildEngineeringAsync(ExecutiveDashboardRequest r)
    {
        var docs = Docs(r);
        var e = new DashboardEngineeringDto();

        // ---- بخش‌های وضعیت
        var statusRows = await docs.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        foreach (var row in statusRows)
        {
            e.Total += row.Count;
            switch (EngineeringDocumentDefinitions.BucketOf(row.Status))
            {
                case EngineeringDocumentDefinitions.BucketApproved: e.Approved += row.Count; break;
                case EngineeringDocumentDefinitions.BucketInReview: e.InReview += row.Count; break;
                case EngineeringDocumentDefinitions.BucketReturned: e.Returned += row.Count; break;
                case EngineeringDocumentDefinitions.BucketDraft: e.Draft += row.Count; break;
                default: e.Other += row.Count; break;
            }
        }
        e.ApprovedPercent = Percent(e.Approved, e.Total);
        e.Buckets = new List<DashboardBucketDto>
        {
            new() { Key = EngineeringDocumentDefinitions.BucketApproved, Value = e.Approved },
            new() { Key = EngineeringDocumentDefinitions.BucketInReview, Value = e.InReview },
            new() { Key = EngineeringDocumentDefinitions.BucketReturned, Value = e.Returned },
            new() { Key = EngineeringDocumentDefinitions.BucketDraft, Value = e.Draft },
            new() { Key = EngineeringDocumentDefinitions.BucketOther, Value = e.Other }
        }.Where(b => b.Value > 0).ToList();

        if (e.Total == 0) return e;

        e.ProjectCount = await docs.Select(x => x.ProjectId).Distinct().CountAsync();
        e.RejectedTrue = await docs.CountAsync(EngineeringDocumentDefinitions.Rejected);
        e.ReturnedWithComments = await docs.CountAsync(EngineeringDocumentDefinitions.ReturnedWithComments);
        var now = DateTime.Now;
        e.Overdue = await docs.CountAsync(x => x.PlannedDate != null && x.ActualDate == null && x.PlannedDate < now);

        // ---- دیسیپلین‌ها
        var approvedStatuses = EngineeringDocumentDefinitions.ApprovedStatuses;
        var inReviewStatuses = EngineeringDocumentDefinitions.InReviewStatuses;
        var disciplineRows = await docs.GroupBy(x => x.DisciplineId).Select(g => new
        {
            DisciplineId = g.Key,
            Total = g.Count(),
            Approved = g.Count(x => approvedStatuses.Contains(x.Status)),
            InReview = g.Count(x => inReviewStatuses.Contains(x.Status)),
            Returned = g.Count(x => x.Status == "Rejected"),
            Draft = g.Count(x => x.Status == "Draft")
        }).OrderByDescending(x => x.Total).Take(TopDisciplines).ToListAsync();
        var disciplineIds = disciplineRows.Where(x => x.DisciplineId != null).Select(x => x.DisciplineId!.Value).ToList();
        var disciplineNames = await _context.Disciplines.AsNoTracking().Where(d => disciplineIds.Contains(d.DisciplineId))
            .ToDictionaryAsync(d => d.DisciplineId, d => d.Name);
        e.Disciplines = disciplineRows.Select(x => new DashboardDisciplineRowDto
        {
            Name = x.DisciplineId.HasValue && disciplineNames.TryGetValue(x.DisciplineId.Value, out var n) ? n : string.Empty,   // empty = unassigned (UI translates)
            Total = x.Total, Approved = x.Approved, InReview = x.InReview, Returned = x.Returned, Draft = x.Draft,
            ApprovedPercent = Percent(x.Approved, x.Total)
        }).ToList();

        // ---- پروژه‌ها (وقتی روی یک پروژه فیلتر نشده): چند پروژه‌ی بزرگ + یک ردیف «سایر» تا هیچ داده‌ای پنهان نشود
        if (!r.ProjectId.HasValue)
        {
            var projectRows = await docs.GroupBy(x => x.ProjectId).Select(g => new
            {
                ProjectId = g.Key, Total = g.Count(), Approved = g.Count(x => approvedStatuses.Contains(x.Status))
            }).OrderByDescending(x => x.Total).ToListAsync();
            // «پروژه‌ی اصلی» = دست‌کم ۱٪ کل مدارک؛ بقیه (مثلاً صدها پروژه‌ی کوچک/آزمایشی) در یک ردیف «سایر» جمع می‌شوند تا رتبه‌بندی را شلوغ نکنند
            var top = projectRows.Where(x => x.Total * 100 >= e.Total).Take(TopProjects).ToList();
            if (top.Count == 0) top = projectRows.Take(1).ToList();
            var rest = projectRows.Where(x => !top.Contains(x)).ToList();
            var pids = top.Select(x => x.ProjectId).ToList();
            var pnames = await _context.Projects.AsNoTracking().Where(p => pids.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => p.ProjectName);
            e.Projects = top.Select(x => new DashboardProjectRowDto
            {
                ProjectId = x.ProjectId, Name = pnames.GetValueOrDefault(x.ProjectId, "#" + x.ProjectId), Total = x.Total, Approved = x.Approved,
                ApprovedPercent = Percent(x.Approved, x.Total)
            }).ToList();
            if (rest.Count > 0)
            {
                var total = rest.Sum(x => x.Total);
                var approved = rest.Sum(x => x.Approved);
                // ProjectId 0 = «سایر پروژه‌ها»؛ تعداد پروژه‌ها در OthersCount
                e.Projects.Add(new DashboardProjectRowDto { ProjectId = 0, Name = string.Empty, Total = total, Approved = approved, ApprovedPercent = Percent(approved, total), OthersCount = rest.Count });
            }
        }

        // ---- پاسخ کارفرما (کد خام؛ معنی کدها را سازمان تعیین می‌کند، اینجا چیزی اختراع نمی‌شود)
        var responses = await docs.Where(x => x.CommentStatus != null && x.CommentStatus != "")
            .GroupBy(x => x.CommentStatus!).Select(g => new { Code = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(TopResponseCodes).ToListAsync();
        e.ClientResponses = responses.Select(x => new DashboardCountDto { Label = x.Code, Value = x.Count }).ToList();

        // ---- رویژن‌ها
        var revisions = from rev in _context.DocumentRevisions.AsNoTracking()
                        where rev.IsActive
                        join d in docs on rev.DocumentId equals d.DocumentId
                        select rev;
        e.TotalRevisions = await revisions.CountAsync();
        e.RevisionsPerDocument = Math.Round((double)e.TotalRevisions / e.Total, 2);
        e.IssuedRevisions = await revisions.CountAsync(x => x.IssuedDate != null);

        var issuedMonths = await revisions.Where(x => x.IssuedDate != null)
            .GroupBy(x => new { x.IssuedDate!.Value.Year, x.IssuedDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        e.Issuance = FillMonths(issuedMonths.Select(x => (new DateTime(x.Year, x.Month, 1), x.Count)));

        var pressure = await revisions.GroupBy(x => x.DocumentId).Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.DocumentId).Take(TopPressure).ToListAsync();
        var pressureIds = pressure.Select(x => x.DocumentId).ToList();
        var pressureDocs = await _context.EngineeringDocuments.AsNoTracking().Where(d => pressureIds.Contains(d.DocumentId))
            .Select(d => new { d.DocumentId, d.DocumentNo, d.Title, d.Status }).ToDictionaryAsync(d => d.DocumentId);
        e.RevisionPressure = pressure.Where(p => pressureDocs.ContainsKey(p.DocumentId)).Select(p => new DashboardRevisionPressureDto
        {
            DocumentId = p.DocumentId, DocumentNo = pressureDocs[p.DocumentId].DocumentNo, Title = pressureDocs[p.DocumentId].Title,
            Status = pressureDocs[p.DocumentId].Status, Revisions = p.Count
        }).ToList();

        // ---- کامل‌بودن داده (اعتبار شاخص‌های وابسته)
        var withDiscipline = await docs.CountAsync(x => x.DisciplineId != null);
        var withPlanned = await docs.CountAsync(x => x.PlannedDate != null);
        var withResponse = await docs.CountAsync(x => x.CommentStatus != null && x.CommentStatus != "");
        var withIssued = await docs.CountAsync(x => _context.DocumentRevisions.Any(rv => rv.DocumentId == x.DocumentId && rv.IssuedDate != null));
        e.Completeness = new DashboardCompletenessDto
        {
            WithDisciplinePercent = Percent(withDiscipline, e.Total),
            WithPlannedDatePercent = Percent(withPlanned, e.Total),
            WithClientResponsePercent = Percent(withResponse, e.Total),
            WithIssuedRevisionPercent = Percent(withIssued, e.Total)
        };

        return e;
    }

    /// <summary>سری ماهانه‌ی بدون حفره (ماه بدون رویداد = صفر) به‌همراه جمع تجمعی؛ برای محور زمانی پیوسته.</summary>
    public static List<DashboardMonthPointDto> FillMonths(IEnumerable<(DateTime Month, int Count)> rows)
    {
        var map = rows.GroupBy(x => x.Month).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        if (map.Count == 0) return new List<DashboardMonthPointDto>();

        var first = map.Keys.Min();
        var last = map.Keys.Max();
        var result = new List<DashboardMonthPointDto>();
        var cumulative = 0;
        for (var m = first; m <= last; m = m.AddMonths(1))
        {
            var count = map.GetValueOrDefault(m);
            cumulative += count;
            result.Add(new DashboardMonthPointDto { Date = m, Issued = count, Cumulative = cumulative });
        }
        return result;
    }

    private async Task<List<DashboardProjectOptionDto>> BuildProjectOptionsAsync(ExecutiveDashboardRequest r)
    {
        // لیست انتخاب پروژه فیلتر پروژه را نادیده می‌گیرد (وگرنه با انتخاب یک پروژه، بقیه ناپدید می‌شدند)
        var all = new ExecutiveDashboardRequest { RestrictToOwnDiscipline = r.RestrictToOwnDiscipline, OwnDisciplineId = r.OwnDisciplineId };
        var rows = await Docs(all).GroupBy(x => x.ProjectId).Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(200).ToListAsync();
        var ids = rows.Select(x => x.ProjectId).ToList();
        var names = await _context.Projects.AsNoTracking().Where(p => ids.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => new { p.ProjectName, p.ProjectCode });
        return rows.Select(x => new DashboardProjectOptionDto
        {
            Id = x.ProjectId, Documents = x.Count,
            Name = names.TryGetValue(x.ProjectId, out var n) ? n.ProjectName : "#" + x.ProjectId,
            Code = names.TryGetValue(x.ProjectId, out var c) ? c.ProjectCode : null
        }).ToList();
    }

    // ------------------------------------------------------------------ VDR / procurement / transmittals

    private async Task<DashboardVdrDto> BuildVdrAsync(ExecutiveDashboardRequest r)
    {
        var report = await _vdr.GetReportAsync(r.ProjectId);
        return new DashboardVdrDto
        {
            Total = report.TotalItems, Closed = report.ClosedCount, UnderReview = report.UnderReviewCount,
            PendingResubmission = report.PendingResubmissionCount, Overdue = report.OverdueCount, Submissions = report.TotalSubmissions,
            ByWorkflow = report.ByWorkflowStatus.Select(x => new DashboardCountDto { Label = x.Status, Value = x.Count }).ToList(),
            ByVendor = report.ByVendor.Select(x => new DashboardCountDto { Label = x.Vendor, Value = x.Count }).ToList()
        };
    }

    private async Task<DashboardProcurementDto> BuildProcurementAsync(ExecutiveDashboardRequest r)
    {
        var report = await _procurement.GetReportAsync(r.ProjectId);
        var mrs = _context.MaterialRequisitions.AsNoTracking().Where(x => x.IsActive);
        if (r.ProjectId.HasValue) mrs = mrs.Where(x => x.ProjectId == r.ProjectId.Value);
        var mrRows = await mrs.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();

        var pos = _context.PurchaseOrders.AsNoTracking().Where(x => x.IsActive && x.Status != PurchaseOrderStatuses.Cancelled && x.Status != PurchaseOrderStatuses.Draft);
        if (r.ProjectId.HasValue) pos = pos.Where(x => x.ProjectId == r.ProjectId.Value);
        var poRows = await pos.GroupBy(x => x.Currency).Select(g => new { Currency = g.Key, Value = g.Sum(x => (decimal?)x.Value) ?? 0m, Count = g.Count() })
            .OrderByDescending(x => x.Value).ToListAsync();

        return new DashboardProcurementDto
        {
            Packages = report.TotalPackages, Tenders = report.TotalTenders, Bids = report.TotalBids, Contracts = report.TotalContracts,
            PurchaseOrders = poRows.Sum(x => x.Count), NtpsIssued = report.TotalNTPsIssued,
            MaterialRequisitions = mrRows.Sum(x => x.Count),
            OpenMaterialRequisitions = mrRows.Where(x => x.Status != "Closed" && x.Status != "Cancelled").Sum(x => x.Count),
            MrByStatus = mrRows.Select(x => new DashboardCountDto { Label = x.Status, Value = x.Count }).ToList(),
            PurchaseOrderValue = poRows.Select(x => new DashboardCurrencyValueDto { Currency = x.Currency ?? "", Value = x.Value, Count = x.Count }).ToList()
        };
    }

    private async Task<DashboardTransmittalDto> BuildTransmittalsAsync(ExecutiveDashboardRequest r)
    {
        var q = _context.Transmittals.AsNoTracking().Where(x => x.IsActive);
        if (r.ProjectId.HasValue) q = q.Where(x => x.ProjectId == r.ProjectId.Value);

        var total = await q.CountAsync();
        var sent = await q.CountAsync(x => x.Status == "Sent");
        var months = await q.GroupBy(x => new { x.TransmittalDate.Year, x.TransmittalDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();

        return new DashboardTransmittalDto
        {
            Total = total, Sent = sent,
            ByMonth = FillMonths(months.Select(x => (new DateTime(x.Year, x.Month, 1), x.Count)))
        };
    }

    // ------------------------------------------------------------------ operations / correspondence

    private async Task<DashboardOperationsDto> BuildOperationsAsync()
    {
        var report = await _cmms.GetReportAsync();
        return new DashboardOperationsDto
        {
            TotalWorkOrders = report.TotalWorkOrders,
            OpenWorkOrders = report.OpenCount + report.InProgressCount,
            CriticalWorkOrders = report.CriticalCount,
            AvgCompletionDays = report.AvgCompletionDays,
            Equipment = await _context.Equipments.AsNoTracking().CountAsync(),
            ByPriority = report.ByPriority.Select(x => new DashboardCountDto { Label = x.Priority, Value = x.Count }).ToList()
        };
    }

    private async Task<DashboardCorrespondenceDto> BuildCorrespondenceAsync(ExecutiveDashboardRequest r)
    {
        var report = await _correspondence.GetReportAsync(r.ProjectId);
        var today = DateTime.Today;
        var answered = LetterResponseRules.AnsweredChildStatuses;
        var finished = LetterResponseRules.FinishedStatuses;

        var letters = _context.Letters.AsNoTracking().Where(l => l.IsActive && l.ResponseRequired && l.ResponseDueDate != null && l.ResponseDueDate < today
            && !finished.Contains(l.Status)
            && !_context.Letters.Any(c => c.IsActive && c.ParentLetterId == l.LetterId && answered.Contains(c.Status)));
        if (r.ProjectId.HasValue) letters = letters.Where(l => l.ProjectId == r.ProjectId.Value);

        return new DashboardCorrespondenceDto
        {
            Total = report.TotalLetters, Incoming = report.TotalIncoming, Outgoing = report.TotalOutgoing,
            OverdueReferrals = report.OverdueReferrals, OverdueResponses = await letters.CountAsync()
        };
    }

    // ------------------------------------------------------------------ attention

    public static List<DashboardAttentionDto> BuildAttention(ExecutiveDashboardDto d)
    {
        var items = new List<DashboardAttentionDto>();
        void Add(string key, string severity, int count, string module)
        {
            if (count > 0) items.Add(new DashboardAttentionDto { Key = key, Severity = severity, Count = count, Module = module });
        }

        if (d.Engineering != null)
        {
            Add("EngRejected", "High", d.Engineering.RejectedTrue, "Engineering");
            Add("EngOverdue", "High", d.Engineering.Overdue, "Engineering");
            Add("EngReturned", "Medium", d.Engineering.ReturnedWithComments, "Engineering");
            Add("EngInReview", "Info", d.Engineering.InReview, "Engineering");
        }
        if (d.Vdr != null)
        {
            Add("VdrOverdue", "High", d.Vdr.Overdue, "Vdr");
            Add("VdrResubmission", "Medium", d.Vdr.PendingResubmission, "Vdr");
        }
        if (d.Correspondence != null)
        {
            Add("LetterResponseOverdue", "High", d.Correspondence.OverdueResponses, "Correspondence");
            Add("ReferralOverdue", "High", d.Correspondence.OverdueReferrals, "Correspondence");
        }
        if (d.Operations != null) Add("CriticalWorkOrders", "High", d.Operations.CriticalWorkOrders, "Cmms");
        if (d.Procurement != null)
        {
            var draftMr = d.Procurement.MrByStatus.Where(x => x.Label == "Draft").Sum(x => x.Value);
            Add("MrDraft", "Info", draftMr, "Procurement");
        }

        var rank = new Dictionary<string, int> { ["High"] = 0, ["Medium"] = 1, ["Info"] = 2 };
        return items.OrderBy(x => rank[x.Severity]).ThenByDescending(x => x.Count).ToList();
    }
}
