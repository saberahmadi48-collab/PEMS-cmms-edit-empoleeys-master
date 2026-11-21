using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class DisciplineMasterService : IDisciplineMasterService
{
    private readonly PEMSDbContext _ctx;

    public DisciplineMasterService(PEMSDbContext ctx) => _ctx = ctx;

    public async Task<List<DisciplineRow>> GetAllAsync(CancellationToken ct = default)
    {
        var empCounts = await _ctx.Employees.AsNoTracking().GroupBy(e => e.DisciplineId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var docCounts = await _ctx.EngineeringDocuments.AsNoTracking().Where(d => d.DisciplineId != null).GroupBy(d => d.DisciplineId!.Value).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var rows = await _ctx.Disciplines.AsNoTracking().OrderByDescending(d => d.IsActive).ThenBy(d => d.Name).ThenBy(d => d.Code).ToListAsync(ct);
        return rows.Select(d => new DisciplineRow(d.DisciplineId, d.Code, d.Name, d.Description, d.ImportAliases, d.TransmittalCode, d.IsActive,
            empCounts.GetValueOrDefault(d.DisciplineId), docCounts.GetValueOrDefault(d.DisciplineId))).ToList();
    }

    public async Task<DisciplineOpResult> CreateAsync(string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default)
    {
        var (err, c, n, aliases, tc) = await NormalizeAsync(null, code, name, importAliases, transmittalCode, ct);
        if (err != null) return DisciplineOpResult.Fail(err);
        _ctx.Disciplines.Add(new Discipline { Code = c, Name = n, Description = Clean(description), ImportAliases = aliases, TransmittalCode = tc, IsActive = true, CreatedDate = DateTime.Now });
        await _ctx.SaveChangesAsync(ct);
        return DisciplineOpResult.Ok();
    }

    public async Task<DisciplineOpResult> UpdateAsync(int disciplineId, string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default)
    {
        var d = await _ctx.Disciplines.FirstOrDefaultAsync(x => x.DisciplineId == disciplineId, ct);
        if (d == null) return DisciplineOpResult.Fail("Disc_Err_NotFound");
        var (err, c, n, aliases, tc) = await NormalizeAsync(disciplineId, code, name, importAliases, transmittalCode, ct);
        if (err != null) return DisciplineOpResult.Fail(err);
        d.Code = c; d.Name = n; d.Description = Clean(description); d.ImportAliases = aliases; d.TransmittalCode = tc;
        await _ctx.SaveChangesAsync(ct);
        return DisciplineOpResult.Ok();
    }

    public async Task<DisciplineOpResult> SetActiveAsync(int disciplineId, bool isActive, CancellationToken ct = default)
    {
        var d = await _ctx.Disciplines.FirstOrDefaultAsync(x => x.DisciplineId == disciplineId, ct);
        if (d == null) return DisciplineOpResult.Fail("Disc_Err_NotFound");
        d.IsActive = isActive;
        await _ctx.SaveChangesAsync(ct);
        return DisciplineOpResult.Ok();
    }

    private static DisciplineDetail ToDetail(Discipline d) =>
        new(d.DisciplineId, d.Code, d.Name, d.Description, d.ImportAliases, d.TransmittalCode, d.IsActive, d.CreatedDate, d.RowVersion);

    public async Task<DisciplineDetail?> GetAsync(int disciplineId, CancellationToken ct = default)
    {
        var d = await _ctx.Disciplines.AsNoTracking().FirstOrDefaultAsync(x => x.DisciplineId == disciplineId, ct);
        return d == null ? null : ToDetail(d);
    }

    public async Task<DisciplinePageResult> QueryAsync(DisciplineQuery query, CancellationToken ct = default)
    {
        IQueryable<Discipline> rows = _ctx.Disciplines.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var t = query.Q.Trim();
            rows = rows.Where(d => d.Code.Contains(t) || d.Name.Contains(t) || (d.Description ?? "").Contains(t) || (d.ImportAliases ?? "").Contains(t));
        }
        if (query.IsActive is { } active) rows = rows.Where(d => d.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.TransmittalCode)) { var tc = query.TransmittalCode.Trim().ToUpper(); rows = rows.Where(d => d.TransmittalCode == tc); }

        var total = await rows.CountAsync(ct);
        IOrderedQueryable<Discipline>? ordered = null;
        foreach (var (field, desc) in query.Sort)
        {
            ordered = (field.ToLowerInvariant(), ordered == null, desc) switch
            {
                ("code", true, false) => rows.OrderBy(d => d.Code),
                ("code", true, true) => rows.OrderByDescending(d => d.Code),
                ("code", false, false) => ordered!.ThenBy(d => d.Code),
                ("code", false, true) => ordered!.ThenByDescending(d => d.Code),
                ("name", true, false) => rows.OrderBy(d => d.Name),
                ("name", true, true) => rows.OrderByDescending(d => d.Name),
                ("name", false, false) => ordered!.ThenBy(d => d.Name),
                ("name", false, true) => ordered!.ThenByDescending(d => d.Name),
                ("createddate", true, false) => rows.OrderBy(d => d.CreatedDate),
                ("createddate", true, true) => rows.OrderByDescending(d => d.CreatedDate),
                ("createddate", false, false) => ordered!.ThenBy(d => d.CreatedDate),
                ("createddate", false, true) => ordered!.ThenByDescending(d => d.CreatedDate),
                _ => ordered,
            };
        }
        ordered = ordered == null ? rows.OrderBy(d => d.Name).ThenBy(d => d.DisciplineId) : ordered.ThenBy(d => d.DisciplineId);
        var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new DisciplinePageResult(items.Select(ToDetail).ToList(), total);
    }

    public async Task<DisciplineOpResult> CreateAsync(DisciplineWrite write, CancellationToken ct = default)
    {
        var (err, c, n, aliases, tc) = await NormalizeAsync(null, write.Code, write.Name, write.ImportAliases, write.TransmittalCode, ct);
        if (err != null) return DisciplineOpResult.Fail(err);
        var d = new Discipline { Code = c, Name = n, Description = Clean(write.Description), ImportAliases = aliases, TransmittalCode = tc, IsActive = write.IsActive, CreatedDate = DateTime.Now };
        _ctx.Disciplines.Add(d);
        await _ctx.SaveChangesAsync(ct);
        return DisciplineOpResult.Ok(ToDetail(d));
    }

    public async Task<DisciplineOpResult> ReplaceAsync(int disciplineId, DisciplineWrite write, byte[]? expectedRowVersion, CancellationToken ct = default)
    {
        var d = await _ctx.Disciplines.FirstOrDefaultAsync(x => x.DisciplineId == disciplineId, ct);
        if (d == null) return DisciplineOpResult.Fail("Disc_Err_NotFound");
        var (err, c, n, aliases, tc) = await NormalizeAsync(disciplineId, write.Code, write.Name, write.ImportAliases, write.TransmittalCode, ct);
        if (err != null) return DisciplineOpResult.Fail(err);
        if (expectedRowVersion != null) _ctx.Entry(d).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
        d.Code = c; d.Name = n; d.Description = Clean(write.Description); d.ImportAliases = aliases; d.TransmittalCode = tc; d.IsActive = write.IsActive;
        try { await _ctx.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return DisciplineOpResult.Fail(DisciplineErrorKeys.Concurrency); }
        return DisciplineOpResult.Ok(ToDetail(d));
    }

    public async Task<DisciplineOpResult> DeleteAsync(int disciplineId, byte[]? expectedRowVersion, CancellationToken ct = default)
    {
        var d = await _ctx.Disciplines.FirstOrDefaultAsync(x => x.DisciplineId == disciplineId, ct);
        if (d == null) return DisciplineOpResult.Fail("Disc_Err_NotFound");
        if (expectedRowVersion != null) _ctx.Entry(d).Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
        _ctx.Disciplines.Remove(d);
        try { await _ctx.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return DisciplineOpResult.Fail(DisciplineErrorKeys.Concurrency); }
        catch (DbUpdateException) { _ctx.Entry(d).State = EntityState.Unchanged; return DisciplineOpResult.Fail(DisciplineErrorKeys.InUse); }
        return DisciplineOpResult.Ok();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<(string? Error, string Code, string Name, string? Aliases, string? TransmittalCode)> NormalizeAsync(
        int? selfId, string code, string name, string? importAliases, string? transmittalCode, CancellationToken ct)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        var n = (name ?? "").Trim();
        if (c.Length == 0 || n.Length == 0) return ("Disc_Err_Required", c, n, null, null);
        if (c.Length > 20 || n.Length > 150) return ("Disc_Err_TooLong", c, n, null, null);

        var tc = Clean(transmittalCode)?.ToUpperInvariant();
        if (tc != null && (tc.Length != 1 || !char.IsLetter(tc[0]))) return ("Disc_Err_TransmittalCode", c, n, null, null);

        var aliases = (importAliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var others = await _ctx.Disciplines.AsNoTracking().Where(d => selfId == null || d.DisciplineId != selfId)
            .Select(d => new { d.Code, d.Name, d.ImportAliases }).ToListAsync(ct);
        if (others.Any(o => o.Code.Equals(c, StringComparison.OrdinalIgnoreCase))) return ("Disc_Err_DuplicateCode", c, n, null, null);
        if (others.Any(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) return ("Disc_Err_DuplicateName", c, n, null, null);

        // Alias یا Code/Name خودش با هر شناسه‌ی دیسیپلین دیگر نباید برابر باشد (وگرنه Import مبهم می‌شود)
        var foreign = others.SelectMany(o => new[] { o.Code, o.Name }.Concat((o.ImportAliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aliases.Any(a => foreign.Contains(a))) return ("Disc_Err_AliasConflict", c, n, null, null);

        return (null, c, n, aliases.Count == 0 ? null : string.Join(", ", aliases), tc);
    }
}
