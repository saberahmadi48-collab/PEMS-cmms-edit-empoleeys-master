/* PEMS Part Catalogue - client behaviour (progressive enhancement; every rule is re-validated on the server). */
(function () {
    'use strict';
    // markup comes from the page's registered templates (script-templates component, pems-template.js), never from a string in this file
    function tpl(name, values) { return window.PemsTemplate.fill(window.PemsTemplate.clone(name), values || {}); }

    var page = document.querySelector('.pc-page');
    if (!page) return;

    var txt = {
        noResults: page.dataset.txtNoResults || '',
        search: page.dataset.txtSearch || '',
        local: page.dataset.txtLocal || '',
        mfrExists: page.dataset.txtManufacturerExists || '',
        mfrSimilar: page.dataset.txtManufacturerSimilar || ''
    };
    var lookupBase = page.dataset.pcLookupBase || '/Warehouse/PartCatalogueLookup';

    function debounce(fn, ms) {
        var t;
        return function () {
            var a = arguments, c = this;
            clearTimeout(t);
            t = setTimeout(function () { fn.apply(c, a); }, ms);
        };
    }

    function getJson(url) {
        return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : []; })
            .catch(function () { return []; });
    }

    function fire(el) { el.dispatchEvent(new Event('change', { bubbles: true })); }

    /* ------------------------------------------------------------------ server-side searchable dropdown */
    // The option list is rendered in <body> with position:fixed ("portal") and positioned from the input's viewport rect.
    // Reason: every card is .vs-panel (overflow:hidden) and some fields live in .modal-body / .pc-table-scroll (overflow:auto),
    // so an absolutely positioned child list was clipped to the card ("half-open dropdown"). z-index (1056) only wins over the
    // Bootstrap modal (1055) once the list is no longer inside the clipping ancestor. Flips upward when there is no room below.
    function initLookup(host) {
        if (host.dataset.pcReady) return;
        host.dataset.pcReady = '1';

        var inputId = host.dataset.inputId || ('pcl_' + Math.random().toString(36).slice(2));
        var listId = inputId + '_list';

        var input = PemsTemplate.clone('pc-combobox-input');
        input.type = 'text';
        input.id = inputId;
        input.autocomplete = 'off';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', listId);
        input.placeholder = txt.search;
        input.value = host.dataset.text || '';

        var hidden = tpl('pc-hidden');
        hidden.name = host.dataset.name;
        if (host.dataset.form) hidden.setAttribute('form', host.dataset.form);
        var initial = host.dataset.id;
        hidden.value = (initial && initial !== '0') ? initial : '';

        var list = tpl('pc-lookup-list');
        list.id = listId;

        host.appendChild(input);
        host.appendChild(hidden);
        document.body.appendChild(list);

        var items = [], active = -1, candidates = [], seq = 0, isOpen = false;

        function place() {
            var r = input.getBoundingClientRect();
            if (r.bottom < 0 || r.top > window.innerHeight) { close(); return; }   // input scrolled out of view
            var vh = window.innerHeight, vw = window.innerWidth, gap = 2, margin = 8;
            var below = vh - r.bottom - margin, above = r.top - margin;
            var up = below < 160 && above > below;
            var avail = Math.max(120, Math.min(320, up ? above : below)) - gap;
            var width = Math.min(Math.max(r.width, 220), vw - 2 * margin);
            var rtl = getComputedStyle(input).direction === 'rtl';
            var left = rtl ? r.right - width : r.left;                              // align to the inline-start edge
            left = Math.min(Math.max(margin, left), vw - width - margin);
            list.dir = rtl ? 'rtl' : 'ltr';
            list.style.width = width + 'px';
            list.style.left = left + 'px';
            list.style.maxHeight = avail + 'px';
            if (up) { list.style.top = 'auto'; list.style.bottom = (vh - r.top + gap) + 'px'; }
            else { list.style.bottom = 'auto'; list.style.top = (r.bottom + gap) + 'px'; }
        }
        function onViewport(e) { if (e && e.target === list) return; if (isOpen) place(); }

        function close() {
            list.hidden = true; isOpen = false; active = -1;
            input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant');
            window.removeEventListener('resize', onViewport);
            window.removeEventListener('scroll', onViewport, true);
        }
        function open() {
            list.hidden = false;
            if (!isOpen) {
                isOpen = true;
                window.addEventListener('resize', onViewport);
                window.addEventListener('scroll', onViewport, true);
            }
            input.setAttribute('aria-expanded', 'true');
            place();
        }

        function highlight(i, scroll) {
            var nodes = list.querySelectorAll('.pc-lookup-item');
            if (active >= 0 && nodes[active]) nodes[active].classList.remove('active');
            active = i;
            if (active >= 0 && nodes[active]) {
                nodes[active].classList.add('active');
                input.setAttribute('aria-activedescendant', nodes[active].id);
                if (scroll) nodes[active].scrollIntoView({ block: 'nearest' });
            } else input.removeAttribute('aria-activedescendant');
        }

        function render() {
            list.replaceChildren();
            if (!items.length) {
                list.appendChild(tpl('pc-lookup-empty', { text: txt.noResults }));
            }
            items.forEach(function (it, i) {
                var li = tpl('pc-lookup-item', { text: it.text });
                li.id = listId + '_o' + i;
                li.addEventListener('mousedown', function (e) { e.preventDefault(); pick(it, 'manual'); });
                li.addEventListener('mousemove', function () { if (active !== i) highlight(i, false); });
                list.appendChild(li);
            });
            active = -1;
            open();
        }

        function setValue(it, source) {
            hidden.value = it.id;
            input.value = it.text;
            hidden.dataset.extra = it.extra || '';
            hidden.dataset.source = source || 'manual';
            candidates = [];
            close();
            fire(hidden);
        }
        function pick(it, source) { setValue(it, source); }

        function clearValue() {
            hidden.value = ''; input.value = ''; hidden.dataset.extra = ''; hidden.dataset.source = '';
            fire(hidden);
        }

        // programmatic API used by FREE FORMAT TEXT resolution (same hidden field, same change event as a manual pick)
        host.pcSetValue = function (it, source) { setValue(it, source); };
        host.pcClear = clearValue;
        host.pcSetCandidates = function (arr) { candidates = Array.isArray(arr) ? arr : []; };

        var search = debounce(function () {
            var q = input.value.trim();
            var my = ++seq;
            getJson(host.dataset.url + '?q=' + encodeURIComponent(q)).then(function (res) {
                if (my !== seq) return;                                              // a newer query superseded this answer
                items = Array.isArray(res) ? res : [];
                render();
            });
        }, 250);

        input.addEventListener('input', function () {
            candidates = [];
            if (hidden.value) { hidden.value = ''; hidden.dataset.source = ''; fire(hidden); }
            search();
        });
        input.addEventListener('focus', function () {
            if (hidden.value) return;
            if (candidates.length) { seq++; items = candidates.slice(); render(); }   // ambiguous FREE FORMAT resolution: offer the candidates first
            else search();
        });
        input.addEventListener('blur', function () {
            setTimeout(close, 120);
            if (!hidden.value) input.value = '';
        });
        input.addEventListener('keydown', function (e) {
            if (list.hidden && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) { search(); e.preventDefault(); return; }
            if (e.key === 'ArrowDown') { highlight(Math.min(items.length - 1, active + 1), true); e.preventDefault(); }
            else if (e.key === 'ArrowUp') { highlight(Math.max(0, active - 1), true); e.preventDefault(); }
            else if (e.key === 'Home' && !list.hidden && items.length) { highlight(0, true); e.preventDefault(); }
            else if (e.key === 'End' && !list.hidden && items.length) { highlight(items.length - 1, true); e.preventDefault(); }
            else if (e.key === 'Enter' && !list.hidden && active >= 0) { pick(items[active], 'manual'); e.preventDefault(); }
            else if (e.key === 'Escape' && !list.hidden) { close(); e.stopPropagation(); }   // do not let Escape also dismiss a parent modal
        });
    }

    document.querySelectorAll('.pc-lookup').forEach(initLookup);

    /* ------------------------------------------------------------------ Technical Class long description */
    var tcHidden = document.querySelector('input[name="TechnicalClassId"]');
    var tcLong = document.getElementById('pc_TechnicalClassLong');
    if (tcHidden && tcLong) {
        tcHidden.addEventListener('change', function () {
            if (!tcHidden.value) { tcLong.textContent = ''; return; }
            window.PemsSkeleton.show(tcLong, 'text', { lines: 2 });   // SCRUM-337: while the description loads
            fetch(lookupBase + '/TechnicalClass?id=' + encodeURIComponent(tcHidden.value), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (it) { tcLong.textContent = it && it.extra ? it.extra : ''; })
                .catch(function () { tcLong.textContent = ''; });
        });
    }

    /* ------------------------------------------------------------------ Section -> Group1 -> Group2 */
    var secSel = document.querySelector('[data-pc-section]');
    var g1 = document.querySelector('[data-pc-group1]');
    var g2 = document.querySelector('[data-pc-group2]');

    function fillSelect(sel, items, keep) {
        var current = keep ? sel.value : '';
        sel.replaceChildren(new Option('', ''));
        items.forEach(function (it) {
            var o = new Option(it.text, it.id);
            if (String(it.id) === String(current)) o.selected = true;
            sel.appendChild(o);
        });
    }

    if (secSel && g1) {
        secSel.addEventListener('change', function () {
            var url = lookupBase + '/Group1' + (secSel.value ? '?sectionId=' + encodeURIComponent(secSel.value) : '');
            getJson(url).then(function (res) { fillSelect(g1, res, true); if (g2) { fillSelect(g2, [], false); } });
        });
    }
    if (g1 && g2) {
        g1.addEventListener('change', function () {
            var url = lookupBase + '/Group2' + (g1.value ? '?group1Id=' + encodeURIComponent(g1.value) : '');
            getJson(url).then(function (res) { fillSelect(g2, res, true); });
        });
    }

    /* ------------------------------------------------------------------ Manufacturer: LOCAL -> NO_PART_NR, new manufacturer */
    var mfrHost = document.querySelector('[data-pc-manufacturer]');
    var mfrHidden = mfrHost ? mfrHost.querySelector('input[type=hidden]') : null;
    var mfrInput = mfrHost ? mfrHost.querySelector('input[type=text]') : null;
    var mpn = document.getElementById('pc_ManufacturerPartNumber');
    var isNew = document.getElementById('pc_IsNewManufacturer');
    var newName = document.getElementById('pc_NewManufacturerName');
    var check = document.getElementById('pc_ManufacturerCheck');

    function isLocalSelected() {
        return !!(mfrHidden && mfrHidden.value && mfrHidden.dataset.extra === 'LOCAL')
            || !!(mfrInput && mfrInput.value.trim().toUpperCase() === 'LOCAL' && mfrHidden && mfrHidden.value);
    }

    function applyLocal() {
        if (!mpn) return;
        if (isLocalSelected()) {
            mpn.value = 'NO_PART_NR';
            mpn.readOnly = true;
            mpn.title = txt.local;
        } else {
            if (mpn.readOnly && mpn.value === 'NO_PART_NR') mpn.value = '';
            mpn.readOnly = false;
            mpn.title = '';
        }
    }

    if (mfrHidden) mfrHidden.addEventListener('change', applyLocal);
    applyLocal();

    function applyNew() {
        if (!isNew || !newName) return;
        newName.disabled = !isNew.checked;
        if (isNew.checked) {
            if (mfrHidden) { mfrHidden.value = ''; }
            if (mfrInput) { mfrInput.value = ''; mfrInput.disabled = true; }
        } else {
            if (mfrInput) mfrInput.disabled = false;
            newName.value = '';
            if (check) check.textContent = '';
        }
        applyLocal();
    }
    if (isNew) { isNew.addEventListener('change', applyNew); if (isNew.checked && mfrInput) mfrInput.disabled = true; }

    if (newName) {
        newName.addEventListener('input', function () {
            // Latin uppercase only (the server enforces the same rule)
            var v = newName.value.toUpperCase().replace(/[^A-Z0-9 .,&()\-\/]/g, '');
            if (v !== newName.value) newName.value = v;
        });
        newName.addEventListener('input', debounce(function () {
            var name = newName.value.trim();
            if (!check) return;
            if (name.length < 2) { check.textContent = ''; check.className = 'form-text'; return; }
            fetch(lookupBase + '/CheckManufacturer?name=' + encodeURIComponent(name), { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (res) {
                    if (!res) return;
                    var exact = res.exact || [], similar = res.similar || [];
                    if (exact.length) { check.textContent = txt.mfrExists + ' ' + exact.join(', '); check.className = 'form-text text-danger'; }
                    else if (similar.length) { check.textContent = txt.mfrSimilar + ' ' + similar.join(', '); check.className = 'form-text text-warning'; }
                    else { check.textContent = ''; check.className = 'form-text'; }
                })
                .catch(function () { });
        }, 400));
    }

    /* ------------------------------------------------------------------ Type Designation -> Critical / Capital (display only) */
    var typeSel = document.querySelector('[data-pc-type]');
    var critEl = document.getElementById('pcIsCritical');
    var capEl = document.getElementById('pcIsCapital');
    function paintPill(el, on) {
        if (!el) return;
        var yes = el.dataset.yes, no = el.dataset.no;
        if (yes === undefined) { el.dataset.yes = page.dataset.txtYes || ''; el.dataset.no = page.dataset.txtNo || ''; }
        el.classList.toggle('vs-pill--danger', on);
        el.classList.toggle('vs-pill--neutral', !on);
    }
    if (typeSel) {
        var yesText = critEl ? (critEl.dataset.yes || '') : '';
        typeSel.addEventListener('change', function () {
            var opt = typeSel.options[typeSel.selectedIndex];
            var flags = opt ? (opt.dataset.flags || '').split(',') : [];
            var cr = flags.indexOf('CR') >= 0, cap = flags.indexOf('CAP') >= 0;
            [[critEl, cr], [capEl, cap]].forEach(function (p) {
                if (!p[0]) return;
                var yes = page.dataset.txtYes, no = page.dataset.txtNo;
                if (yes && no) p[0].textContent = p[1] ? yes : no;
                p[0].classList.toggle('vs-pill--danger', p[1]);
                p[0].classList.toggle('vs-pill--neutral', !p[1]);
            });
        });
    }

    /* ------------------------------------------------------------------ Planning method A -> 0 */
    var planSel = document.querySelector('[data-pc-planning]');
    var minStock = document.getElementById('pc_MinimumStock');
    var maxRate = document.getElementById('pc_MaxConsumptionRate');
    function applyPlanning() {
        if (!planSel || !minStock || !maxRate) return;
        var opt = planSel.options[planSel.selectedIndex];
        var isA = opt && (opt.dataset.code || '').toUpperCase() === 'A';
        [minStock, maxRate].forEach(function (el) {
            if (isA) { el.value = '0'; el.readOnly = true; } else { el.readOnly = false; }
        });
    }
    if (planSel) { planSel.addEventListener('change', applyPlanning); applyPlanning(); }

    /* ------------------------------------------------------------------ FREE FORMAT TEXT is single line + Technical Class suggestion */
    var fft = document.getElementById('pc_FreeFormatText');
    if (fft) {
        var normalize = function () { fft.value = fft.value.replace(/[\r\n\t]+/g, ' '); };
        fft.addEventListener('input', normalize);
        // A text <input> silently drops line breaks on paste ("LINE ONE\nLINE TWO" -> "LINE ONELINE TWO"), gluing words together and defeating
        // the Technical Class match. Insert pasted multi-line text with the breaks turned into single spaces instead.
        fft.addEventListener('paste', function (e) {
            var cd = e.clipboardData || window.clipboardData;
            var text = cd ? cd.getData('text') : '';
            if (text && /[\r\n\t]/.test(text)) {
                e.preventDefault();
                var flat = text.replace(/\s*[\r\n\t]+\s*/g, ' ');
                var s = fft.selectionStart, en = fft.selectionEnd;
                fft.setRangeText(flat, s == null ? fft.value.length : s, en == null ? fft.value.length : en, 'end');
                fft.dispatchEvent(new Event('input', { bubbles: true }));
            } else {
                setTimeout(normalize, 0);
            }
        });
        fft.addEventListener('keydown', function (e) { if (e.key === 'Enter') e.preventDefault(); });

        // The server proposes a class (exact code / name-prefix); the browser never decides. A class the user picked by hand is never
        // overwritten. A class this feature set automatically is cleared again when the text no longer supports it.
        var tcHost = document.querySelector('.pc-lookup[data-name="TechnicalClassId"]');
        var tcStatus = document.getElementById('pc_TcResolveStatus');
        if (tcHost && tcHidden && tcStatus) {
            var seqR = 0;
            var say = function (kind, text) { tcStatus.className = 'form-text pc-tc-status pc-tc-status--' + kind; tcStatus.textContent = text || ''; };
            var isAuto = function () { return tcHidden.dataset.source === 'auto'; };
            var showCandidates = function (cands) {
                cands.forEach(function (c) {
                    var b = tpl('pc-tc-cand', { text: c.text });
                    b.addEventListener('click', function () { tcHost.pcSetValue(c, 'manual'); say('ok', tcStatus.dataset.txtPicked || ''); });
                    tcStatus.appendChild(b);
                });
            };
            var apply = function (res) {
                tcHost.pcSetCandidates([]);
                if (res.status === 'resolved' && res.resolved) {
                    if (!tcHidden.value || isAuto()) {
                        tcHost.pcSetValue(res.resolved, 'auto');
                        say('ok', (tcStatus.dataset.txtResolved || '') + ' ' + res.resolved.text);
                    } else if (String(res.resolved.id) === String(tcHidden.value)) {
                        say('ok', (tcStatus.dataset.txtResolved || '') + ' ' + res.resolved.text);
                    } else {
                        say('warn', (tcStatus.dataset.txtMismatch || '') + ' ' + res.resolved.text);
                    }
                    return;
                }
                if (isAuto()) tcHost.pcClear();                                       // stale automatic resolution
                if (res.status === 'ambiguous' && res.candidates && res.candidates.length) {
                    tcHost.pcSetCandidates(res.candidates);
                    say('warn', tcStatus.dataset.txtAmbiguous || '');
                    showCandidates(res.candidates);
                } else {
                    say('muted', tcHidden.value ? '' : (tcStatus.dataset.txtNone || ''));
                }
            };
            var resolveNow = debounce(function () {
                var text = fft.value.trim();
                var my = ++seqR;
                if (!text) { if (isAuto()) tcHost.pcClear(); say('muted', ''); return; }
                fetch(lookupBase + '/ResolveTechnicalClass?text=' + encodeURIComponent(text.slice(0, 1000)),
                      { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (res) { if (my !== seqR || !res) return; apply(res); })     // drop answers for superseded text
                    .catch(function () { });
            }, 450);
            fft.addEventListener('input', resolveNow);
        }
    }

    /* ------------------------------------------------------------------ dynamic attribute rows */
    var addAttr = document.getElementById('pcAddAttr');
    var attrTpl = document.getElementById('pcAttrTemplate');
    var attrTable = document.getElementById('pcAttrTable');
    if (addAttr && attrTpl && attrTable) {
        addAttr.addEventListener('click', function () {
            var idx = parseInt(addAttr.dataset.nextIndex || '1', 10);
            // the row is the page's own <template> (pcAttrTemplate): cloned, its __i__ placeholders replaced in the attributes
            var row = attrTpl.content.cloneNode(true);
            row.querySelectorAll('*').forEach(function (el) {
                Array.prototype.forEach.call(el.attributes, function (a) {
                    if (a.value.indexOf('__i__') >= 0) el.setAttribute(a.name, a.value.replace(/__i__/g, String(idx)));
                });
            });
            attrTable.tBodies[0].appendChild(row);
            addAttr.dataset.nextIndex = String(idx + 1);
        });
        attrTable.addEventListener('click', function (e) {
            var btn = e.target.closest('.pc-attr-remove');
            if (btn) { var row = btn.closest('tr'); if (row) row.remove(); }
        });
    }

    /* ------------------------------------------------------------------ generate-number button lives in an independent form */
    document.querySelectorAll('[data-pc-confirm]').forEach(function (b) { b.setAttribute('data-pems-confirm', b.dataset.pcConfirm); });

    /* ------------------------------------------------------------------ Admin: positions multi-select -> CSV (option order) */
    document.querySelectorAll('select[data-pc-pos-target]').forEach(function (sel) {
        var hid = document.getElementById(sel.dataset.pcPosTarget);
        if (!hid) return;
        sel.addEventListener('change', function () {
            hid.value = Array.prototype.map.call(sel.selectedOptions, function (o) { return o.value; }).join(',');
        });
    });

    /* ------------------------------------------------------------------ Admin: resolver diagnostics (server-rendered partial) */
    var prevOut = document.getElementById('pcPreviewOut');
    if (prevOut) {
        var runPreview = function (key) {
            var keySel = document.getElementById('pcPrevKey');
            if (key && keySel) keySel.value = key;
            var req = document.querySelector('#pcPrevReqHost input[type=hidden]');
            var grp = document.querySelector('#pcPrevGrpHost input[type=hidden]');
            var url = '/Warehouse/PartCatalogueAdmin/Preview?key=' + encodeURIComponent(keySel.value)
                + '&requesterEmployeeId=' + encodeURIComponent((req && req.value) || '0')
                + (grp && grp.value ? '&commodityGroupId=' + encodeURIComponent(grp.value) : '');
            fetch(url, { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.text() : ''; })
                .then(function (html) { window.PemsTemplate.html(prevOut, html); });   // HTML comes from our own server-encoded partial
        };
        document.querySelectorAll('[data-pc-preview]').forEach(function (b) {
            b.addEventListener('click', function () { runPreview(b.dataset.pcPreview); });
        });
    }
})();
