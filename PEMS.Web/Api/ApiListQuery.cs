namespace PEMS.Web.Api;

/// <summary>The one list query contract: <c>sort</c>, <c>filter[field]</c>, <c>q</c>, offset paging (chapter 08, rules 9 and 10).</summary>
public sealed class ApiListQuery
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;
    public const int MaxOffsetRows = 10_000;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Q { get; init; }
    public IReadOnlyList<(string Field, bool Descending)> Sort { get; init; } = Array.Empty<(string, bool)>();
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    public static bool TryParse(IQueryCollection query, out ApiListQuery result, out List<ApiFieldError> errors)
    {
        errors = new List<ApiFieldError>();
        var page = ReadInt(query, "page", 1, 1, int.MaxValue, errors);
        var size = ReadInt(query, "pageSize", DefaultPageSize, 1, MaxPageSize, errors);
        if (errors.Count == 0 && (long)page * size > MaxOffsetRows)
            errors.Add(new ApiFieldError("/page", "offset-limit", "Api_Err_OffsetLimit", $"Offset paging stops at {MaxOffsetRows} rows; use a filter or a cursor export."));

        var sort = new List<(string, bool)>();
        foreach (var part in query["sort"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sort.Add(part.StartsWith('-') ? (part[1..], true) : (part.TrimStart('+'), false));

        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            if (kv.Key.Length > 8 && kv.Key.StartsWith("filter[", StringComparison.Ordinal) && kv.Key.EndsWith(']'))
                filters[kv.Key[7..^1]] = kv.Value.ToString();
        }

        result = new ApiListQuery { Page = page, PageSize = size, Q = string.IsNullOrWhiteSpace(query["q"]) ? null : query["q"].ToString().Trim(), Sort = sort, Filters = filters };
        return errors.Count == 0;
    }

    private static int ReadInt(IQueryCollection q, string name, int fallback, int min, int max, List<ApiFieldError> errors)
    {
        if (!q.TryGetValue(name, out var raw) || raw.Count == 0 || raw[0]!.Length == 0) return fallback;
        if (!int.TryParse(raw[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < min || n > max)
        {
            errors.Add(new ApiFieldError("/" + name, "out-of-range", "Api_Err_OutOfRange", $"{name} must be between {min} and {max}."));
            return fallback;
        }
        return n;
    }
}

/// <summary>A page of a list response (offset paging has a total).</summary>
public sealed record ApiPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
