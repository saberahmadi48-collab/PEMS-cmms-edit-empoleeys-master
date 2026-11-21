# راهنمای فاز Framework جدید PEMS.Web

این فاز شامل: Layout سازمانی، Login، ASP.NET Core Identity، RBAC (۸ نقش)، Dashboard با KPI واقعی، منوی کامل با دسترسی بر اساس نقش، و مدیریت Users/Roles.

## تغییرات کلیدی

- **`PEMSDbContext`** حالا از `IdentityDbContext<ApplicationUser, ApplicationRole, int>` ارث می‌برد. تمام Entity/DbSet های قبلی شما (Project, EngineeringDocument, Equipment و ...) دست‌نخورده باقی مانده‌اند؛ فقط جداول Identity (AspNetUsers, AspNetRoles, AspNetUserRoles, ...) اضافه شده‌اند.
- Entity قدیمی `PEMS.Domain.Entities.User` **حذف نشده** (طبق درخواست شما برای حفظ Entity/Migration های موجود) ولی دیگر برای Login استفاده نمی‌شود؛ سیستم ورود واقعی روی `ApplicationUser` (Identity) کار می‌کند.
- `ApplicationUser` / `ApplicationRole` در پروژه‌ی `PEMS.Persistence/Identity/` قرار دارند.
- کل سایت پشت یک `[Authorize]` سراسری است؛ فقط صفحات `Account/*` (Login, ForgotPassword, AccessDenied) بدون ورود در دسترس‌اند.

## ۱) نصب پکیج

```
dotnet restore
```

پکیج جدید: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (به PEMS.Persistence اضافه شد).

## ۲) ساخت Migration جدید (Additive - چیزی حذف نمی‌شود)

```
dotnet ef migrations add AddAspNetIdentity --project PEMS.Persistence --startup-project PEMS.Web
dotnet ef database update --project PEMS.Persistence --startup-project PEMS.Web
```

## ۳) اجرای برنامه و ورود

اولین اجرا، به‌صورت خودکار:
- ۸ نقش سیستم (SystemAdmin, EngineeringManager, EngineeringSupervisor, DisciplineEngineer, DocumentController, MaintenanceEngineer, ContractManager, Viewer) ساخته می‌شوند.
- یک کاربر پیش‌فرض System Admin ساخته می‌شود (فقط اگر هیچ کاربری در سیستم نباشد):

```
Username: admin
Password: <the value of Seed__Admin__Password in env/<name>.secrets.env - there is no built-in default>
```

**⚠️ حتماً بلافاصله بعد از اولین ورود این رمز را از صفحه‌ی Users تغییر دهید.**

## ۴) ساختار منو و دسترسی نقش‌ها

| بخش منو | نقش‌های مجاز |
|---|---|
| Engineering Management | SystemAdmin, EngineeringManager, EngineeringSupervisor, DisciplineEngineer, DocumentController, Viewer |
| AI Engineering Intelligence | SystemAdmin, EngineeringManager, DisciplineEngineer, DocumentController |
| Contract Management | SystemAdmin, EngineeringManager, ContractManager |
| CMMS Integration | SystemAdmin, EngineeringManager, EngineeringSupervisor, MaintenanceEngineer |
| Administration | فقط SystemAdmin |

این مپینگ در `PEMS.Web/Security/MenuAccess.cs` است و به‌راحتی قابل تغییر است.

## ۵) چه چیزی الان کار می‌کند و چه چیزی «به‌زودی» است

فعال و کاربردی:
- Login / Logout / Forgot Password (صفحه‌ی راهنمایی، بدون ارسال ایمیل واقعی)
- Dashboard با آمار واقعی از دیتابیس (پروژه‌ها، مدارک، وضعیت‌ها، Work Order های باز، هشدارهای AI)
- Engineering Documents (همان ماژول قبلی، حالا داخل Layout جدید)
- Administration → Users (ساخت/ویرایش کاربر، تخصیص نقش، فعال/غیرفعال‌سازی)
- Administration → Roles (لیست، ساخت نقش سفارشی، حذف نقش‌های غیرسیستمی)

صفحه‌ی «Coming Soon» (طبق دستور شما - فقط Framework، ماژول‌ها بعداً):
Projects, Document Register, Document Revision, Document Workflow, Transmittal, MR/MRQ Management, تمام زیرمنوهای AI Intelligence، تمام Contract Management، تمام CMMS Integration (به‌جز KPI در Dashboard)، Disciplines/Departments/System Settings.

## ۶) نکات امنیتی برای Production

- رمز پیش‌فرض ادمین را فوراً تغییر دهید.
- `RequireUniqueEmail = true` فعال است.
- Lockout بعد از ۵ تلاش ناموفق، ۱۰ دقیقه فعال می‌شود.
- برای Production واقعی، `Password.RequireUppercase` و `RequireNonAlphanumeric` را در `Program.cs` سخت‌گیرانه‌تر کنید و یک سرویس ارسال ایمیل واقعی برای Forgot Password اضافه کنید.
