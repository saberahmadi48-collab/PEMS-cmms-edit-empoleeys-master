using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Uploads;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-729: what MyProfileController showed and saved, as operations: the same protected image store, the same signature checks (a PNG data URL whose bytes are a PNG), the employee found through the user name.</summary>
public sealed class MyProfileOperations(PEMSDbContext context, IProtectedImageStore images) : IMyProfileOperations
{
    private const string NoEmployee = "کارمند مرتبط با این حساب کاربری یافت نشد - برای ثبت عکس/امضا باید حساب شما به یک رکورد Employee وصل باشد.";
    private const string PngPrefix = "data:image/png;base64,";

    private async Task<Employee?> EmployeeAsync(string userName)
    {
        if (string.IsNullOrEmpty(userName)) return null;
        var employeeId = await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        return employeeId.HasValue ? await context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value) : null;
    }

    public async Task<List<MyProfileRow>> ProfileAsync(string userName)
    {
        var e = await EmployeeAsync(userName);
        return e is null ? [new MyProfileRow("", "", "", "", "", NoEmployee)]
            : [new MyProfileRow(e.FirstName + " " + e.LastName, e.Position ?? "", e.PersonnelCode ?? "", e.PhotoPath ?? "", e.SignaturePath ?? "", "")];
    }

    /// <summary>The extension of an image from its first bytes (the upload guard has already checked the allow-list); an unknown content is refused.</summary>
    private static string? ExtensionOf(byte[] bytes)
        => bytes.Length > 12 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png"
         : bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? ".jpg"
         : bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[8] == 0x57 ? ".webp"
         : bytes.Length > 3 && bytes[0] == 0x47 && bytes[1] == 0x49 ? ".gif" : null;

    public async Task<MyProfileResult> SavePhotoAsync(string userName, byte[] file)
    {
        var employee = await EmployeeAsync(userName);
        if (employee is null) return new(false, NoEmployee);
        if (file is null || file.Length == 0) return new(false, "فایلی انتخاب نشده.");
        var extension = ExtensionOf(file);
        if (extension is null) return new(false, "فرمت تصویر پشتیبانی نمی‌شود.");
        using var stream = new MemoryStream(file);
        employee.PhotoPath = await images.SaveAsync(ProtectedImageStore.ProfilePhotos, "photo" + extension, stream);
        await context.SaveChangesAsync();
        return new(true, "عکس پروفایل ذخیره شد.");
    }

    public async Task<MyProfileResult> SaveSignatureAsync(string userName, string signatureDataUrl)
    {
        var employee = await EmployeeAsync(userName);
        if (employee is null) return new(false, NoEmployee);
        if (string.IsNullOrWhiteSpace(signatureDataUrl) || !signatureDataUrl.StartsWith(PngPrefix, StringComparison.Ordinal)) return new(false, "امضا خالی است یا فرمتش نامعتبره - دوباره امضا کن.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(signatureDataUrl[PngPrefix.Length..]); }
        catch (FormatException) { return new(false, "امضا قابل‌پردازش نبود - دوباره تلاش کن."); }
        // SCRUM-202: the drawn signature is not a form file, so it is checked here with the same allowlist (PNG bytes)
        if (!UploadPolicy.SignatureMatches(".png", bytes.AsSpan(0, Math.Min(bytes.Length, UploadPolicy.HeadLength)))) return new(false, "امضا قابل‌پردازش نبود - دوباره تلاش کن.");
        using var stream = new MemoryStream(bytes);
        employee.SignaturePath = await images.SaveAsync(ProtectedImageStore.Signatures, "signature.png", stream);
        await context.SaveChangesAsync();
        return new(true, "امضای دیجیتال ذخیره شد.");
    }
}
