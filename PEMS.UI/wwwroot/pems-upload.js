// PEMS.UI uploader (SCRUM-580, #580): <div data-pems-upload data-config="{...}"><input type="file"></div> is written by <pems-uploader>. The input stays the real, posting control; this file adds
// drag and drop onto the box, a list of the chosen files with their sizes and a remove button, and the same size and type check the server guard makes. A file that fails the check marks
// the input invalid (setCustomValidity), so the form cannot be posted until it is removed. All markup is a registered script template (up-*); this file only fills it.
(function () {
    'use strict';

    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }
    function slot(el, name) { return el.matches('[data-slot="' + name + '"]') ? el : el.querySelector('[data-slot="' + name + '"]'); }
    function fmt(text) { var a = arguments, s = String(text || ''); for (var i = 1; i < a.length; i++) s = s.split('{' + (i - 1) + '}').join(String(a[i])); return s; }
    function size(n) { return n >= 1048576 ? (n / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(n / 1024)) + ' KB'; }

    // accept = ".xlsx,.xlsm,image/*,application/pdf": an extension, a type or a type family
    function allowed(file, accept) {
        if (!accept) return true;
        var name = file.name.toLowerCase(), type = (file.type || '').toLowerCase();
        return accept.split(',').some(function (raw) {
            var a = raw.trim().toLowerCase();
            if (!a) return false;
            if (a.charAt(0) === '.') return name.length > a.length && name.slice(-a.length) === a;
            if (a.slice(-2) === '/*') return type.indexOf(a.slice(0, -1)) === 0;
            return type === a;
        });
    }

    function init(root) {
        if (root.getAttribute('data-pems-upload-ready') !== null) return;
        root.setAttribute('data-pems-upload-ready', '');
        var cfg = {};
        try { cfg = JSON.parse(root.getAttribute('data-config') || '{}'); } catch (e) { cfg = {}; }
        var input = root.querySelector('input[type="file"]');
        if (!input) return;
        var t = cfg.texts || {};
        var shell = tpl('up-root', { drop: t.drop, choose: t.choose });
        var zone = slot(shell, 'zone'), list = slot(shell, 'list'), pick = slot(shell, 'choose'), msg = slot(shell, 'message');
        root.appendChild(shell);
        root.classList.add('is-enhanced');
        pick.addEventListener('click', function () { input.click(); });

        function problems(files) {
            var out = [];
            files.forEach(function (f) {
                if (cfg.maxBytes && f.size > cfg.maxBytes) out.push(fmt(t.tooBig, f.name, cfg.maxMb));
                else if (!allowed(f, cfg.accept)) out.push(fmt(t.wrongType, f.name, cfg.accept));
            });
            return out;
        }

        function render() {
            var files = Array.prototype.slice.call(input.files || []);
            list.textContent = '';
            files.forEach(function (f, i) {
                var row = tpl('up-file', { name: f.name, size: size(f.size), remove: t.remove });
                slot(row, 'remove').addEventListener('click', function () { drop(i); });
                list.appendChild(row);
            });
            var bad = problems(files);
            msg.textContent = bad.join(' ');
            input.setCustomValidity(bad.join(' '));
            root.classList.toggle('has-error', bad.length > 0);
            root.classList.toggle('has-files', files.length > 0);
        }

        function replace(files) {
            var dt = new DataTransfer();
            files.forEach(function (f) { dt.items.add(f); });
            input.files = dt.files;
            render();
        }
        function drop(index) { var files = Array.prototype.slice.call(input.files || []); files.splice(index, 1); replace(files); }

        input.addEventListener('change', render);
        ['dragenter', 'dragover'].forEach(function (ev) { zone.addEventListener(ev, function (e) { e.preventDefault(); zone.classList.add('is-over'); }); });
        ['dragleave', 'drop'].forEach(function (ev) { zone.addEventListener(ev, function (e) { e.preventDefault(); zone.classList.remove('is-over'); }); });
        zone.addEventListener('drop', function (e) {
            var dropped = Array.prototype.slice.call((e.dataTransfer && e.dataTransfer.files) || []);
            if (!dropped.length) return;
            replace(cfg.multiple ? Array.prototype.slice.call(input.files || []).concat(dropped) : dropped.slice(0, 1));
            input.dispatchEvent(new Event('input', { bubbles: true }));
        });
        render();
    }

    function scan(scope) { (scope || document).querySelectorAll('[data-pems-upload]').forEach(init); }
    function boot() {
        scan(document);
        new MutationObserver(function (list) { list.forEach(function (m) { m.addedNodes.forEach(function (n) { if (n.nodeType === 1) { if (n.matches && n.matches('[data-pems-upload]')) init(n); scan(n); } }); }); }).observe(document.body, { childList: true, subtree: true });
    }
    window.PemsUpload = { init: init, scan: scan, allowed: allowed };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
