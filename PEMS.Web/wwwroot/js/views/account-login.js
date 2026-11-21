// طبق «PEMS Premium Three-Theme UI/UX Redesign» (بند ۲۳/۲۸): نمونه‌ی محدود و کم‌ریسک
// از سیستم Loading تازه - فقط روی همین فرم، بدون دست‌زدن به رفتار Submit یا اعتبارسنجی
// موجود (طبق بند ۹ (touch/loading-buttons) اسکیل ui-ux-pro-max: دکمه باید حین کار
// async غیرفعال شود و بازخورد بدهد).
(function () {
    var form = document.querySelector('form[action*="Login"], form');
    var btn = document.getElementById('loginSubmitBtn');
    var label = document.getElementById('loginSubmitLabel');
    if (!form || !btn || !label) return;
    form.addEventListener('submit', function () {
        if (!form.checkValidity || form.checkValidity()) {
            btn.disabled = true;
            label.replaceChildren(PemsTemplate.clone('login-busy'), document.createTextNode(' ' + label.dataset.signingIn));
        }
    });
})();
