// طبق سند PEMS-SEC-PERMIT-AUTH-001: مراسم WebAuthn سمت کلاینت - هرگز داده‌ی بیومتریک را
// نمی‌بیند یا لمس نمی‌کند؛ فقط API استاندارد navigator.credentials مرورگر (که خودِ سیستم‌عامل/
// Authenticator دستگاه را صدا می‌زند) و Assertion رمزنگاری‌شده‌ی حاصل را به سرور می‌فرستد.

function base64urlToBuffer(base64url) {
    const padding = "=".repeat((4 - (base64url.length % 4)) % 4);
    const base64 = (base64url + padding).replace(/-/g, "+").replace(/_/g, "/");
    const raw = atob(base64);
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    return bytes.buffer;
}

function bufferToBase64url(buffer) {
    const bytes = new Uint8Array(buffer);
    let str = "";
    for (const b of bytes) str += String.fromCharCode(b);
    return btoa(str).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function pemsRegisterPasskey(nickname, antiForgeryToken) {
    if (!window.PublicKeyCredential) {
        return { success: false, error: "این مرورگر از WebAuthn/Passkey پشتیبانی نمی‌کند." };
    }

    const optionsResp = await fetch("/WebAuthn/GetRegistrationOptions");
    if (!optionsResp.ok) return { success: false, error: "دریافت گزینه‌های ثبت ناموفق بود." };
    const optionsJson = await optionsResp.text();
    const options = JSON.parse(optionsJson);

    const publicKey = {
        ...options,
        challenge: base64urlToBuffer(options.challenge),
        user: { ...options.user, id: base64urlToBuffer(options.user.id) },
        excludeCredentials: (options.excludeCredentials || []).map(c => ({ ...c, id: base64urlToBuffer(c.id) }))
    };

    let credential;
    try {
        credential = await navigator.credentials.create({ publicKey });
    } catch (e) {
        return { success: false, error: "ثبت Passkey لغو شد یا ناموفق بود: " + e.message };
    }

    const attestationResponseJson = JSON.stringify({
        id: credential.id,
        rawId: bufferToBase64url(credential.rawId),
        type: credential.type,
        response: {
            attestationObject: bufferToBase64url(credential.response.attestationObject),
            clientDataJSON: bufferToBase64url(credential.response.clientDataJSON)
        }
    });

    const form = new FormData();
    form.append("attestationResponseJson", attestationResponseJson);
    form.append("optionsJson", optionsJson);
    form.append("nickname", nickname || "");
    form.append("__RequestVerificationToken", antiForgeryToken);

    const completeResp = await fetch("/WebAuthn/CompleteRegistration", { method: "POST", body: form });
    return await completeResp.json();
}

async function pemsPerformStepUp() {
    if (!window.PublicKeyCredential) {
        return { success: false, error: "این مرورگر از WebAuthn/Passkey پشتیبانی نمی‌کند." };
    }

    const optionsResp = await fetch("/WebAuthn/GetAssertionOptions");
    if (!optionsResp.ok) {
        let error = "هیچ Passkey ای برای این حساب ثبت نشده - قبل از این اقدام از بخش Manage Passkeys یک Passkey ثبت کنید.";
        try { const body = await optionsResp.json(); if (body.error) error = body.error; } catch { /* ignore */ }
        return { success: false, error };
    }
    const optionsJson = await optionsResp.text();
    const options = JSON.parse(optionsJson);

    const publicKey = {
        ...options,
        challenge: base64urlToBuffer(options.challenge),
        allowCredentials: (options.allowCredentials || []).map(c => ({ ...c, id: base64urlToBuffer(c.id) }))
    };

    let assertion;
    try {
        assertion = await navigator.credentials.get({ publicKey });
    } catch (e) {
        return { success: false, error: "احراز هویت Passkey لغو شد یا ناموفق بود: " + e.message };
    }

    const assertionResponseJson = JSON.stringify({
        id: assertion.id,
        rawId: bufferToBase64url(assertion.rawId),
        type: assertion.type,
        response: {
            authenticatorData: bufferToBase64url(assertion.response.authenticatorData),
            clientDataJSON: bufferToBase64url(assertion.response.clientDataJSON),
            signature: bufferToBase64url(assertion.response.signature),
            userHandle: assertion.response.userHandle ? bufferToBase64url(assertion.response.userHandle) : null
        }
    });

    return { success: true, assertionResponseJson, optionsJson };
}

// روی هر فرم اقدام حساس صدا زده می‌شود - به‌جای Submit مستقیم، اول مراسم Step-up را انجام
// می‌دهد، دو فیلد مخفی را پر می‌کند، سپس فرم را Submit می‌کند.
// extraFieldName/extraFieldValue: چون دکمه دیگر Submit واقعی نیست، الگوی استاندارد HTML
// «name/value روی خودِ دکمه‌ی Submit» (مثل decision=Approve/Reject) دیگر خودکار کار نمی‌کند -
// این‌جا به‌صورت صریح یک فیلد مخفی معادل ساخته/به‌روزرسانی می‌شود.
async function pemsSubmitWithStepUp(button, extraFieldName, extraFieldValue) {
    const formEl = button.closest("form");
    // چون دکمه دیگر type="submit" نیست، Validation استاندارد HTML5 (مثل required) به‌صورت
    // خودکار اجرا نمی‌شود - این‌جا صریحاً فراخوانی می‌شود تا همون تجربه‌ی کاربری حفظ شود.
    if (formEl && !formEl.reportValidity()) return;

    if (extraFieldName) {
        let ef = formEl.querySelector(`input[name="${extraFieldName}"]`);
        if (!ef) { ef = document.createElement("input"); ef.type = "hidden"; ef.name = extraFieldName; formEl.appendChild(ef); }
        ef.value = extraFieldValue;
    }

    button.disabled = true;
    const originalText = button.textContent;
    button.textContent = "در انتظار تایید Passkey...";

    const result = await pemsPerformStepUp();

    if (!result.success) {
        if (typeof showLiveToast === "function") { showLiveToast(result.error, true); } else { console.error(result.error); }
        button.disabled = false;
        button.textContent = originalText;
        return;
    }

    const form = button.closest("form");
    let a = form.querySelector('input[name="webAuthnAssertionJson"]');
    if (!a) { a = document.createElement("input"); a.type = "hidden"; a.name = "webAuthnAssertionJson"; form.appendChild(a); }
    a.value = result.assertionResponseJson;

    let o = form.querySelector('input[name="webAuthnOptionsJson"]');
    if (!o) { o = document.createElement("input"); o.type = "hidden"; o.name = "webAuthnOptionsJson"; form.appendChild(o); }
    o.value = result.optionsJson;

    form.submit();
}
