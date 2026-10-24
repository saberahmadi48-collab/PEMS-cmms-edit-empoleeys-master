// اعلان‌های لحظه‌ای (SignalR) + همگام‌سازی شمارنده با دیتابیس + اکشن‌های Dismiss/MarkAll
(function () {
    var userId = document.body.getAttribute('data-user-id');
    if (!userId) return;

    function setBadge(count) {
        // شمارنده‌های سایدبار داده‌محور (sidebar-badges.js) هم با هر تغییر اعلان دوباره از سرور خوانده شوند
        document.dispatchEvent(new CustomEvent('pems:notifications-changed'));
        var badge = document.getElementById('notifBadge');
        if (!badge) return;
        count = parseInt(count, 10) || 0;
        badge.textContent = count > 0 ? String(count) : '';
        badge.classList.toggle('d-none', count <= 0);
    }

    // شمارنده‌ی مرجع همیشه از سرور (دیتابیس) می‌آید؛ شمارش محلی (+1) قابل اتکا نیست
    function refreshCount() {
        return fetch('/Notification/UnreadCount', { cache: 'no-store', credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d) setBadge(d.unread); })
            .catch(function () { });
    }

    function token() {
        var input = document.querySelector('#notifAf input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function post(url, id) {
        var body = new URLSearchParams();
        body.append('__RequestVerificationToken', token());
        if (id) body.append('id', id);
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        }).then(function (r) { return r.ok ? r.json() : null; });
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('#notifDropdownBody [data-notif-action]');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation(); // منوی کشویی باز بماند

        var action = btn.getAttribute('data-notif-action');
        if (action === 'dismiss') {
            var id = btn.getAttribute('data-notif-id');
            post('/Notification/Dismiss', id).then(function (d) {
                if (!d) return;
                var row = btn.closest('[data-notif-id]');
                if (row) row.remove();
                setBadge(d.unread);
            });
        } else if (action === 'markAll') {
            post('/Notification/MarkAllRead').then(function (d) {
                if (!d) return;
                setBadge(d.unread);
                if (typeof loadNotifDropdown === 'function') loadNotifDropdown();
            });
        }
    });

    if (typeof signalR === 'undefined') { refreshCount(); return; }

    // هویت از کوکی احراز هویت در سرور استخراج می‌شود؛ هیچ userId ای از کلاینت فرستاده نمی‌شود
    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/notifications')
        .withAutomaticReconnect()
        .build();

    connection.on('ReceiveNotification', function (n, unread) {
        if (typeof showLiveToast === 'function') showLiveToast(n.title, false);
        setBadge(unread);
    });

    connection.on('UnreadCountChanged', setBadge);

    // اعلان‌هایی که هنگام قطع بودن اتصال رسیده‌اند: بعد از Reconnect شمارنده از سرور بازیابی می‌شود
    connection.onreconnected(refreshCount);

    connection.start()
        .then(refreshCount)
        .catch(function () { /* اتصال زنده اختیاری است - نبودش نباید کار کاربر را مختل کند */ });
})();
