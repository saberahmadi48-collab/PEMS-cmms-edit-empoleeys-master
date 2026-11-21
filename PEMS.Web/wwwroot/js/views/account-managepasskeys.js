    async function registerNow() {
        const nickname = document.getElementById('nickname').value;
        const token = document.querySelector('#registerForm input[name="__RequestVerificationToken"]').value;
        const alertBox = document.getElementById('pems-passkey-alert');
        PemsTemplate.clear(alertBox);

        const result = await pemsRegisterPasskey(nickname, token);

        if (result.success) {
            location.reload();
        } else {
            alertBox.replaceChildren(PemsTemplate.fill(PemsTemplate.clone('passkey-error'), { text: result.error || 'ثبت ناموفق بود.' }));
        }
    }
