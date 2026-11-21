// طبق سند PEMS-SEC-PERMIT-AUTH-001 بند ۱۴: پایه‌ی PWA برای دسترسی موبایل قابل‌نصب.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/service-worker.js').catch(function () { /* غیرحیاتی */ });
    });
}
