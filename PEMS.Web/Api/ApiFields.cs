using System.Text.Json;

namespace PEMS.Web.Api;

/// <summary>Sparse fieldsets: <c>fields=code,name</c> keeps only those properties of a resource (chapter 08, rule 9).</summary>
public static class ApiFields
{
    public static bool TryParse(IQueryCollection query, IReadOnlySet<string> allowed, out IReadOnlySet<string>? fields, out List<ApiFieldError> errors)
    {
        errors = new List<ApiFieldError>();
        fields = null;
        var raw = query["fields"].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!allowed.Contains(f)) errors.Add(new ApiFieldError("/fields", "unknown-field", "Api_Err_UnknownField", $"'{f}' is not a field of this resource."));
            else set.Add(f);
        }
        fields = set;
        return errors.Count == 0;
    }

    public static object Project(object resource, IReadOnlySet<string>? fields)
    {
        if (fields == null) return resource;
        var element = JsonSerializer.SerializeToElement(resource, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var result = new Dictionary<string, JsonElement>();
        foreach (var p in element.EnumerateObject())
            if (fields.Contains(p.Name)) result[p.Name] = p.Value.Clone();
        return result;
    }
}
