// Copy / print buttons for a secret that is shown exactly once (SCRUM-354: temporary admin password).
// Markup contract: a container with [data-pems-secret-reveal] holding [data-secret-value], [data-secret-copy] (optional data-copied-text),
// [data-secret-print] and optionally [data-secret-download="file-name.txt"].
(function () {
    'use strict';

    function init(root) {
        if (root.dataset.pemsInit === '1') return;
        root.dataset.pemsInit = '1';

        var value = root.querySelector('[data-secret-value]');
        var copy = root.querySelector('[data-secret-copy]');
        var print = root.querySelector('[data-secret-print]');

        if (copy && value) {
            copy.addEventListener('click', function () {
                var text = value.textContent.trim();
                var done = function () {
                    var copied = copy.getAttribute('data-copied-text');
                    if (copied) { copy.textContent = copied; }
                };
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text).then(done, function () { /* the value stays selectable (user-select-all) */ });
                }
            });
        }

        if (print) {
            print.addEventListener('click', function () { window.print(); });
        }

        // SCRUM-355: download the recovery codes as a text file; built in the browser, nothing is requested from the server
        var download = root.querySelector('[data-secret-download]');
        if (download && value) {
            download.addEventListener('click', function () {
                var blob = new Blob([value.textContent.trim() + '\r\n'], { type: 'text/plain' });
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = download.getAttribute('data-secret-download') || 'pems-secret.txt';
                document.body.appendChild(a);
                a.click();
                a.remove();
                URL.revokeObjectURL(url);
            });
        }
    }

    function boot() {
        document.querySelectorAll('[data-pems-secret-reveal]').forEach(init);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
