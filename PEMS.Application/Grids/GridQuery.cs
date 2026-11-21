using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace PEMS.Application.Grids;

// SCRUM-54 (#32): the server side of the data grid. One request shape (page, page size, sort, filters, search) and one safe way to apply it to any IQueryable:
// only the fields a controller declares can be sorted or filtered, values are parsed by the field's type, and the order is always deterministic so a page never repeats a row.

public sealed record GridSort(string Field, bool Descending);

/// <summary>One column filter. <see cref="Op"/> is eq, ne, contains, startsWith, endsWith, gt, gte, lt, lte, in (a comma separated list), range (<c>from..to</c>: from inclusive, to exclusive; either side may be empty) or empty (blank value).</summary>
public sealed record GridFilter(string Field, string Op, string Value);

public sealed record GridRequest(int Page, int PageSize, IReadOnlyList<GridSort> Sort, IReadOnlyList<GridFilter> Filters, string? Search)
{
    public const int MaxPageSize = 500;

    /// <summary>
    /// Reads <c>?page=2&amp;pageSize=50&amp;sort=name,-created&amp;q=pump&amp;f.name=contains:abc&amp;f.age=gte:18&amp;f.status=in:Open,Closed</c>. A filter without an operator is "eq" for numbers and dates and
    /// "contains" for text. Nothing here trusts the client: sizes are clamped and unknown fields are rejected later by <see cref="GridFields{T}"/>.
    /// </summary>
    public static GridRequest FromQuery(IEnumerable<KeyValuePair<string, string?>> query, int defaultPageSize = 50)
    {
        var map = query.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
        int Int(string key, int fallback) => map.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        var size = Math.Clamp(Int("pageSize", defaultPageSize), 1, MaxPageSize);
        var page = Math.Max(Int("page", 1), 1);
        var sort = new List<GridSort>();
        if (map.TryGetValue("sort", out var s) && !string.IsNullOrWhiteSpace(s))
            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
                sort.Add(part.StartsWith('-') ? new GridSort(part[1..], true) : new GridSort(part.TrimStart('+'), false));
        var filters = new List<GridFilter>();
        foreach (var (key, value) in map)
        {
            if (!key.StartsWith("f.", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(value)) continue;
            var field = key[2..];
            var colon = value.IndexOf(':');
            if (colon > 0 && value[..colon] is "eq" or "ne" or "contains" or "startsWith" or "endsWith" or "gt" or "gte" or "lt" or "lte" or "in" or "range")
                filters.Add(new GridFilter(field, value[..colon], value[(colon + 1)..]));
            else filters.Add(new GridFilter(field, "", value));
            if (filters.Count >= 20) break;
        }
        return new GridRequest(page, size, sort, filters, map.TryGetValue("q", out var q) && !string.IsNullOrWhiteSpace(q) ? q.Trim() : null);
    }
}

public sealed record GridPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed class GridQueryException(string message) : Exception(message);

/// <summary>The fields of a row type that a grid may sort, filter and search. Declared once per grid; anything else is refused.</summary>
public sealed class GridFields<T>
{
    private sealed record Field(LambdaExpression Selector, Type Type, bool Searchable);
    private readonly Dictionary<string, Field> _fields = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultSort;

    /// <summary>Declares a field; <paramref name="searchable"/> includes a text field in the quick search (<c>q</c>).</summary>
    public GridFields<T> Add<TValue>(string name, Expression<Func<T, TValue>> selector, bool searchable = false)
    {
        _fields[name] = new Field(selector, typeof(TValue), searchable && typeof(TValue) == typeof(string));
        _defaultSort ??= name;
        return this;
    }

    /// <summary>The field that breaks ties so the order is total (usually the key); default is the first declared field.</summary>
    public GridFields<T> TieBreak(string name) { _defaultSort = name; return this; }

    internal bool TryGet(string name, out LambdaExpression selector, out Type type)
    {
        if (_fields.TryGetValue(name, out var f)) { selector = f.Selector; type = f.Type; return true; }
        selector = null!; type = null!; return false;
    }

    internal IEnumerable<LambdaExpression> SearchFields => _fields.Values.Where(f => f.Searchable).Select(f => f.Selector);
    internal string? DefaultSort => _defaultSort;
}

public static class GridQueryExtensions
{
    /// <summary>The rows that match the filters and the quick search (no order, no paging): count this for the total.</summary>
    public static IQueryable<T> ApplyGridFilters<T>(this IQueryable<T> query, GridRequest request, GridFields<T> fields)
    {
        foreach (var filter in request.Filters)
        {
            if (!fields.TryGet(filter.Field, out var selector, out var type)) throw new GridQueryException($"\"{filter.Field}\" is not a column of this grid.");
            query = query.Where(Predicate<T>(selector, type, filter));
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var param = Expression.Parameter(typeof(T), "r");
            var needle = Expression.Constant(request.Search.ToLowerInvariant());
            Expression? any = null;
            foreach (var sel in fields.SearchFields)
            {
                var body = Bind(sel, param);
                var test = Expression.AndAlso(Expression.NotEqual(body, Expression.Constant(null, typeof(string))), Expression.Call(Expression.Call(body, nameof(string.ToLower), null), nameof(string.Contains), null, needle));
                any = any is null ? test : Expression.OrElse(any, test);
            }
            if (any is not null) query = query.Where(Expression.Lambda<Func<T, bool>>(any, param));
        }
        return query;
    }

    /// <summary>The requested order, then the tie-break field, so paging is stable.</summary>
    public static IQueryable<T> ApplyGridSort<T>(this IQueryable<T> query, GridRequest request, GridFields<T> fields)
    {
        IOrderedQueryable<T>? ordered = null;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sort in request.Sort.Concat(fields.DefaultSort is null ? [] : [new GridSort(fields.DefaultSort, false)]))
        {
            if (!used.Add(sort.Field)) continue;
            if (!fields.TryGet(sort.Field, out var selector, out var type)) throw new GridQueryException($"\"{sort.Field}\" is not a column of this grid.");
            ordered = OrderBy(ordered ?? query, selector, type, sort.Descending, ordered is not null);
        }
        return ordered ?? query;
    }

    public static IQueryable<T> Paged<T>(this IQueryable<T> query, GridRequest request) => query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);

    private static Expression<Func<T, bool>> Predicate<T>(LambdaExpression selector, Type type, GridFilter f)
    {
        var param = Expression.Parameter(typeof(T), "r");
        var member = Bind(selector, param);
        var under = Nullable.GetUnderlyingType(type) ?? type;
        Expression Const(string text) => Expression.Constant(Parse(text, under, f.Field), type);

        if (f.Op == "" && under == typeof(string)) f = f with { Op = "contains" };
        if (f.Op == "") f = f with { Op = "eq" };
        Expression body;
        if (f.Op == "in")
        {
            var parts = f.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(100).ToList();
            if (parts.Count == 0) return Expression.Lambda<Func<T, bool>>(Expression.Constant(false), param);
            body = parts.Select(p => (Expression)Expression.Equal(member, Const(p))).Aggregate(Expression.OrElse);
        }
        else if (under == typeof(string) && f.Op is "contains" or "startsWith" or "endsWith")
        {
            var method = f.Op switch { "contains" => nameof(string.Contains), "startsWith" => nameof(string.StartsWith), _ => nameof(string.EndsWith) };
            var lowered = Expression.Call(member, nameof(string.ToLower), null);
            body = Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                Expression.Call(lowered, method, null, Expression.Constant(f.Value.ToLowerInvariant())));
        }
        else if (f.Op == "range")
        {
            if (under == typeof(string) || under == typeof(bool)) throw new GridQueryException($"\"range\" cannot be used on the column \"{f.Field}\".");
            var cut = f.Value.IndexOf("..", StringComparison.Ordinal);
            if (cut < 0) throw new GridQueryException("A range is written from..to.");
            var from = f.Value[..cut].Trim(); var to = f.Value[(cut + 2)..].Trim();
            Expression? both = null;
            if (from.Length > 0) both = Expression.GreaterThanOrEqual(member, Const(from));
            if (to.Length > 0) { var upper = Expression.LessThan(member, Const(to)); both = both is null ? upper : Expression.AndAlso(both, upper); }
            body = both ?? Expression.Constant(true);
        }
        else if (f.Op is "eq" or "ne")
        {
            body = under == typeof(string)
                ? Expression.Equal(Expression.Call(member, nameof(string.ToLower), null), Expression.Constant(f.Value.ToLowerInvariant()))
                : Expression.Equal(member, Const(f.Value));
            if (f.Op == "ne") body = Expression.Not(body);
        }
        else if (f.Op is "gt" or "gte" or "lt" or "lte")
        {
            if (under == typeof(string) || under == typeof(bool)) throw new GridQueryException($"\"{f.Op}\" cannot be used on the column \"{f.Field}\".");
            var constant = Const(f.Value);
            body = f.Op switch { "gt" => Expression.GreaterThan(member, constant), "gte" => Expression.GreaterThanOrEqual(member, constant), "lt" => Expression.LessThan(member, constant), _ => Expression.LessThanOrEqual(member, constant) };
        }
        else throw new GridQueryException($"\"{f.Op}\" is not a filter operator.");
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>The selector's body with its own parameter replaced by <paramref name="param"/> (inlined, so a provider such as EF translates it as if it were written there).</summary>
    private static Expression Bind(LambdaExpression selector, ParameterExpression param) => new Replace(selector.Parameters[0], param).Visit(selector.Body);

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    private static object? Parse(string text, Type under, string field)
    {
        try
        {
            if (under == typeof(string)) return text;
            if (under == typeof(bool)) return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" ? true : text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0" ? false : throw new FormatException();
            if (under == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
            if (under == typeof(DateOnly)) return DateOnly.ParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (under.IsEnum) return Enum.Parse(under, text, ignoreCase: true);
            return Convert.ChangeType(text, under, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidCastException)
        {
            throw new GridQueryException($"\"{text}\" is not a valid value for the column \"{field}\".");
        }
    }

    private static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> query, LambdaExpression selector, Type type, bool descending, bool then)
    {
        var name = (then, descending) switch { (false, false) => nameof(Queryable.OrderBy), (false, true) => nameof(Queryable.OrderByDescending), (true, false) => nameof(Queryable.ThenBy), _ => nameof(Queryable.ThenByDescending) };
        var call = Expression.Call(typeof(Queryable), name, [typeof(T), type], query.Expression, Expression.Quote(selector));
        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(call);
    }
}

/// <summary>Writes the rows of a grid as CSV for the export button (UTF-8 with a byte order mark so Excel reads Persian text; formula characters are neutralised).</summary>
public static class GridCsv
{
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', headers.Select(Cell)).Append("\r\n");
        foreach (var row in rows) sb.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Cell(object? v)
    {
        var s = v switch { null => "", DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;   // a cell must never run as a formula
        return s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
