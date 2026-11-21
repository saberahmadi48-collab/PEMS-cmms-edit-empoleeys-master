// PEMS — Maintenance Scheduling Calendar: Drag & Drop reschedule (Week view only).
// طبق سند بند ۱۶: Drag فقط روز را در همان ردیف تیم جابه‌جا می‌کند (ساعت/مدت‌زمان حفظ می‌شود) -
// هر جابه‌جایی از همان Action دامنه‌ای (MaintenanceCalendar/Reschedule -> ScheduleWorkOrderAsync)
// عبور می‌کند که مجوز/دیسیپلین/مرحله را دقیقاً مثل فرم غیر-Drag بررسی می‌کند. جایگزین بدون
// Drag همان فرم "Planned Start/Planned Finish" در WorkOrder/Details است (طبق بند ۱۶: باید یک
// جایگزین قابل‌دسترس وجود داشته باشد).
(function () {
    'use strict';

    function antiForgeryToken() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function showError(message) {
        var container = document.querySelector('.content-card');
        if (!container) { if (window.PemsErrors) window.PemsErrors.show(message); return; }   // SCRUM-549: never a browser pop-up
        var banner = window.PemsTemplate.fill(window.PemsTemplate.clone('mc-error'), { text: message });
        container.parentElement.insertBefore(banner, container);
    }

    async function reschedule(workOrderId, plannedStart, plannedFinish) {
        var body = new URLSearchParams();
        body.append('workOrderId', workOrderId);
        body.append('plannedStart', plannedStart);
        body.append('plannedFinish', plannedFinish);
        body.append('__RequestVerificationToken', antiForgeryToken());

        var res = await fetch('/MaintenanceCalendar/Reschedule', { method: 'POST', body: body });
        var data = await res.json();
        if (data.success) {
            window.location.reload();
        } else {
            showError(data.error || 'Reschedule failed.');
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var cards = document.querySelectorAll('.pems-cal-card[draggable="true"]');
        var cells = document.querySelectorAll('.pems-cal-week-cell');
        if (cards.length === 0 || cells.length === 0) return;

        cards.forEach(function (card) {
            card.addEventListener('dragstart', function (e) {
                e.dataTransfer.setData('application/json', JSON.stringify({
                    workOrderId: card.getAttribute('data-wo'),
                    start: card.getAttribute('data-start'),
                    finish: card.getAttribute('data-finish')
                }));
                e.dataTransfer.effectAllowed = 'move';
            });
        });

        cells.forEach(function (cell) {
            cell.addEventListener('dragover', function (e) {
                if (!cell.getAttribute('data-date')) return;
                e.preventDefault();
                cell.classList.add('pems-cal-dragover');
            });
            cell.addEventListener('dragleave', function () { cell.classList.remove('pems-cal-dragover'); });
            cell.addEventListener('drop', function (e) {
                e.preventDefault();
                cell.classList.remove('pems-cal-dragover');
                var newDate = cell.getAttribute('data-date');
                if (!newDate) return;

                var raw = e.dataTransfer.getData('application/json');
                if (!raw) return;
                var payload = JSON.parse(raw);

                var oldStart = new Date(payload.start);
                var oldFinish = new Date(payload.finish);
                var durationMs = oldFinish.getTime() - oldStart.getTime();

                var target = new Date(newDate + 'T00:00:00');
                var newStart = new Date(target.getFullYear(), target.getMonth(), target.getDate(),
                    oldStart.getHours(), oldStart.getMinutes(), 0, 0);
                var newFinish = new Date(newStart.getTime() + durationMs);

                function toLocalIso(d) {
                    var pad = function (n) { return String(n).padStart(2, '0'); };
                    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
                        'T' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':00';
                }

                reschedule(payload.workOrderId, toLocalIso(newStart), toLocalIso(newFinish));
            });
        });
    });
})();
