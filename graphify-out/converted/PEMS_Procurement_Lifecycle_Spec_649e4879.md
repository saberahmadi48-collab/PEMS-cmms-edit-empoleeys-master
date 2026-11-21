<!-- converted from PEMS_Procurement_Lifecycle_Spec.docx -->

سند اجرایی فرایند تقاضا، کاتالوگ قطعات، موجودی، تأمین و خرید در PEMS
نسخه ۱.۰ — پیش‌نویس مرجع برای Claude Code
# ۱. هدف این سند
این سند تمام تصمیم‌ها، تحلیل‌ها و برنامه اجرایی گفت‌وگوی قبلی را در یک مرجع واحد جمع می‌کند تا Claude Code بتواند آن را از ریشه ریپو بخواند و پیش از هر تغییر کدی، وضعیت واقعی ریپو را بررسی کند. این سند توصیف وضعیت فعلی ریپو نیست؛ هر ادعای «موجود بودن» باید در فاز صفر با خواندن سورس کد تأیید شود.
مسیر ریپو: /opt/pems/source/PEMS-cmms-edit-empoleeys-master
# ۲. منابع و نمودارهای مرجع
سه نمودار مرجع در این گفت‌وگو تهیه شده‌اند که نقش متفاوتی دارند و نباید با هم خلط شوند:
- نمودار اول (جریان ساده): نیاز به قطعه، بررسی موجودی قابل‌استفاده، رزروها و سفارش‌های باز؛ اگر پارت تأییدشده در کاتالوگ وجود داشته باشد، تأمین از انبار یا خرید؛ در غیر این صورت Part Catalogue Request و بررسی تکراری‌بودن و تأیید فنی. این نمودار منطق تصمیم‌گیری را نشان می‌دهد.
- نمودار دوم (چرخه ۱۹مرحله‌ای PEMS): از ثبت درخواست و تأیید مدیر سمت، بررسی فنی MR، تأیید نیاز و مشخصات، تشکیل Procurement Package، استعلام و دریافت پیشنهادها، ارزیابی فنی و تجاری، انتخاب و تأیید فروشنده، تهیه PO در وضعیت Draft، تأیید سفارش خرید، صدور رسمی PO، دریافت کالا، بازرسی و کنترل کیفیت، ثبت ورود انبار، تطبیق فاکتور، تأیید پرداخت و بستن پرونده. تأییدکننده هر مرحله در متن نمودار مشخص شده است.
- نمودار سوم (معماری هدف): منابع نیاز (MR، دستورکار تعمیرات، تجهیز، انبار) به Procurement Package وصل می‌شوند و همه تأییدها از PEMS.WorkflowPlatform و IWorkflowRuntime عبور می‌کنند؛ کنترل دسترسی، تفکیک وظایف و ممیزی به‌صورت لایه مشترک عمل می‌کنند. این نمودار معماری هدف است، نه گزارش پیاده‌سازی‌شده بودن.
## ۲.۱ نمودارهای مرجع (تصویری)
نمودارهای زیر متن کامل مراحل را تصویری می‌کنند. هر کدام کنار توضیح مربوط خود آمده است.

نمودار ۱: منطق تصمیم‌گیری از نیاز تا تأمین یا Part Catalogue Request




نمودار ۲: چرخه کامل ۱۹ مرحله‌ای PEMS همراه با تأییدکنندگان هر مرحله (در چهار بخش)


نمودار ۳: معماری هدف با گردش کار و کنترل دسترسی مشترک (معماری هدف است، نه گزارش پیاده‌سازی)

نمودار ۴: زنجیره فرایند خرید (نمای کلی)

نمودار ۵: فلوچارت فرایند خرید با نقش هر مرحله و مسیرهای تأیید و استثنا
# ۳. قواعد غیرقابل مذاکره
- معماری موجود PEMS حفظ شود و مفاهیم موجود دوباره ساخته نشوند.
- موتور گردش‌کار دوم ساخته نشود. همه تأییدها از PEMS.WorkflowPlatform و از طریق APIهای عمومی آن انجام شوند.
- هیچ نام موجودیت، API، مجوز، انتساب کارمند یا سمت سازمانی‌ای فرض یا ساخته نشود.
- هیچ شناسه کارمند واقعی در کد قرار نگیرد.
- اگر تأییدکننده معتبر پیدا نشد، به مدیر سیستم یا هر کاربر جایگزین واگذار نشود؛ درخواست با دلیل مشخص در وضعیت «نیازمند تعیین مسئول» بماند.
- از انتخاب ترتیبی و نامرتب مانند FirstOrDefault() برای تعیین تأییدکننده استفاده نشود.
- هیچ مسیر، مجوز، واردات، خروجی یا گردش کار موجودی حذف یا دور زده نشود.
- تغییرات پایگاه داده فقط افزایشی و امن برای مهاجرت باشند. عملیات مخرب روی دیتابیس تولید ممنوع است.
- سفارش‌های خرید جدید از وضعیت Draft شروع شوند؛ سفارش‌های واردشده که تاریخچه‌شان Issued است باید رفتار واردات خود را حفظ کنند، مگر با مهاجرت داده‌ای جداگانه و تأییدشده.
- یک PO تأییدشده به‌معنای رسید کالا، ثبت موجودی، تأیید فاکتور یا پرداخت نیست.
- تست‌هایی که رکورد می‌سازند فقط روی دیتابیس ایزوله اجرا شوند. هیچ رکوردی صرفاً به‌دلیل شبیه تست بودن پاک نشود.
- هیچ تست رد‌شده، رد‌شده‌ای که نادیده گرفته شده یا محدودیت محیطی پنهان نشود.
- موفقیت build به‌معنای درستی فرایند کسب‌وکار نیست.
- commit یا push تنها با مجوز صریح انجام شود.
# ۴. تفکیک مفاهیم
مفاهیم زیر باید در مدل داده و گردش کار جدا نگه داشته شوند:
| مفهوم | معنا | نمونه یا یادداشت |
| --- | --- | --- |
| Material / Part Catalogue | مرجع قطعات شناخته‌شده و تأییدشده | فقط رکوردهای تأییدشده در کاتالوگ اصلی |
| Part Catalogue Request | درخواست ایجاد یا اصلاح پارت؛ ثبت آن معادل ایجاد قطعی پارت نیست | وضعیت‌های درخواست جدا از رکورد اصلی |
| Material Demand / Requisition | نیاز به قطعه، با هر منشأ | کمبود انبار، دستورکار تعمیرات، PM، پروژه، مهندسی |
| Procurement Process | تأمین از طریق خرید پس از بررسی موجودی و تأییدهای لازم | MR، Procurement Package، RFQ، PO |
| Employee / Position / Discipline | کارمند، سمت سازمانی، تخصص | تعیین تأییدکننده از انتساب‌های فعال |
| Identity Role / Permission | نقش و مجوز سیستمی | نباید با مسئولیت گردش کار یکی فرض شود |
| Workflow Responsibility | مسئولیت یک مرحله در گردش کار | مثلاً PO Approver یا Technical Evaluator؛ این برچسب‌ها خودبه‌خود نقش Identity نیستند |

# ۵. منشأهای نیاز به قطعه
نیاز به قطعه ممکن است از هر یک از منشأهای زیر شروع شود. هیچ‌کدام به‌تنهایی الزاماً به ایجاد پارت جدید یا خرید منجر نمی‌شود:
- کمبود موجودی یا رسیدن به نقطه سفارش (Replenishment).
- درخواست مواد داخلی (Store Requisition / MIV).
- دستورکار تعمیرات، PM، خرابی تجهیز یا برنامه نگهداری (Work Order).
- درخواست مهندسی یا نیاز پروژه.
- قطعه جدید یا پارت‌نامبر نامشخص (Part Catalogue Request).
هر منشأ باید اصالت و قابلیت ردیابی خود را حفظ کند (Traceability) تا بتوان از پرونده خرید تا منبع نیاز را دنبال کرد.
# ۶. فرایند هدف از نیاز تا بستن پرونده
## ۶.۱ تعیین هویت قطعه
- اگر پارت تأییدشده در کاتالوگ وجود دارد: از همان پارت استفاده شود.
- اگر پارت دریافت‌شده در کاتالوگ نیست: Part Catalogue Request ثبت و بررسی شود.
- اگر پارت‌نامبر هنوز مشخص نیست: درخواست موقت با سازنده، مدل، مشخصات فنی و مستندات ثبت شود و تا تکمیل اطلاعات وارد تأیید نهایی نشود.
- اگر پارت مشابه یا معادل وجود دارد: ابتدا تطبیق انجام شود تا رکورد تکراری ساخته نشود.
## ۶.۲ بررسی موجودی و نیاز خالص
قبل از پیشنهاد تأمین یا خرید، این موارد بررسی شوند: موجودی قابل‌استفاده (نه صرفاً موجودی فیزیکی)، موجودی بلوکه یا قرنطینه‌شده در صورت مدل‌سازی، رزروها و تخصیص‌ها، سفارش‌های خرید باز و مقادیر باقی‌مانده، رسیدهای تأییدشده در راه، و در صورت تعریف: نقطه سفارش، حداقل و حداکثر موجودی، زمان تأمین و موجودی اطمینان.
فرمول نیاز خالص باید بر اساس مدل واقعی انبار در فاز صفر تعیین و تست شود. فرمول پیشنهادی برای بررسی: نیاز کل منهای موجودی قابل‌استفاده، منهای رزروهای معتبر، منهای سفارش‌های باز و رسیدهای در راه که قبلاً شمرده نشده‌اند. اگر داده‌ای در مدل وجود ندارد، آن را گزارش کنید و مقدار ساختگی وارد نکنید.
مقدار ورودی در حال بررسی نباید دو بار شمرده شود. کالای رزروشده یا غیرقابل‌استفاده نباید آزاد فرض شود.
## ۶.۳ درخواست کاتالوگ
Part Catalogue Request مسیر رسمی ایجاد یا تأیید پارت جدید است. ثبت درخواست هم‌ارز ایجاد پارت قطعی در کاتالوگ اصلی نیست. این مرحله باید موارد زیر را انجام دهد:
- جستجوی پارت موجود بر اساس شماره پارت سازنده، شماره داخلی، نام مستعار و شناسه‌های فنی.
- شناسایی تکراری‌های احتمالی و ارجاع موارد مبهم به بررسی.
- پذیرش درخواست با شماره پارت نامشخص فقط در صورت وجود اطلاعات فنی لازم و توجیه کسب‌وکار.
- نگهداری درخواست جدا از رکورد تأییدشده کاتالوگ تا زمان تأیید.
- تعیین تأییدکنندگان فنی و کاتالوگ از انتساب‌های فعال، سمت‌ها، مجوزها و قواعد تأییدشده سازمانی.
- اعمال تفکیک وظایف: درخواست‌دهنده نمی‌تواند تأییدکننده همان درخواست باشد.
- ثبت پیوند با منشأ نیاز و نگهداری تاریخچه ممیزی.
- ارائه تشخیص قابل‌اقدام در صورت نبود تأییدکننده مجاز.
## ۶.۴ تأمین از انبار یا خرید
اگر موجودی قابل‌استفاده کافی باشد، تأمین از انبار با استفاده از مکانیزم موجود Store Requisition و تراکنش‌های انبار انجام می‌شود. اگر کافی نباشد، Procurement Package یا درخواست خرید برای نیاز خالص ایجاد می‌شود. تراکنش‌های موجود انبار باید دوباره استفاده شوند و نباید عملیات تکراری مانند Issue یا Receipt جدید ساخته شود.
## ۶.۵ خرید و استعلام
این مرحله شامل MR، Procurement Package، RFQ یا Vendor Inquiry، پیشنهادهای فروشندگان، پاسخ‌های فنی، ارزیابی فنی، ارزیابی تجاری، پیشنهاد انتخاب فروشنده، تأیید انتخاب و تهیه PO است. استعلام باید حداقل از سه تأمین‌کننده انجام شود؛ اگر کمتر باشد علت ثبت شود. پیشنهادها تا زمان بازگشایی قفل باشند و سؤال و پاسخ به همه تأمین‌کنندگان ارسال شود.
ارسال RFQ در محیط بدون اینترنت از طریق پورتال داخلی یا روش‌های حضوری، تلفنی و کتبی ثبت‌شده انجام می‌شود؛ هر ارسال باید زمان، گیرنده و رسید داشته باشد.
## ۶.۶ دریافت، بازرسی، ثبت انبار، تطبیق، پرداخت و بستن پرونده
- دریافت کالا: رسید جزئی یا کامل، با کنترل تعداد نسبت به خطوط PO و رسیدهای قبلی.
- بازرسی کیفی در صورت نیاز: پذیرش، رد یا قرنطینه. کالای رد یا قرنطینه‌شده نباید به‌عنوان موجودی آزاد قابل‌استفاده ثبت شود.
- ثبت ورود به انبار: فقط پس از عبور کنترل‌های لازم، از طریق تراکنش‌های موجود.
- تطبیق سه‌طرفه: سفارش، رسید پذیرفته‌شده و فاکتور، در صورت وجود قابلیت مالی/AP در ریپو.
- تأیید پرداخت: فقط در صورت پیاده‌سازی جریان مالی. اگر وجود ندارد، گزارش شود و وضعیت پرداخت صرفاً با تغییر فیلد شبیه‌سازی نشود.
- بستن پرونده خرید و تهیه گزارش‌ها با وضعیت‌های نهایی قانونی.
# ۷. ماتریس تأیید و بررسی
جدول زیر پیشنهاد فرایندی است و نام سمت‌ها را به‌عنوان مسئولیت گردش کار مطرح می‌کند؛ این‌ها نباید بدون تطبیق با ساختار واقعی سازمان به‌عنوان نقش سیستمی یا سمت موجود فرض شوند. ستون آخر وضعیت تصمیم را نشان می‌دهد.
| مرحله | مسئول تأیید یا بررسی پیشنهادی | مبنای تعیین | وضعیت تصمیم |
| --- | --- | --- | --- |
| تأیید نیاز | مدیر مستقیم درخواست‌دهنده | زنجیره واقعی سمت‌های سازمانی | تأیید نشده — نیاز به تطبیق با چارت |
| بررسی فنی MR و مشخصات | کارشناس یا مرجع فنی دیسیپلین مربوط | نوع قطعه و تخصص | تأیید نشده |
| تأیید نیاز برای قطعات تعمیراتی | مرجع تعمیرات | اهمیت و حساسیت قطعه، ضوابط مصوب | تأیید نشده — ضوابط مشخص نیست |
| کنترل کاتالوگ و تکراری نبودن | مسئول کاتالوگ قطعات | کیفیت اطلاعات و ثبت نهایی | تأیید نشده |
| بررسی بودجه | کارشناس بودجه / مالی | انتساب فعال | تأیید نشده |
| تأیید بودجه ناکافی | مدیر مالی | سیاست مصوب | تأیید نشده — سقف‌ها نامشخص |
| ارزیابی فنی پیشنهادها | Technical Evaluator (متخصص دیسیپلین) | تخصص و انتساب | تأیید نشده |
| ارزیابی تجاری | Commercial Evaluator / مسئول بازرگانی | انتساب و سیاست | تأیید نشده |
| تأیید انتخاب فروشنده | Award Approver | حدود اختیار مصوب | تأیید نشده — حدود مبلغ نامشخص |
| تأیید سفارش خرید (PO) | PO Approver، مدیر خرید؛ مبلغ بالا: مدیر مالی | مبلغ و اختیار | تأیید نشده — حدود مبلغ نامشخص |
| بازرسی کیفی | بازرس QC | نوع کالا و الزام | تأیید نشده |
| ثبت ورود انبار | انباردار | انتساب انبار | تأیید نشده |
| تأیید پرداخت | مدیر مالی یا مقام مجاز مالی | حدود اختیار | تأیید نشده |

قواعد زیر باید از سیاست‌های مصوب استخراج شوند و نباید حدس زده شوند: حدود ریالی هر سطح تأیید، سقف مبلغ استعلام، تعداد تأمین‌کنندگان الزامی در موارد اضطراری، و اینکه آیا تأیید کاتالوگ پیش از خرید در همه سناریوها الزامی است یا نه.
# ۸. قواعد کاتالوگ و جلوگیری از تکرار
- قبل از ثبت هر درخواست کاتالوگ، جستجوی پارت موجود اجباری است.
- تطبیق بر اساس شماره پارت سازنده، شماره داخلی، نام مستعار و شناسه‌های فنی انجام شود.
- تطبیق مبهم به بررسی انسانی ارجاع شود؛ تصمیم خودکار برای ادغام انجام نشود.
- رکورد درخواست و رکورد تأییدشده کاتالوگ جدا باشند.
- ایجاد پارت یا تغییر آن بدون تأیید لازم به کاتالوگ اصلی راه نیابد.
- هر تغییر وضعیت، کاربر، زمان و علت ثبت شود.
# ۹. قواعد موجودی، تخصیص همزمان و جلوگیری از تکرار
- درخواست‌های همزمان برای یک قطعه نباید باعث تخصیص ناسازگار همان موجودی شوند؛ قفل یا تراکنش مناسب در لایه داده لازم است.
- ارسال تکراری فرم (Duplicate submission) و تلاش مجدد (Retry) نباید تقاضای تکراری یا تراکنش انبار تکراری ایجاد کند.
- تخصیص جزئی، کسری و بک‌اوردر باید بر اساس مدل موجود تعریف شوند؛ اگر مدل ندارد، گزارش شود.
- تمام تراکنش‌های انبار باید قابل ردیابی و ممیزی باشند.
- نیاز ناشی از کمبود انبار نباید به‌تنهایی باعث ایجاد خودکار پارت جدید یا سفارش خرید شود.
# ۱۰. قواعد خرید و سفارش خرید
- PO جدید از Draft شروع شود، پس از تأیید وضعیت فعلی کد و مسیر امن تغییر.
- PO واردشده (Imported) با تاریخچه Issued باید رفتار فعلی خود را حفظ کند.
- تغییر وضعیت‌ها فقط در لایه domain/application و به‌صورت سمت سرور انجام شود؛ اعتبار انتقال‌ها باید تست شود.
- فرم‌ها و فرمت‌های واردات/خروجی موجود سازگار بمانند.
- PO هرگز نباید به‌عنوان رسید پذیرفته‌شده یا ثبت موجودی تفسیر شود.
# ۱۱. فازهای اجرا و معیار پذیرش
در هر فاز ابتدا وضعیت موجود تأیید شود، سپس تغییرات حداقلی و منسجم اعمال شود، تست‌های متمرکز و رگرسیون اجرا شوند و گزارش دقیق (فایل‌ها، مهاجرت‌ها، تست‌ها، نتایج، Graphify و ریسک‌های باقی‌مانده) ارائه شود. تا معیار پذیرش فاز تأیید نشده، فاز بعدی شروع نشود.
| فاز | محدوده | معیار پذیرش |
| --- | --- | --- |
| فاز ۰: ممیزی | خواندن سورس، Graphify، نقشه وابستگی، وضعیت موجودیت‌ها، سرویس‌ها، APIها، گردش‌ها، مجوزها و تست‌ها. بدون تغییر کد عملیاتی. | گزارش وضعیت موجود، فهرست موجود/ناقص/گمشده، نقشه داده، ماتریس تأیید، طرح تست و تصمیم‌های مسدودکننده. |
| فاز ۱: کاتالوگ قطعات | جستجوی پارت موجود، تکراری‌یابی، پارت‌نامبر نامشخص، تعیین تأییدکننده واقعی. | پارت موجود قابل استفاده؛ تکراری‌ها شناسایی؛ درخواست جدا از کاتالوگ؛ تأییدکننده از انتساب فعال؛ تشخیص برای نبود تأییدکننده؛ تست‌های مجوز. |
| فاز ۲: نیازسنجی و موجودی | محاسبه موجودی قابل‌استفاده، رزروها، سفارش‌های باز، نیاز خالص؛ اتصال به Store Requisition و Inventory. | منشأ هر نیاز حفظ شود؛ عدم شمارش دوباره؛ تخصیص همزمان سازگار؛ بدون تکرار تقاضا یا تراکنش. |
| فاز ۳: درخواست تأمین و گردش تأیید | اتصال منشأها به درخواست تأمین، تعیین مسئول از ساختار واقعی، اجرای گردش از PEMS.WorkflowPlatform. | هر انتقال مجاز و ممیزی‌شده؛ بدون پیشروی غیرمجاز در نبود تأییدکننده؛ درخواست‌دهنده نتواند تأیید مستقل را دور بزند. |
| فاز ۴: خرید و انتخاب تأمین‌کننده | چرخه MR، Procurement Package، استعلام، پیشنهاد، ارزیابی، انتخاب، PO. | وضعیت‌ها سازگار با Draft؛ واردات PO حفظ شود؛ PO با رسید یکی تلقی نشود؛ تست‌های مستقل. |
| فاز ۵: دریافت و بستن چرخه | رسید جزئی و کامل، بازرسی، ثبت انبار، کنترل ارتباط رسید با PO، تطبیق فاکتور در صورت وجود. | رسید جزئی تعداد-امن؛ کالای رد/قرنطینه غیرآزاد؛ تراکنش‌ها سازگار؛ قابلیت مالی ناموجود گزارش شود. |
| فاز ۶: تست یکپارچه و استقرار | تست‌های موفق و ناموفق، مجوزها، تفکیک وظایف، هم‌زمانی، مهاجرت، رگرسیون، مستندات، Graphify. | build موفق؛ همه تست‌های الزامی پاس یا شکست با شواهد؛ مهاجرت بررسی‌شده؛ مسیرهای تأیید غیرمجاز وجود ندارد. |

# ۱۲. موارد الزامی تست
- پارت موجود تأییدشده
- درخواست پارت جدید
- شماره پارت تکراری یا مبهم
- نبود اطلاعات شماره پارت
- نبود تأییدکننده
- تأییدکننده غیرفعال یا فاقد مجوز
- تعارض درخواست‌دهنده و تأییدکننده (تفکیک وظایف)
- موجودی کافی، ناکافی، رزروشده، بلوکه‌شده و در راه
- چند تقاضای همزمان برای یک قطعه
- PO باز موجود در برابر تقاضای خرید جدید
- رسید جزئی و رسید ردشده
- ارسال تکراری و رفتار تلاش مجدد
- رد دسترسی و دسترسی متقاطع کاربران
- سازگاری واردات و خروجی موجود
- ری‌استارت، خطا و بازیابی گردش کار
# ۱۳. ایزوله‌سازی دیتابیس و Graphify
- پیش از اجرای تست‌های تغییردهنده داده، پیکربندی تست بررسی شود.
- از دیتابیس ایزوله تست یا روش جداسازی رسمی ریپو استفاده شود؛ دیتابیس توسعه مشترک نباید آلوده شود.
- Graphify قبل از پیاده‌سازی بررسی شود؛ پس از هر فاز، بازسازی آن با روش رسمی انجام و خروجی بررسی شود.
- Graphify تا زمان موفقیت واقعی عملیات، به‌روز اعلام نشود.
- مستندات معماری و گردش کار هر فاز به‌روز شود.
# ۱۴. تصمیم‌های باز که پیش از اجرا باید تعیین شوند
| موضوع | وضعیت | توضیح |
| --- | --- | --- |
| حدود ریالی و سقف تأیید هر سطح | باز | نباید حدس زده شود؛ از ماتریس اختیارات مصوب استخراج شود |
| ماتریس تأیید سمت‌ها و انتساب‌ها | باز | باید از چارت واقعی و انتسابات فعال استخراج شود |
| الزام تأیید کاتالوگ پیش از خرید در هر سناریو | باز | خرید اضطراری یا غیرانباری ممکن است مسیر متفاوت داشته باشد |
| فرمول دقیق نیاز خالص | باز | بر اساس مدل واقعی انبار و داده‌های موجود تعیین شود |
| تعداد حداقل تأمین‌کننده در استعلام | باز | سه پیشنهادی است؛ سیاست مصوب لازم است |
| وجود یا عدم وجود قابلیت AP/پرداخت | باز | در فاز صفر تأیید شود؛ اگر نبود، پیشنهاد جداگانه داده شود |
| مهاجرت یا نگهداری PO واردشده Issued | باز | نیازمند تصمیم صریح و تأیید داده‌ای |
| مرجع ارسال RFQ در شبکه بدون اینترنت | باز | پورتال داخلی، پیامک یا روش کتبی؛ بسته به زیرساخت |

# ۱۵. پروتکل کار با Claude Code
- ابتدا این سند و فایل‌های راهنمای پروژه از ریشه ریپو خوانده شوند.
- فاز ۰ بدون تغییر کد اجرا شود و خروجی آن گزارش شود.
- هر فاز: وضعیت فعلی، فهرست فایل‌ها، قوانین کسب‌وکار، پیاده‌سازی حداقلی، تست، بررسی امنیت و سازگاری، Graphify، مستندات و گزارش.
- هر جا تصمیم مدیریتی یا تغییر مخرب لازم باشد، همان بخش متوقف و تصمیم لازم گزارش شود.
- محدوده گسترش نیابد؛ هیچ کار جانبی بدون تأیید انجام نشود.
# پیوست الف: پرامپت مادر برای Claude Code
متن زیر به‌صورت مستقیم به Claude Code داده می‌شود. نام‌های لاتین موجودیت‌ها و کلاس‌ها ترجمه نمی‌شوند.
PEMS — End-to-End Parts Demand, Part Catalogue, Inventory Replenishment and Procurement Implementation

ROLE: Senior enterprise architect; ASP.NET Core 10 / EF Core 10 engineer; CMMS and inventory specialist; procurement analyst; workflow architect; security engineer; test lead.

MISSION: Audit, plan, implement and verify an integrated parts demand and procurement lifecycle in the existing PEMS repository. Implementation proceeds through audit and approval gates.

REPO: /opt/pems/source/PEMS-cmms-edit-empoleeys-master

STEP 1 — BASELINE: Read this specification file and all project instruction files at the repo root. Inspect Graphify output and its update instructions. Inspect the source directly; Graphify is an aid, not proof. Classify each capability as implemented, partial, missing, broken or unverified. Trace UI routes, controllers, services, entities, EF configurations, migrations, APIs, permissions, jobs, import/export and tests. Review schema and migration history without destructive operations. Do not write production code until the audit and dependency map are documented.

STEP 2 — CONSTRAINTS: Preserve architecture. Do not create a second workflow engine; use PEMS.WorkflowPlatform through its public APIs. Do not invent entities, APIs, permissions, assignments or positions. Do not hardcode employee IDs. Do not assign approvals to an administrator. Do not delete or bypass existing routes, permissions, workflows, imports or exports. Migrations are additive and safe. Never run destructive production DB operations. New POs start in Draft; imported POs keep their import behavior. An approved PO is not a receipt, posting, invoice approval or payment. Do not mark a phase complete without passing acceptance tests. Do not commit or push without explicit authorization.

STEP 3 — LIFECYCLE: (A) Demand sources: inventory shortage, store requisition, maintenance work order / PM, equipment, engineering or project request, unidentified part. (B) Availability: approved part identity, on-hand, usable vs blocked/quarantined, reservations, open POs, confirmed incoming receipts, reorder points, min/max, lead time, safety stock. Define and test a net-demand calculation from the actual model; never double count; never treat reserved or unusable stock as free; document gaps without fabricating values. (C) Part Catalogue Requests: extend existing workflow. Search existing approved parts first (manufacturer part number, internal number, aliases, technical identifiers). Detect duplicates; route ambiguous matches to review. Allow unknown part numbers only with required technical data. Keep requests separate from approved master records. Resolve reviewers and approvers from active assignments, positions, permissions and approved rules. Enforce segregation of duties. Diagnose missing approvers. Preserve audit and link to demand origin. (D) Replenishment: distinguish internal issue from vendor procurement; reuse inventory transaction types; no duplicate issue/receipt/MIV entity; prevent duplicate demands and repeated postings; define partial allocation, backorder and shortage; handle concurrency. (E) Procurement: audit MR, Procurement Package, RFQ/vendor inquiry, quotations, technical clarification, technical and commercial evaluation, award approval, PO create/approve/issue/close. Reuse existing entities. (F) Receiving: partial and complete receipts; PO-line and quantity validation; quality inspection and quarantine; inventory posting only after controls; invoice matching if AP exists; report absent financial capabilities as gaps — do not simulate settlement.

STEP 4 — APPROVALS: Keep employee, position, reporting line, discipline, department, identity role, permission, workflow responsibility and project scope distinct. Validate active assignment, scope, permission, delegation and segregation of duties at action time. No unordered FirstOrDefault(). No silent admin fallback. Block with a clear diagnostic when no eligible approver exists. Propose an approval matrix with unconfirmed items clearly marked. Never invent spending thresholds or claim proposed roles are configured.

STEP 5 — PHASES: Phase 0 audit and plan (no code changes). Phase 1 part catalogue requests. Phase 2 demand and inventory availability. Phase 3 requisition and workflow integration. Phase 4 procurement lifecycle. Phase 5 receiving and closeout. Phase 6 integrated verification, migration review, regression, documentation and Graphify refresh. Each phase has acceptance criteria listed in section 11 of the specification.

STEP 6 — TESTING: Use isolated test DB or the repo's established isolation. Never clean up records without proving ownership. Cover: existing approved part; new part request; duplicate or ambiguous MPN; missing part-number data; missing approver; inactive or unauthorized approver; requester/approver conflict; insufficient, reserved, blocked and incoming stock; concurrent demands; open PO vs new demand; partial and rejected receipt; duplicate submission and retry; permission denial and cross-user access; import/export compatibility; workflow restart, failure and recovery.

STEP 7 — PROTOCOL: Work incrementally. For each phase: state verified current behavior; list exact files; explain business rules and data impact; implement the smallest coherent change; run focused tests; review security and compatibility; run regression; update Graphify and docs; report evidence and unresolved findings. Stop and report when a major architecture change, destructive migration, unapproved organizational assumption or policy decision is required.

STEP 8 — FIRST RESPONSE: Phase 0 only. Return: verified current-state architecture; existing vs missing capabilities; dependency and data-flow diagram; approval-resolution matrix with unconfirmed decisions marked; phase plan with likely files; test and DB isolation strategy; risks and blocking decisions.