using Microsoft.Net.Http.Headers;

namespace PEMS.Web.Api;

/// <summary>ETag and If-Match handling over a row version (guideline chapter 08, rule 6).</summary>
public static class ApiETag
{
    public static string Format(byte[]? rowVersion) =>
        "\"" + (rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion).Replace('+', '-').Replace('/', '_').TrimEnd('=') : "0") + "\"";

    public static byte[]? Parse(string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag)) return null;
        var v = etag.Trim().Trim('"');
        if (v.Length == 0 || v == "0") return null;
        v = v.Replace('-', '+').Replace('_', '/');
        v = v.PadRight(v.Length + (4 - v.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(v); } catch (FormatException) { return null; }
    }

    public enum Check { Missing, Matches, Stale }

    /// <summary>Compares the request's If-Match with the current row version. "*" always matches.</summary>
    public static Check Evaluate(HttpRequest request, byte[]? current)
    {
        var header = request.Headers[HeaderNames.IfMatch].ToString();
        if (string.IsNullOrWhiteSpace(header)) return Check.Missing;
        if (header.Trim() == "*") return Check.Matches;
        var wanted = Format(current);
        return header.Split(',').Select(h => h.Trim()).Any(h => h.StartsWith("W/", StringComparison.Ordinal) ? h[2..] == wanted : h == wanted)
            ? Check.Matches : Check.Stale;
    }
}
