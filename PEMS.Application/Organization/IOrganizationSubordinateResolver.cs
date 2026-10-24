namespace PEMS.Application.Organization;

public sealed record SubordinateInfo(int EmployeeId, string Name, string? PositionTitle, int Depth);

/// <summary>روابط سازمانی یک کارمند از چارت سازمانی: مافوق‌ها (از نزدیک به دور)، زیردستان (مستقیم و غیرمستقیم) و هم‌ترازها.</summary>
public sealed record OrgRelations(IReadOnlyList<int> ManagerEmployeeIds, IReadOnlyList<SubordinateInfo> Subordinates, IReadOnlyList<int> PeerEmployeeIds)
{
    public static OrgRelations Empty { get; } = new(Array.Empty<int>(), Array.Empty<SubordinateInfo>(), Array.Empty<int>());
    public IReadOnlySet<int> SubordinateIds => Subordinates.Select(s => s.EmployeeId).ToHashSet();
}

/// <summary>
/// جهت معکوس سلسله‌مراتب سمت‌ها (Position.ParentPositionId): زیردستان یک کارمند. مدیر مستقیم همچنان فقط از
/// IOrganizationResolver.ResolveManagerAsync می‌آید؛ این سرویس همان داده را از سمت دیگر می‌خواند و چیزی حدس نمی‌زند:
/// کارمندی که سمت اصلی معتبر ندارد، زیردست/مافوق/هم‌تراز ندارد. فقط خواندنی است.
/// </summary>
public interface IOrganizationSubordinateResolver
{
    Task<IReadOnlyList<SubordinateInfo>> GetSubordinatesAsync(int employeeId, bool includeIndirect = true, DateTime? asOf = null, CancellationToken ct = default);

    Task<OrgRelations> GetRelationsAsync(int employeeId, DateTime? asOf = null, CancellationToken ct = default);
}
