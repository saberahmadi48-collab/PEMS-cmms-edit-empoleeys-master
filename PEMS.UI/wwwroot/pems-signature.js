/* SCRUM-729: the drawn signature (component "signature-pad"). Draws on the canvas with the mouse or a finger, and posts the PNG as a data URL to the action named by data-action, as JSON, with the
   antiforgery token of the page. No markup is built here: the component wrote the canvas, the buttons and the status line. */
(function () {
    'use strict';

    function token() { var el = document.querySelector('input[name="__RequestVerificationToken"]'); return el ? el.value : ''; }

    function init(host) {
        var canvas = host.querySelector('[data-pems-signature-canvas]');
        var status = host.querySelector('[data-pems-signature-status]');
        if (!canvas || !canvas.getContext) return;
        var ctx = canvas.getContext('2d');
        var drawn = false, drawing = false, lastX = 0, lastY = 0;

        function blank() {
            // the canvas is styled to the width of its box: the pixel size follows, so the stroke stays sharp
            var rect = canvas.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) { canvas.width = rect.width; canvas.height = rect.height; }
            ctx.fillStyle = '#fff';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.lineWidth = 2; ctx.lineCap = 'round'; ctx.strokeStyle = '#1a1a1a';
            drawn = false;
        }
        function pos(e) {
            var rect = canvas.getBoundingClientRect();
            var p = e.touches && e.touches.length > 0 ? e.touches[0] : e;
            return { x: p.clientX - rect.left, y: p.clientY - rect.top };
        }
        function start(e) { drawing = true; var p = pos(e); lastX = p.x; lastY = p.y; e.preventDefault(); }
        function move(e) {
            if (!drawing) return;
            var p = pos(e);
            ctx.beginPath(); ctx.moveTo(lastX, lastY); ctx.lineTo(p.x, p.y); ctx.stroke();
            lastX = p.x; lastY = p.y; drawn = true; e.preventDefault();
        }
        function end() { drawing = false; }
        function say(text, bad) { if (status) { status.textContent = text; status.classList.toggle('text-danger', !!bad); status.classList.toggle('text-success', !bad); } }

        blank();
        canvas.addEventListener('mousedown', start); canvas.addEventListener('mousemove', move); window.addEventListener('mouseup', end);
        canvas.addEventListener('touchstart', start, { passive: false }); canvas.addEventListener('touchmove', move, { passive: false }); canvas.addEventListener('touchend', end);

        host.querySelector('[data-pems-signature-clear]').addEventListener('click', function () { blank(); say('', false); });
        host.querySelector('[data-pems-signature-save]').addEventListener('click', function () {
            if (!drawn) { say(host.getAttribute('data-empty'), true); return; }
            var body = {}; body[host.getAttribute('data-field') || 'signatureDataUrl'] = canvas.toDataURL('image/png');
            fetch(host.getAttribute('data-action'), { method: 'POST', credentials: 'same-origin', headers: { 'Accept': 'application/json', 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify(body) })
                .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
                .then(function (answer) { say(answer && answer.message ? answer.message : host.getAttribute('data-saved'), false); })
                .catch(function () { say(host.getAttribute('data-error'), true); });
        });
    }

    function boot() { document.querySelectorAll('[data-pems-signature]').forEach(init); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
