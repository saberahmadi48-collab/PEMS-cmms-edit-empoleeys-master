// PEMS.UI rich-text editor (SCRUM-59). Loaded once by the layout. The one editor of PEMS: form fields of kind RichText and the Letter pages.
//
// Every <textarea data-pems-rte> becomes a Quill editor (vendored Quill 1.3.7, BSD-3-Clause, PEMS.Web/wwwroot/lib/quill). Quill is loaded
// only when such a field is on the page. The textarea stays the form value: it is updated on every change and on submit, so the form posts
// and validates as before, and without JavaScript it is a plain textarea.
// The HTML the editor produces is NOT trusted: the server sanitizes it (IRichTextSanitizerService, FormValueSanitizer for form fields).
//   data-pems-rte                     the field
//   data-pems-rte-for="<id>"          on any element: enhance the textarea with this id (adapter for pages that cannot mark the textarea)
//   data-pems-rte-upload="/X/Y"       the image button uploads to this URL (antiforgery token from the form, answer { url }); without it
//                                     there is no image button (images are never pasted as Base64)
//   data-pems-rte-label="..."         accessible name of the editing area (default: the field's <label>)
// On a data-pems-rte-for element the upload and label attributes are read from that element.
// Texts and the Quill folder are inputs: the layout puts them on <body data-pems-rte-texts='{json}' data-pems-rte-lib="/lib/quill/">
// (the first element on the page carrying them is used, so a partial in a layout without them can bring its own).
// Direction: a Persian page (dir=rtl) starts new text right-to-left and right-aligned, an English page left-to-right; the direction
// button switches the current paragraph, so mixed Persian/English text works on both.
// API: textarea.quillEditor (the Quill instance), the 'rte-change' event on the textarea, PemsRte.init(root) for content added later.
(function () {
    'use strict';
    if (window.PemsRte) return;

    var FONTS = ['sans-serif', 'serif', 'monospace', 'tahoma', 'arial', 'times-new-roman', 'b-nazanin'];
    var SIZES = ['10px', '12px', '14px', '16px', '18px', '20px', '24px', '32px', '40px'];
    var texts = null;
    var loading = null;
    var registered = false;

    function t(key) {
        if (texts === null) {
            var host = document.querySelector('[data-pems-rte-texts]');
            try { texts = JSON.parse((host && host.getAttribute('data-pems-rte-texts')) || '{}'); } catch (e) { texts = {}; }
        }
        return texts[key] || '';
    }

    function libPath() {
        var host = document.querySelector('[data-pems-rte-lib]');
        var p = (host && host.getAttribute('data-pems-rte-lib')) || '/lib/quill/';
        return p.charAt(p.length - 1) === '/' ? p : p + '/';
    }

    // Quill on demand: one <link> and one <script>, shared by every field of the page.
    function loadQuill() {
        if (window.Quill) return Promise.resolve(window.Quill);
        if (loading) return loading;
        loading = new Promise(function (resolve, reject) {
            var base = libPath();
            if (!document.querySelector('link[data-pems-rte-css]')) {
                var css = document.createElement('link');
                css.rel = 'stylesheet';
                css.href = base + 'quill.snow.min.css';
                css.setAttribute('data-pems-rte-css', '');
                document.head.appendChild(css);
            }
            var s = document.createElement('script');
            s.src = base + 'quill.min.js';
            s.onload = function () { window.Quill ? resolve(window.Quill) : reject(new Error('Quill missing')); };
            s.onerror = reject;
            document.head.appendChild(s);
        });
        return loading;
    }

    function register(Quill) {
        if (registered) return;
        registered = true;
        var Font = Quill.import('formats/font');
        Font.whitelist = FONTS;
        Quill.register(Font, true);
        var Size = Quill.import('attributors/style/size');
        Size.whitelist = SIZES;
        Quill.register(Size, true);
    }

    function toolbar(withImage) {
        var media = withImage ? ['blockquote', 'link', 'image'] : ['blockquote', 'link'];
        return [
            [{ font: FONTS }, { size: SIZES }],
            ['bold', 'italic', 'underline', 'strike'],
            [{ color: [] }, { background: [] }],
            [{ script: 'sub' }, { script: 'super' }],
            [{ header: [1, 2, 3, false] }],
            [{ list: 'ordered' }, { list: 'bullet' }],
            [{ indent: '-1' }, { indent: '+1' }],
            [{ direction: 'rtl' }],
            [{ align: [] }],
            media,
            ['clean']
        ];
    }

    function pageIsRtl(textarea) {
        var host = textarea.closest('[dir]') || document.documentElement;
        return (host.getAttribute('dir') || '').toLowerCase() === 'rtl';
    }

    function labelOf(textarea, source) {
        var given = source.getAttribute('data-pems-rte-label');
        if (given) return given;
        if (textarea.id) {
            var label = document.querySelector('label[for="' + textarea.id + '"]');
            if (label) return label.textContent.replace(/\*/g, '').trim();
        }
        return t('editor');
    }

    // Quill builds an icon-only toolbar: name every control (localised) and expose the editing area as a labelled textbox.
    function nameToolbar(quill, name, describedBy) {
        var tb = quill.getModule('toolbar').container;
        tb.setAttribute('role', 'toolbar');
        tb.setAttribute('aria-label', name);
        tb.querySelectorAll('button').forEach(function (b) {
            var cls = (b.className.match(/ql-([a-z]+)/) || [])[1], val = b.getAttribute('value');
            var key = cls === 'script' ? (val === 'sub' ? 'sub' : 'super')
                : cls === 'list' ? (val === 'ordered' ? 'ordered' : 'bullet')
                : cls === 'indent' ? (val === '-1' ? 'outdent' : 'indent') : cls;
            b.setAttribute('type', 'button');
            if (t(key)) { b.setAttribute('aria-label', t(key)); b.setAttribute('title', t(key)); }
        });
        tb.querySelectorAll('.ql-picker').forEach(function (p) {
            var cls = (p.className.match(/ql-(font|size|color|background|header|align)/) || [])[1];
            if (!cls || !t(cls)) return;
            var lab = p.querySelector('.ql-picker-label');
            if (lab) { lab.setAttribute('aria-label', t(cls)); lab.setAttribute('title', t(cls)); }
            p.querySelectorAll('.ql-picker-item').forEach(function (it) {
                if (!it.getAttribute('aria-label')) it.setAttribute('aria-label', t(cls) + ' ' + (it.getAttribute('data-label') || it.getAttribute('data-value') || ''));
            });
        });
        tb.querySelectorAll('select').forEach(function (s) {
            var cls = (s.className.match(/ql-(font|size|color|background|header|align)/) || [])[1];
            if (cls && t(cls)) s.setAttribute('aria-label', t(cls));
        });
        var tip = quill.container.querySelector('.ql-tooltip input');
        if (tip && t('link')) tip.setAttribute('aria-label', t('link'));
        quill.root.setAttribute('role', 'textbox');
        quill.root.setAttribute('aria-multiline', 'true');
        quill.root.setAttribute('aria-label', name);
        if (describedBy) quill.root.setAttribute('aria-describedby', describedBy);
    }

    function uploadHandler(quill, url, textarea) {
        return function () {
            var input = document.createElement('input');
            input.type = 'file';
            input.accept = 'image/png,image/jpeg,image/gif,image/webp';
            input.onchange = function () {
                var file = input.files[0];
                if (!file) return;
                var data = new FormData();
                data.append('file', file);
                var scope = textarea.closest('form') || document;
                var token = scope.querySelector('input[name="__RequestVerificationToken"]');
                if (token) data.append('__RequestVerificationToken', token.value);
                fetch(url, { method: 'POST', body: data, credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                    .then(function (res) {
                        var range = quill.getSelection(true);
                        quill.insertEmbed(range.index, 'image', res.url);
                    })
                    .catch(function () { if (window.PemsErrors) window.PemsErrors.show(t('uploadFailed')); });   // SCRUM-549: a toast, never a browser pop-up
            };
            input.click();
        };
    }

    function isEmpty(quill) { return quill.getText().trim().length === 0 && !quill.root.querySelector('img'); }

    function build(Quill, textarea, source) {
        if (textarea.dataset.pemsRteInit === 'done') return;
        textarea.dataset.pemsRteInit = 'done';
        register(Quill);

        var upload = source.getAttribute('data-pems-rte-upload');
        var readOnly = textarea.readOnly || textarea.disabled;
        var holder = window.PemsTemplate.clone('rte-box');
        var area = holder.firstElementChild;
        // the editor sits directly under its textarea, inside the field's own column
        textarea.parentNode.insertBefore(holder, textarea.nextSibling);
        textarea.hidden = true;
        textarea.style.display = 'none';      // a page rule giving the textarea a display would beat [hidden]

        // a hidden required control blocks the submit without showing why: the check moves to the editor (the server checks again)
        var required = textarea.required;
        textarea.required = false;

        var quill = new Quill(area, {
            theme: 'snow',
            readOnly: readOnly,
            modules: { toolbar: readOnly ? false : toolbar(!!upload) }
        });
        if (!readOnly) nameToolbar(quill, labelOf(textarea, source), textarea.getAttribute('aria-describedby'));
        else { quill.root.setAttribute('aria-label', labelOf(textarea, source)); quill.root.setAttribute('aria-readonly', 'true'); }
        if (upload && !readOnly) quill.getModule('toolbar').addHandler('image', uploadHandler(quill, upload, textarea));

        if (textarea.value) {
            quill.clipboard.dangerouslyPasteHTML(textarea.value);   // the stored value was sanitized by the server
        } else if (pageIsRtl(textarea)) {
            quill.formatLine(0, 1, { direction: 'rtl', align: 'right' });
        }
        quill.history.clear();

        function sync() { textarea.value = isEmpty(quill) ? '' : quill.root.innerHTML; }
        quill.on('text-change', function () {
            sync();
            holder.classList.remove('pems-rte-box--invalid');
            // the value changes from code, so input/change do not fire; pages that track edits (draft autosave) listen to this
            textarea.dispatchEvent(new Event('rte-change', { bubbles: true }));
        });
        if (textarea.getAttribute('aria-invalid') === 'true') holder.classList.add('pems-rte-box--invalid');

        var form = textarea.form || textarea.closest('form');
        if (form) {
            form.addEventListener('submit', function (e) {
                sync();
                if (required && !textarea.value) {
                    e.preventDefault();
                    holder.classList.add('pems-rte-box--invalid');
                    quill.root.setAttribute('aria-invalid', 'true');
                    quill.focus();
                }
            });
        }
        textarea.quillEditor = quill;
    }

    function targets(root) {
        var list = [];
        (root || document).querySelectorAll('textarea[data-pems-rte]').forEach(function (ta) { list.push([ta, ta]); });
        (root || document).querySelectorAll('[data-pems-rte-for]').forEach(function (el) {
            var ta = document.getElementById(el.getAttribute('data-pems-rte-for'));
            if (ta && ta.tagName === 'TEXTAREA') list.push([ta, el]);
        });
        return list.filter(function (p) { return p[0].dataset.pemsRteInit !== 'done'; });
    }

    function init(root) {
        var list = targets(root);
        if (!list.length) return Promise.resolve();
        return loadQuill().then(function (Quill) {
            list.forEach(function (p) { build(Quill, p[0], p[1]); });
        }).catch(function () { /* Quill could not load: the plain textareas stay and still post */ });
    }

    window.PemsRte = { init: init };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(document); });
    else init(document);
})();
