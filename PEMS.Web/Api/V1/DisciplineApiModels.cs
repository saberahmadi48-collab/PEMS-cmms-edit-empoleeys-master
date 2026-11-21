using System.Text.Json;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Api.V1;

/// <summary>Body of POST and PUT on <c>/api/v1/disciplines</c>.</summary>
public sealed record DisciplineInput(string? Code, string? Name, string? Description, string? ImportAliases, string? TransmittalCode, bool? IsActive)
{
    public DisciplineWrite ToWrite() => new(Code ?? "", Name ?? "", Description, ImportAliases, TransmittalCode, IsActive ?? true);
}

/// <summary>A discipline as returned by the API; instants are UTC, texts are canonical (display formatting is the page's job).</summary>
public sealed record DisciplineResource(int Id, string Code, string Name, string? Description, string? ImportAliases, string? TransmittalCode,
    bool IsActive, DateTime CreatedDate, IReadOnlyList<string> AllowedActions)
{
    public static readonly IReadOnlySet<string> FieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "id", "code", "name", "description", "importAliases", "transmittalCode", "isActive", "createdDate", "allowedActions" };

    public static DisciplineResource From(DisciplineDetail d, IReadOnlyList<string> allowed) =>
        new(d.DisciplineId, d.Code, d.Name, d.Description, d.ImportAliases, d.TransmittalCode, d.IsActive,
            DateTime.SpecifyKind(d.CreatedDate, d.CreatedDate.Kind == DateTimeKind.Utc ? DateTimeKind.Utc : DateTimeKind.Local).ToUniversalTime(), allowed);
}

/// <summary>JSON Merge Patch (RFC 7396) over the writable fields: absent = keep, null = clear (nullable fields only).</summary>
public static class DisciplineMergePatch
{
    public static bool TryApply(JsonElement patch, DisciplineDetail current, out DisciplineWrite? result, out List<ApiFieldError> errors)
    {
        errors = new List<ApiFieldError>();
        result = null;
        if (patch.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ApiFieldError("", "invalid-body", "Api_Err_InvalidBody", "The body must be a JSON object."));
            return false;
        }

        string code = current.Code, name = current.Name;
        string? description = current.Description, aliases = current.ImportAliases, transmittal = current.TransmittalCode;
        var active = current.IsActive;

        foreach (var p in patch.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "code": if (Text(p, false, errors) is { } c) code = c; break;
                case "name": if (Text(p, false, errors) is { } n) name = n; break;
                case "description": description = Text(p, true, errors); break;
                case "importaliases": aliases = Text(p, true, errors); break;
                case "transmittalcode": transmittal = Text(p, true, errors); break;
                case "isactive":
                    if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) active = p.Value.GetBoolean();
                    else errors.Add(new ApiFieldError("/isActive", p.Value.ValueKind == JsonValueKind.Null ? "null-not-allowed" : "wrong-type"));
                    break;
                default:
                    errors.Add(new ApiFieldError("/" + p.Name, "unknown-property", "Api_Err_UnknownProperty", $"'{p.Name}' is not a property of a discipline."));
                    break;
            }
        }
        if (errors.Count > 0) return false;
        result = new DisciplineWrite(code, name, description, aliases, transmittal, active);
        return true;
    }

    private static string? Text(JsonProperty p, bool nullable, List<ApiFieldError> errors)
    {
        var pointer = "/" + p.Name;
        switch (p.Value.ValueKind)
        {
            case JsonValueKind.String: return p.Value.GetString();
            case JsonValueKind.Null:
                if (!nullable) errors.Add(new ApiFieldError(pointer, "null-not-allowed"));
                return null;
            default:
                errors.Add(new ApiFieldError(pointer, "wrong-type"));
                return null;
        }
    }
}
