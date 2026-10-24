// شمارنده‌های زنده‌ی سایدبار داده‌محور: بارگذاری اولیه، هر ۶۰ ثانیه، و فوراً پس از هر رویداد اعلان SignalR (notifications-hub.js).
// فقط کلیدهایی که در سایدبار همین کاربر رندر شده‌اند درخواست می‌شوند؛ سرور هم دوباره مجوز هر شمارنده را می‌سنجد.
(function () {
    var badges = document.querySelectorAll('.pems-sidebar [data-nav-badge]');
    if (!badges.length) return;

    var keys = Array.from(new Set(Array.from(badges).map(function (b) { return b.getAttribute('data-nav-badge'); })));
    var inFlight = false;

    function apply(counts) {
        badges.forEach(function (b) {
            var n = parseInt(counts[b.getAttribute('data-nav-badge')], 10) || 0;
            b.textContent = n > 99 ? '99+' : (n > 0 ? String(n) : '');
            b.classList.toggle('d-none', n <= 0);
            var link = b.closest('.nav-link');
            if (link) {
                var label = link.querySelector('.nav-label');
                // صفحه‌خوان: «عنوان، ۵ مورد» (بدون تکیه بر رنگ)
                b.setAttribute('aria-label', n > 0 ? (label ? label.textContent.trim() + ': ' : '') + n : '');
            }
        });
    }

    function refresh() {
        if (inFlight || document.hidden) return;
        inFlight = true;
        var qs = keys.map(function (k) { return 'keys=' + encodeURIComponent(k); }).join('&');
        fetch('/NavigationBadge/Counts?' + qs, { cache: 'no-store', credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d) apply(d); })
            .catch(function () { /* شمارنده اختیاری است؛ خطای شبکه نباید صفحه را مختل کند */ })
            .finally(function () { inFlight = false; });
    }

    refresh();
    setInterval(refresh, 60000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) refresh(); });
    document.addEventListener('pems:notifications-changed', refresh);
})();
