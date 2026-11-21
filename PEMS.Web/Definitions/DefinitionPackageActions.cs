using System.Text.Json;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.Web.Api.V1;

namespace PEMS.Web.Definitions;

// SCRUM-784 (gap of SCRUM-449): the export and import of definitions as actions, so the history page and an import form are stored definitions over the same service the REST API uses. Export serves the package of one
// version as a file; import reads an uploaded package file and creates drafts (nothing is published). The rights are the permissions of the action definitions (Definitions.View to export, Definitions.Create to import).

internal static class PackageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static TranslatableText Text(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));
    public static TranslatableText Refused(string en, string fa) => Text(en, fa);
}

/// <summary>
/// <c>definition.export</c>: the package of one version of a definition as a JSON file. The posted <c>id</c> is the version reference of the history grid, <c>appKey:kind:key:number</c> (a reference without a number exports
/// the published version). The file is the package of the REST export (<c>pems.definitions/1</c>, hash-checked), so it imports anywhere.
/// </summary>
public sealed class DefinitionExportAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "definition.export";
    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>The definition reference and the version number of <c>appKey:kind:key[:number]</c>; null for text that is not one.</summary>
    public static (string Reference, int? Version)? Parse(string? id)
    {
        var parts = (id ?? "").Split(':');
        if (parts.Length is not (3 or 4) || parts.Any(string.IsNullOrWhiteSpace)) return null;
        if (parts.Length == 3) return (string.Join(':', parts), null);
        return int.TryParse(parts[3], out var n) && n >= 1 ? (string.Join(':', parts[..3]), n) : null;
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        if (Parse(invocation.Values.GetValueOrDefault("id")) is not { } target)
            return ActionOutcome.Refused("id", PackageJson.Refused("Choose a version to export.", "نسخه‌ای برای خروجی انتخاب کنید."));
        using var scope = ActionScope.Open(scopes);
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var id = await HistoryRows.FindAsync(definitions, target.Reference, ct);
        if (id is null) return ActionOutcome.Refused("id", PackageJson.Refused("The definition does not exist.", "تعریف وجود ندارد."));
        var exported = await definitions.ExportAsync(id.Value, target.Version, ct);
        if (!exported.Success) return ActionOutcome.Refused("id", PackageJson.Refused("That version does not exist.", "این نسخه وجود ندارد."));
        var package = exported.Value!;
        var resource = new DefinitionPackageResource(package.Format, DateTime.SpecifyKind(package.ExportedAtUtc, DateTimeKind.Utc), package.Items.Select(i => new DefinitionPackageItemResource(
            i.Kind, i.Key, i.AppKey, JsonDocument.Parse(i.TitleJson).RootElement.Clone(), i.DescriptionJson is null ? null : JsonDocument.Parse(i.DescriptionJson).RootElement.Clone(),
            JsonDocument.Parse(i.PayloadJson).RootElement.Clone(), i.ContentHash, i.VersionNumber)).ToList());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(resource, PackageJson.Options);
        var first = package.Items.FirstOrDefault();
        var name = first is null ? "definition.json" : $"{first.AppKey}.{first.Kind}.{first.Key}.v{first.VersionNumber}.pems-definition.json";
        return ActionOutcome.Download(new FileResult(bytes, name, "application/json"));
    }
}

/// <summary>
/// <c>definition.import</c>: reads the uploaded package file (the form field <c>package</c>, up to 5 MB) and creates drafts through the same checks as the REST import (kind, payload, content hash); nothing is published.
/// The success message says how many definitions were created and how many got a new draft.
/// </summary>
public sealed class DefinitionImportAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "definition.import";
    public const long MaxBytes = 5 * 1024 * 1024;
    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var file = invocation.Files?.FirstOrDefault(f => f.Field == "package");
        if (file is null) return ActionOutcome.Refused("package", PackageJson.Refused("Choose a package file.", "یک فایل بسته انتخاب کنید."));
        if (file.Length > MaxBytes) return ActionOutcome.Refused("package", PackageJson.Refused("The package is larger than 5 MB.", "حجم بسته بیش از ۵ مگابایت است."));

        DefinitionPackageResource? resource;
        try
        {
            await using var stream = file.Open();
            resource = await JsonSerializer.DeserializeAsync<DefinitionPackageResource>(stream, PackageJson.Options, ct);
        }
        catch (JsonException)
        {
            return ActionOutcome.Refused("package", PackageJson.Refused("The file is not a definition package.", "این فایل بسته تعریف نیست."));
        }
        if (resource is null) return ActionOutcome.Refused("package", PackageJson.Refused("The file is empty.", "فایل خالی است."));

        using var scope = ActionScope.Open(scopes);
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var imported = await definitions.ImportAsync(resource.ToPackage(), invocation.UserName ?? "unknown", ct);
        if (!imported.Success)
        {
            var first = imported.Errors?.FirstOrDefault();
            var reason = first is null ? imported.Code ?? "refused" : $"{first.Pointer} {first.Message}".Trim();
            return ActionOutcome.Refused("package", TranslatableText.Literal(reason));
        }
        return ActionOutcome.Done(PackageJson.Refused($"Imported: {imported.Value!.Created} created, {imported.Value.Updated} with a new draft.", $"وارد شد: {imported.Value.Created} ایجاد، {imported.Value.Updated} پیش‌نویس جدید."));
    }
}
