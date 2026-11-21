// PEMS.UI confirm and alert dialog (SCRUM-483). Replaces the browser confirm() and alert(): one shared native <dialog>, built on first use. Vanilla, offline, no inline handlers.
//   data-pems-confirm="text"          on a button, link, input or form: asks first; OK continues the click or the submit, Cancel does nothing
//   data-pems-require="field"         on a button: the named field of its form must be filled first (else it is marked is-invalid and focused, nothing is asked or sent)
//   data-pems-confirm-count="#id"    replaces {n} in the text by the text of that element (a live counter)
//   data-pems-confirm-tone="danger"   colour of the OK button (primary, danger, warning...; default primary)
//   data-pems-confirm-title="text"    heading (default: the layout's data-pems-confirm-title)
//   window.PemsDialog.confirm(text, { tone, title }) -> Promise<boolean>;  window.PemsDialog.alert(text, { title }) -> Promise<void>
// Texts are inputs: the layout puts the localised title, OK and Cancel on <body data-pems-confirm-title / -ok / -cancel>.
(function () {
    'use strict';

    var dialog = null;
    var bypass = new WeakSet();

    function text(name) {
        var body = document.body;
        return (body && body.getAttribute('data-pems-confirm-' + name)) || '';
    }

    function build() {
        if (dialog) return dialog;
        // the dialog markup is the page's template (script-templates, set shell), never a string or a createElement chain here
        dialog = window.PemsTemplate.clone('pems-confirm-dialog');
        var cancel = dialog.querySelector('[data-pems-confirm-cancel]');
        var ok = dialog.querySelector('[data-pems-confirm-ok]');
        dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close('cancel'); });
        cancel.addEventListener('click', function () { dialog.close('cancel'); });
        ok.addEventListener('click', function () { dialog.close('ok'); });
        document.body.appendChild(dialog);
        return dialog;
    }

    // One question at a time; resolves true on OK, false on Cancel, Escape or a click on the backdrop.
    function ask(message, options, alertOnly) {
        options = options || {};
        return new Promise(function (resolve) {
            var d = build();
            d.querySelector('.pems-modal__title').textContent = options.title || text('title');
            d.querySelector('.pems-confirm__message').textContent = message || '';
            var ok = d.querySelector('[data-pems-confirm-ok]');
            var cancel = d.querySelector('[data-pems-confirm-cancel]');
            ok.textContent = text('ok');
            cancel.textContent = text('cancel');
            cancel.hidden = !!alertOnly;
            ok.className = 'pems-btn pems-btn--' + (options.tone || 'primary') + ' pems-btn--medium';
            function done() { d.removeEventListener('close', done); resolve(d.returnValue === 'ok'); }
            d.addEventListener('close', done);
            d.returnValue = '';
            if (typeof d.showModal === 'function') d.showModal(); else d.setAttribute('open', '');
            (alertOnly ? ok : cancel).focus();
        });
    }

    window.PemsDialog = {
        confirm: function (message, options) { return ask(message, options, false); },
        alert: function (message, options) { return ask(message, options, true).then(function () { }); }
    };

    function message(node) {
        var text = node.getAttribute('data-pems-confirm') || '';
        var counter = node.getAttribute('data-pems-confirm-count');
        var source = counter ? document.querySelector(counter) : null;
        return source ? text.replace('{n}', source.textContent) : text;
    }

    function options(node) {
        return { tone: node.getAttribute('data-pems-confirm-tone') || undefined, title: node.getAttribute('data-pems-confirm-title') || undefined };
    }

    // Capture phase: runs before the page's own handlers, so nothing starts (loading overlay, dirty guard) until the user agrees.
    function wants(node) { return !!(node.getAttribute('data-pems-confirm')); }

    // The required field of a button is checked before anything is asked or sent.
    function requirementMet(node) {
        var name = node.getAttribute('data-pems-require');
        if (!name || !node.form) return true;
        var field = node.form.elements[name];
        if (!field || (field.value || '').trim()) return true;
        field.classList.add('is-invalid');
        field.setAttribute('aria-invalid', 'true');
        if (typeof field.focus === 'function') field.focus();
        return false;
    }

    document.addEventListener('click', function (e) {
        var node = e.target.closest ? e.target.closest('[data-pems-confirm],[data-pems-require]') : null;
        if (!node || node.tagName === 'FORM') return;
        if (bypass.has(node)) { bypass.delete(node); return; }
        if (node.disabled || node.getAttribute('aria-disabled') === 'true') return;
        if (!requirementMet(node)) { e.preventDefault(); e.stopImmediatePropagation(); return; }
        if (!wants(node)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        ask(message(node), options(node), false).then(function (yes) {
            if (!yes) return;
            bypass.add(node);
            node.click();
        });
    }, true);

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || form.tagName !== 'FORM' || !wants(form)) return;
        if (bypass.has(form)) { bypass.delete(form); return; }
        var submitter = e.submitter || null;
        e.preventDefault();
        e.stopImmediatePropagation();
        ask(message(form), options(form), false).then(function (yes) {
            if (!yes) return;
            bypass.add(form);
            if (typeof form.requestSubmit === 'function') form.requestSubmit(submitter); else form.submit();
        });
    }, true);
})();
