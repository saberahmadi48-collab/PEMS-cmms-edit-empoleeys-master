using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Seed;

/// <summary>
/// قالب‌های آماده‌ی نامه‌ی داخلی (درخواست، اطلاع‌رسانی، ابلاغ سیاست، تایید، پیشنهاد، ابلاغیه‌ی رسمی، گزارش وضعیت).
/// فقط افزودنی و Idempotent: قالبی که با همین نام و نوع وجود دارد (فعال یا غیرفعال) دست‌نخورده می‌ماند، پس اگر مدیر قالبی را ویرایش
/// یا غیرفعال کند، اجرای بعدی برنامه آن را برنمی‌گرداند. نشانه‌ها ({{Date}}، {{SenderName}} ...) هنگام باز کردن فرم جایگزین می‌شوند.
/// </summary>
public static class LetterTemplateSeeder
{
    public sealed record Definition(string Name, string Subject, string Body, string Priority = "Normal", string Confidentiality = "Internal");

    public static readonly IReadOnlyList<Definition> Defaults = new List<Definition>
    {
        new("Request / درخواست",
            "درخواست: ",
            "<p>با سلام و احترام،</p><p>بدین‌وسیله از شما درخواست می‌شود در خصوص ................ اقدام لازم را تا تاریخ ................ انجام دهید.</p><p><strong>توضیحات:</strong></p><ul><li>................</li></ul><p>پیشاپیش از همکاری شما سپاسگزارم.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>"),
        new("Information Bulletin / اطلاع‌رسانی",
            "اطلاع‌رسانی: ",
            "<p>با سلام و احترام،</p><p>به اطلاع می‌رساند که ................</p><p><strong>نکات کلیدی:</strong></p><ul><li>................</li><li>................</li></ul><p>این نامه صرفاً جهت اطلاع است و نیاز به پاسخ ندارد.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>"),
        new("Policy Notice / ابلاغ سیاست",
            "ابلاغ سیاست: ",
            "<p>با سلام و احترام،</p><p>سیاست/دستورالعمل «................» از تاریخ ................ لازم‌الاجرا است.</p><p><strong>دامنه‌ی اجرا:</strong> ................</p><p><strong>الزامات:</strong></p><ol><li>................</li><li>................</li></ol><p>خواهشمند است همکاران در واحد خود اطلاع‌رسانی و اجرای آن را پیگیری کنند.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>", "High"),
        new("Approval / درخواست تایید",
            "درخواست تایید: ",
            "<p>با سلام و احترام،</p><p>خواهشمند است نسبت به بررسی و تایید ................ اقدام فرمایید.</p><p><strong>خلاصه:</strong> ................</p><p><strong>پیوست‌ها:</strong> ................</p><p>منتظر اعلام نظر شما تا تاریخ ................ هستم.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>", "High"),
        new("Recommendation / پیشنهاد",
            "پیشنهاد: ",
            "<p>با سلام و احترام،</p><p><strong>وضع موجود:</strong> ................</p><p><strong>پیشنهاد:</strong> ................</p><p><strong>مزایا:</strong></p><ul><li>................</li></ul><p><strong>هزینه/زمان تقریبی:</strong> ................</p><p>در صورت موافقت، اقدامات بعدی هماهنگ خواهد شد.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>"),
        new("Formal Notice / ابلاغیه‌ی رسمی",
            "ابلاغیه: ",
            "<p>به: همکاران محترم</p><p>ابلاغیه‌ی شماره‌ی ................ به شرح زیر ابلاغ می‌شود:</p><ol><li>................</li><li>................</li></ol><p>این ابلاغیه از تاریخ ................ لازم‌الاجرا است و دریافت آن باید تایید شود.</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>", "High", "Confidential"),
        new("Status Report / گزارش وضعیت",
            "گزارش وضعیت: ",
            "<p>با سلام و احترام،</p><p><strong>دوره‌ی گزارش:</strong> ................</p><p><strong>اقدامات انجام‌شده:</strong></p><ul><li>................</li></ul><p><strong>اقدامات در دست انجام:</strong></p><ul><li>................</li></ul><p><strong>مسائل و ریسک‌ها:</strong> ................</p><p><strong>برنامه‌ی دوره‌ی بعد:</strong> ................</p><p>{{SenderName}}<br>{{SenderTitle}} – {{SenderDepartment}}<br>{{Date}}</p>")
    };

    public static async Task<int> SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var existing = await db.LetterTemplates.AsNoTracking().Where(t => t.LetterType == "Internal").Select(t => t.Name).ToListAsync();
        var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var d in Defaults)
        {
            if (have.Contains(d.Name)) continue;
            db.LetterTemplates.Add(new LetterTemplate
            {
                Name = d.Name, LetterType = "Internal", Subject = d.Subject, Body = d.Body,
                Priority = d.Priority, ConfidentialityLevel = d.Confidentiality, IsActive = true, CreatedDate = DateTime.Now
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync();
        return added;
    }
}
