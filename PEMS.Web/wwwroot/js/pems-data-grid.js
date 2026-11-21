// PEMS — Global Data Grid Standard (بندهای ۱۷–۲۰)
//
// طبق سند: پهنای ستون باید به‌ازای کاربر پایدار بماند - چون این پروژه هیچ زیرساخت
// User Preference سمت-سرور برای این منظور ندارد (بررسی شد: هیچ جدول UserPreference/
// ColumnPreference/GridPreference در PEMS.Domain وجود ندارد)، طبق بند ۹/۱۸ سند از
// localStorage به‌عنوان مکانیزم سبک اولیه استفاده می‌شود - کلید ذخیره‌سازی
// ("DataGrid.{GridKey}.ColumnWidths") عمداً همان الگوی پیشنهادی سند است تا اگر بعداً
// زیرساخت Preference سمت-سرور اضافه شد، فقط لایه‌ی ذخیره‌سازی عوض شود، نه قرارداد کلید.
(function (window, document) {
    'use strict';

    function storageKey(gridKey) {
        return 'DataGrid.' + gridKey + '.ColumnWidths';
    }

    function loadWidths(gridKey) {
        try {
            var raw = window.localStorage.getItem(storageKey(gridKey));
            return raw ? JSON.parse(raw) : {};
        } catch (e) {
            return {};
        }
    }

    function saveWidths(gridKey, widths) {
        try {
            window.localStorage.setItem(storageKey(gridKey), JSON.stringify(widths));
        } catch (e) {
            // طبق بند صریح این نشست: ذخیره‌ی پهنای ستون یک Best-Effort UX است، نه یک الزام
            // کسب‌وکاری - اگر localStorage در دسترس نبود (حالت خصوصی/سهمیه‌ی پر)، Grid باید
            // با پهنای پیش‌فرض کار کند، نه این‌که خطا پرتاب کند
        }
    }

    function applyStoredWidths(table, gridKey) {
        var widths = loadWidths(gridKey);
        var headers = table.querySelectorAll('th[data-col-key]');
        headers.forEach(function (th) {
            var key = th.getAttribute('data-col-key');
            if (Object.prototype.hasOwnProperty.call(widths, key)) {
                th.style.width = widths[key] + 'px';
            }
        });
    }

    function initColumnResize(gridKey) {
        var table = document.querySelector('table[data-grid-key="' + gridKey + '"]');
        if (!table) return;

        applyStoredWidths(table, gridKey);

        var headers = table.querySelectorAll('th[data-col-key][data-resizable="true"]');
        headers.forEach(function (th) {
            var handle = window.PemsTemplate.clone('col-resize-handle');
            th.appendChild(handle);

            var startX = 0;
            var startWidth = 0;
            var minWidth = parseInt(th.getAttribute('data-min-width') || '80', 10);
            var maxWidth = parseInt(th.getAttribute('data-max-width') || '500', 10);
            var isRtl = document.documentElement.getAttribute('dir') === 'rtl';

            function onPointerMove(e) {
                var clientX = e.touches ? e.touches[0].clientX : e.clientX;
                var delta = clientX - startX;
                if (isRtl) delta = -delta;
                var newWidth = Math.min(maxWidth, Math.max(minWidth, startWidth + delta));
                th.style.width = newWidth + 'px';
            }

            function onPointerUp() {
                handle.classList.remove('pems-resizing');
                document.removeEventListener('mousemove', onPointerMove);
                document.removeEventListener('mouseup', onPointerUp);
                document.removeEventListener('touchmove', onPointerMove);
                document.removeEventListener('touchend', onPointerUp);

                var widths = loadWidths(gridKey);
                widths[th.getAttribute('data-col-key')] = th.offsetWidth;
                saveWidths(gridKey, widths);
            }

            function onPointerDown(e) {
                startX = e.touches ? e.touches[0].clientX : e.clientX;
                startWidth = th.offsetWidth;
                handle.classList.add('pems-resizing');
                document.addEventListener('mousemove', onPointerMove);
                document.addEventListener('mouseup', onPointerUp);
                document.addEventListener('touchmove', onPointerMove);
                document.addEventListener('touchend', onPointerUp);
                e.preventDefault();
            }

            handle.addEventListener('mousedown', onPointerDown);
            handle.addEventListener('touchstart', onPointerDown, { passive: false });
        });
    }

    // طبق بند ۱۹: بازنشانی ستون‌ها به پهنای پیش‌فرض سیستم
    function resetColumns(gridKey) {
        try {
            window.localStorage.removeItem(storageKey(gridKey));
        } catch (e) {
            // نادیده گرفتن - در بدترین حالت فقط بارگذاری مجدد پهنای فعلی را نگه می‌دارد
        }
        window.location.reload();
    }

    window.PemsDataGrid = {
        initColumnResize: initColumnResize,
        resetColumns: resetColumns
    };
})(window, document);
