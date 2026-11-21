namespace PEMS.Application.Interfaces;

public sealed record DisciplineRow(int DisciplineId, string Code, string Name, string? Description, string? ImportAliases, string? TransmittalCode,
    bool IsActive, int EmployeeCount, int DocumentCount);

public sealed record DisciplineOpResult(bool Success, string? ErrorKey = null, DisciplineDetail? Detail = null)
{
    public static DisciplineOpResult Ok() => new(true);
    public static DisciplineOpResult Ok(DisciplineDetail detail) => new(true, null, detail);
    public static DisciplineOpResult Fail(string key) => new(false, key);
}

/// <summary>One discipline as the REST API shows it (SCRUM-435 pilot resource); <c>RowVersion</c> is the ETag source.</summary>
public sealed record DisciplineDetail(int DisciplineId, string Code, string Name, string? Description, string? ImportAliases, string? TransmittalCode,
    bool IsActive, DateTime CreatedDate, byte[]? RowVersion);

/// <summary>The writable fields of a discipline; PUT sends all of them, PATCH starts from the current values.</summary>
public sealed record DisciplineWrite(string Code, string Name, string? Description, string? ImportAliases, string? TransmittalCode, bool IsActive = true);

/// <summary>List query of the REST contract: text search, status filter, sort (<c>code</c>, <c>name</c>, <c>createdDate</c>) and offset paging.</summary>
public sealed record DisciplineQuery(string? Q, bool? IsActive, string? TransmittalCode, IReadOnlyList<(string Field, bool Descending)> Sort, int Page, int PageSize);

public sealed record DisciplinePageResult(IReadOnlyList<DisciplineDetail> Items, int Total);

public static class DisciplineErrorKeys
{
    public const string Concurrency = "Disc_Err_Concurrency";
    public const string InUse = "Disc_Err_InUse";
}

/// <summary>
/// Master-data مدیریت دیسیپلین روی همان Entity موجود (Discipline). دیسیپلین «زمینه‌ی حرفه‌ای» است، نه واحد سازمانی و نه نقش مجوز.
/// حذف فیزیکی وجود ندارد (FK از Employee/Document/Team/...)؛ فقط غیرفعال/فعال‌سازی. Code و Name یکتا؛ Aliasها نباید با Code/Name/Alias دیسیپلین دیگر تداخل کنند (Import بدون ابهام).
/// </summary>
public interface IDisciplineMasterService
{
    [PEMS.Application.Common.PemsOperation("DisciplineMaster", "List", "DisciplineMaster.View")]   // SCRUM-682: the master-data page reads, creates and edits through these operations
    Task<List<DisciplineRow>> GetAllAsync(CancellationToken ct = default);
    [PEMS.Application.Common.PemsOperation("DisciplineMaster", "Create", "DisciplineMaster.Create")]
    Task<DisciplineOpResult> CreateAsync(string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default);
    [PEMS.Application.Common.PemsOperation("DisciplineMaster", "Update", "DisciplineMaster.Edit")]
    Task<DisciplineOpResult> UpdateAsync(int disciplineId, string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default);
    [PEMS.Application.Common.PemsOperation("DisciplineMaster", "SetActive", "DisciplineMaster.Edit")]
    Task<DisciplineOpResult> SetActiveAsync(int disciplineId, bool isActive, CancellationToken ct = default);

    // REST API (SCRUM-435): same rules as the pages above, plus row version and a result that carries the stored record.
    Task<DisciplineDetail?> GetAsync(int disciplineId, CancellationToken ct = default);
    Task<DisciplinePageResult> QueryAsync(DisciplineQuery query, CancellationToken ct = default);
    Task<DisciplineOpResult> CreateAsync(DisciplineWrite write, CancellationToken ct = default);
    /// <summary>Replaces the writable fields; <paramref name="expectedRowVersion"/> not matching the stored one gives <see cref="DisciplineErrorKeys.Concurrency"/>.</summary>
    Task<DisciplineOpResult> ReplaceAsync(int disciplineId, DisciplineWrite write, byte[]? expectedRowVersion, CancellationToken ct = default);
    /// <summary>Physical delete; a discipline that other records point to gives <see cref="DisciplineErrorKeys.InUse"/> (deactivate it instead).</summary>
    Task<DisciplineOpResult> DeleteAsync(int disciplineId, byte[]? expectedRowVersion, CancellationToken ct = default);
}
