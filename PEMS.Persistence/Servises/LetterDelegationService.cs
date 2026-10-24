using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterDelegationService : ILetterDelegationService
{
    private readonly PEMSDbContext _ctx;
    private readonly IOrganizationSubordinateResolver _org;
    private readonly INotificationService? _notifications;

    public LetterDelegationService(PEMSDbContext ctx, IOrganizationSubordinateResolver org, INotificationService? notifications = null)
    {
        _ctx = ctx;
        _org = org;
        _notifications = notifications;
    }

    public async Task<(List<LetterDelegationDto> Given, List<LetterDelegationDto> Received)> GetAsync(int userId)
    {
        var rows = await _ctx.LetterDelegations.AsNoTracking()
            .Where(d => d.DelegatorUserId == userId || d.DelegateUserId == userId)
            .OrderByDescending(d => d.FromDate).Take(100).ToListAsync();
        var userIds = rows.SelectMany(r => new[] { r.DelegatorUserId, r.DelegateUserId }).Distinct().ToList();
        var users = await (from u in _ctx.Users.AsNoTracking() where userIds.Contains(u.Id)
                           join e in _ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId into ge
                           from e in ge.DefaultIfEmpty()
                           select new { u.Id, u.FullName, Position = e != null ? e.Position : null }).ToDictionaryAsync(x => x.Id);

        var now = DateTime.Now;
        LetterDelegationDto Map(LetterDelegation d) => new()
        {
            LetterDelegationId = d.LetterDelegationId,
            DelegatorUserId = d.DelegatorUserId, DelegatorName = users.GetValueOrDefault(d.DelegatorUserId)?.FullName ?? "#" + d.DelegatorUserId,
            DelegateUserId = d.DelegateUserId, DelegateName = users.GetValueOrDefault(d.DelegateUserId)?.FullName ?? "#" + d.DelegateUserId,
            DelegatePosition = users.GetValueOrDefault(d.DelegateUserId)?.Position,
            FromDate = d.FromDate, ToDate = d.ToDate, Note = d.Note, IsActive = d.IsActive,
            IsEffectiveNow = LetterDelegationRules.IsEffective(new LetterDelegationRules.Span(d.DelegatorUserId, d.DelegateUserId, d.FromDate, d.ToDate, d.IsActive), now)
        };
        return (rows.Where(r => r.DelegatorUserId == userId).Select(Map).ToList(), rows.Where(r => r.DelegateUserId == userId).Select(Map).ToList());
    }

    public async Task<List<DelegateCandidateDto>> GetCandidatesAsync(int userId)
    {
        var myEmployeeId = await _ctx.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        var rel = myEmployeeId.HasValue ? await _org.GetRelationsAsync(myEmployeeId.Value) : OrgRelations.Empty;
        var managers = rel.ManagerEmployeeIds.ToHashSet();
        var subs = rel.SubordinateIds;
        var peers = rel.PeerEmployeeIds.ToHashSet();

        var rows = await (from u in _ctx.Users.AsNoTracking()
                          join e in _ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId
                          join d in _ctx.Departments.AsNoTracking() on e.DepartmentId equals d.DepartmentId into gd
                          from d in gd.DefaultIfEmpty()
                          where u.IsActive && e.IsActive && u.Id != userId
                          select new { UserId = u.Id, e.EmployeeId, u.FullName, e.Position, Department = d != null ? d.Name : null })
            .OrderBy(x => x.FullName).Take(500).ToListAsync();

        return rows.Select(r => new DelegateCandidateDto
        {
            UserId = r.UserId, EmployeeId = r.EmployeeId, Name = r.FullName, Position = r.Position, Department = r.Department,
            Relation = managers.Contains(r.EmployeeId) ? "Manager" : subs.Contains(r.EmployeeId) ? "Subordinate" : peers.Contains(r.EmployeeId) ? "Peer" : "Other"
        }).OrderBy(c => c.Relation switch { "Manager" => 0, "Peer" => 1, "Subordinate" => 2, _ => 3 }).ThenBy(c => c.Name).ToList();
    }

    public async Task<CartableOpResult> CreateAsync(int delegatorUserId, int delegateUserId, DateTime fromDate, DateTime toDate, string? note)
    {
        var delegateOk = await (from u in _ctx.Users join e in _ctx.Employees on u.EmployeeId equals e.EmployeeId
                                where u.Id == delegateUserId && u.IsActive && e.IsActive select u.Id).AnyAsync();
        if (!delegateOk) return CartableOpResult.Fail("Del_Err_DelegateInvalid");

        var existing = await _ctx.LetterDelegations.AsNoTracking()
            .Where(d => d.IsActive && (d.DelegatorUserId == delegatorUserId || d.DelegatorUserId == delegateUserId))
            .Select(d => new LetterDelegationRules.Span(d.DelegatorUserId, d.DelegateUserId, d.FromDate, d.ToDate, d.IsActive)).ToListAsync();
        var err = LetterDelegationRules.Validate(delegatorUserId, delegateUserId, fromDate, toDate, DateTime.Now, existing);
        if (err != null) return CartableOpResult.Fail(err);

        var row = new LetterDelegation
        {
            DelegatorUserId = delegatorUserId, DelegateUserId = delegateUserId, FromDate = fromDate.Date, ToDate = toDate.Date,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(300, note.Trim().Length)], IsActive = true
        };
        _ctx.LetterDelegations.Add(row);
        await _ctx.SaveChangesAsync();

        if (_notifications != null)
        {
            try
            {
                var who = await _ctx.Users.AsNoTracking().Where(u => u.Id == delegatorUserId).Select(u => u.FullName).FirstOrDefaultAsync();
                await _notifications.CreateAsync(delegateUserId, "کارتابل نامه‌نگاری به شما تفویض شد",
                    $"{who}: {fromDate:yyyy-MM-dd} تا {toDate:yyyy-MM-dd}", "/Letter?folder=delegated", "LetterDelegation", row.LetterDelegationId);
            }
            catch { /* اعلان بهترین‌تلاش است؛ ثبت تفویض را خراب نمی‌کند */ }
        }
        return CartableOpResult.Ok(1, row.LetterDelegationId);
    }

    public async Task<CartableOpResult> RevokeAsync(int delegationId, int userId)
    {
        var row = await _ctx.LetterDelegations.FirstOrDefaultAsync(d => d.LetterDelegationId == delegationId && d.DelegatorUserId == userId && d.IsActive);
        if (row == null) return CartableOpResult.Fail("Del_Err_NotFound");
        row.IsActive = false;
        row.RevokedDate = DateTime.Now;
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }
}
