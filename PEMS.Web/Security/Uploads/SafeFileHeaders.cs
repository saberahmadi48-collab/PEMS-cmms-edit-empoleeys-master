using Microsoft.Net.Http.Headers;

namespace PEMS.Web.Security.Uploads;

/// <summary>
/// Response headers for every uploaded file PEMS serves (SCRUM-202, OWASP File Upload Cheat Sheet):
/// the browser must not guess another type (<c>nosniff</c>), the file runs no script and loads nothing
/// (<c>Content-Security-Policy</c>), and it is shown inline only when <see cref="PEMS.Application.Uploads.UploadPolicy.IsInlineSafe"/>
/// allows it; everything else is a download (<c>Content-Disposition: attachment</c>).
/// </summary>
public static class SafeFileHeaders
{
    /// <summary>For images and text: an opaque-origin sandbox with no scripts, forms, plugins or loads.</summary>
    public const string SandboxPolicy = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'self'";

    /// <summary>
    /// For PDF: Chromium's built-in PDF viewer refuses to open a document whose CSP has <c>sandbox</c> ("blocked"), so PDFs get
    /// the same lock-down without it. The viewer runs PDF scripts in its own sandbox, never in the PEMS origin.
    /// </summary>
    public const string PdfPolicy = "default-src 'none'; object-src 'self'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'self'";

    public static void Apply(HttpResponse response, string contentType, string? downloadName, bool inline, bool cacheable = false)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = contentType == "application/pdf" ? PdfPolicy : SandboxPolicy;
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers.CacheControl = cacheable ? "private, max-age=3600" : "private, no-store";

        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        if (!string.IsNullOrWhiteSpace(downloadName)) disposition.SetHttpFileName(downloadName);
        response.Headers.ContentDisposition = disposition.ToString();
    }
}
