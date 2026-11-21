using System.Text;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Localization;

/// <summary>Applies <see cref="HtmlLocalization"/> to HTML responses after Razor has rendered them.</summary>
public sealed class LegacyHtmlLocalizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IStringLocalizer<SharedResource> localizer)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
                && context.Response.StatusCode is >= 200 and < 300)
            {
                buffer.Position = 0;
                using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var html = await reader.ReadToEndAsync();
                var localized = HtmlLocalization.LocalizeLegacyHtml(html, localizer);
                var bytes = Encoding.UTF8.GetBytes(localized);
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes);
            }
            else
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
