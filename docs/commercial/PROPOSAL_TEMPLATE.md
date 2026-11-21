# Proposal template (one page, Persian and English)

Plan row #130 (SCRUM-28). **Status: template for owner approval.** No customer data. Every `{{NAME}}` is filled per proposal. Prices come from [PRICE_LIST_V1.md](PRICE_LIST_V1.md) once approved. Prepared 2026-11-03.

Approved by: _not yet_

---

## English

**Proposal {{PROPOSAL_NUMBER}}** · Date {{DATE}} · Valid until {{VALID_UNTIL}}

**To:** {{CUSTOMER_NAME}}, {{CUSTOMER_CONTACT}} · **From:** {{COMPANY_NAME}}, {{SALES_CONTACT}}

**1. Scope**
- PEMS core edition: {{EDITION}} (installed version {{CORE_VERSION}}).
- Modules: {{MODULE_LIST}} (each module has its own version).
- Sites covered: {{SITE_COUNT}}. There is no limit on the number of users.
- Installation environment: {{ENVIRONMENT}} (Windows Server, virtual machine or Docker; offline operation, no internet needed).

**2. Price**

| Item | Price ({{CURRENCY}}) |
|---|---|
| Core edition licence (perpetual) | {{PRICE_CORE}} |
| Module licences (perpetual) | {{PRICE_MODULES}} |
| Discount | {{DISCOUNT}} |
| **Licence total** | **{{PRICE_TOTAL}}** |
| Annual maintenance from {{MAINTENANCE_START}} ({{MAINTENANCE_PERCENT}} of the licence) | {{MAINTENANCE_AMOUNT}} per year |
| Services (installation, training, migration) | {{SERVICES}} |

**3. Support and maintenance terms**
- Maintenance entitles the customer to install new versions released up to the licence update date ({{UPDATES_UNTIL}}); without maintenance the installed version keeps working.
- Support channel and hours: {{SUPPORT_TERMS}}. Response targets: {{RESPONSE_TARGETS}}.
- Supported platforms and limits are those of the PEMS Supportability Matrix valid on the proposal date.

**4. Payment and validity**
- Payment terms: {{PAYMENT_TERMS}}. Taxes: {{TAX_RULE}}.
- This proposal is valid until {{VALID_UNTIL}}.

**5. Acceptance**
Customer signature and date: ______________________   {{COMPANY_NAME}} signature and date: ______________________

---

## فارسی

**پیشنهاد شماره {{PROPOSAL_NUMBER}}** · تاریخ {{DATE_FA}} · اعتبار تا {{VALID_UNTIL_FA}}

**به:** {{CUSTOMER_NAME}}، {{CUSTOMER_CONTACT}} · **از:** {{COMPANY_NAME_FA}}، {{SALES_CONTACT}}

**۱. دامنه‌ی کار**
- نسخه‌ی هسته‌ی PEMS: {{EDITION_FA}} (نسخه‌ی نصب‌شده {{CORE_VERSION}}).
- ماژول‌ها: {{MODULE_LIST_FA}} (هر ماژول نسخه‌ی مستقل خود را دارد).
- تعداد سایت‌های تحت پوشش: {{SITE_COUNT}}. محدودیتی برای تعداد کاربران وجود ندارد.
- محیط نصب: {{ENVIRONMENT_FA}} (ویندوز سرور، ماشین مجازی یا داکر؛ بدون نیاز به اینترنت).

**۲. قیمت**

| مورد | مبلغ ({{CURRENCY_FA}}) |
|---|---|
| مجوز هسته (دائمی) | {{PRICE_CORE}} |
| مجوز ماژول‌ها (دائمی) | {{PRICE_MODULES}} |
| تخفیف | {{DISCOUNT}} |
| **جمع مجوزها** | **{{PRICE_TOTAL}}** |
| نگهداری سالانه از {{MAINTENANCE_START_FA}} ({{MAINTENANCE_PERCENT}} مبلغ مجوز) | {{MAINTENANCE_AMOUNT}} در سال |
| خدمات (نصب، آموزش، انتقال داده) | {{SERVICES}} |

**۳. شرایط پشتیبانی و نگهداری**
- نگهداری به مشتری حق نصب نسخه‌های منتشرشده تا تاریخ به‌روزرسانی مجوز ({{UPDATES_UNTIL_FA}}) را می‌دهد؛ بدون نگهداری، نسخه‌ی نصب‌شده به کار خود ادامه می‌دهد.
- کانال و ساعات پشتیبانی: {{SUPPORT_TERMS_FA}}. اهداف زمان پاسخ: {{RESPONSE_TARGETS_FA}}.
- پلتفرم‌ها و محدودیت‌های پشتیبانی‌شده همان جدول پشتیبانی‌پذیری PEMS در تاریخ این پیشنهاد است.

**۴. پرداخت و اعتبار**
- شرایط پرداخت: {{PAYMENT_TERMS_FA}}. مالیات: {{TAX_RULE_FA}}.
- این پیشنهاد تا {{VALID_UNTIL_FA}} معتبر است.

**۵. تأیید**
امضا و تاریخ مشتری: ______________________   امضا و تاریخ {{COMPANY_NAME_FA}}: ______________________

---

## Rules for using the template

1. One page when printed; no customer data is stored in this file.
2. Module names, editions and prices are copied from the approved catalogue and price list, never typed free-hand.
3. The licence payload (edition, modules, `updates_until`) is generated from the same values the proposal states.
