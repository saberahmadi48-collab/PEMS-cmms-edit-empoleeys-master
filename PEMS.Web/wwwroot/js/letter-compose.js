/* Formal letter compose form: recipients (directory search + CSV), approval chain, auto-save, attachments, preview, submit. */
(function () {
    'use strict';

    var root = document.getElementById('lcRoot');
    var bootEl = document.getElementById('lcBoot');
    if (!root || !bootEl) return;

    var boot = JSON.parse(bootEl.textContent);
    var T = boot.t;
    var $ = function (id) { return document.getElementById(id); };

    var state = {
        letterId: boot.letterId,
        rowVersion: boot.rowVersion,
        recipients: { to: boot.to.slice(), cc: boot.cc.slice(), bcc: boot.bcc.slice() },
        approvers: boot.approvers.slice(),
        dirty: false,
        saving: false,
        queued: false,
        submitting: false
    };

    function token() {
        var el = root.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    function postJson(url, payload) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify(payload),
            credentials: 'same-origin'
        });
    }

    function text(el, value) { el.textContent = value; }

    // ---------------------------------------------------------------- save state indicator
    var saveStateEl = $('lcSaveState'), saveTextEl = $('lcSaveText');
    function setSaveState(kind, message) {
        saveStateEl.setAttribute('data-state', kind);
        var icon = saveStateEl.querySelector('i');
        icon.className = 'bi ' + (kind === 'saving' ? 'bi-arrow-repeat' : kind === 'error' ? 'bi-exclamation-triangle' : kind === 'dirty' ? 'bi-pencil' : 'bi-cloud-check');
        text(saveTextEl, message);
    }

    // ---------------------------------------------------------------- payload
    function val(id) { var el = $(id); return el ? el.value : ''; }
    function intOrNull(id) { var v = val(id); return v ? parseInt(v, 10) : null; }
    function ids(list) { return list.map(function (r) { return r.id; }); }

    function buildInput() {
        var respReq = $('lcRespReq').checked;
        var apprReq = boot.isOutgoing ? true : $('lcApprReq').checked;
        var manual = $('lcManual') ? $('lcManual').value.trim() : '';
        return {
            letterId: state.letterId,
            parentLetterId: boot.parentLetterId,
            isReferral: !!boot.isReferral,
            letterType: boot.letterType,
            category: val('lcCategory') || null,
            subject: val('lcSubject'),
            body: val('composeBody'),
            priority: val('lcPriority'),
            confidentialityLevel: val('lcConf'),
            letterDate: val('lcDate') || null,
            referenceNumber: val('lcRef') || null,
            projectId: intOrNull('lcProject'),
            disciplineId: intOrNull('lcDiscipline'),
            relatedDepartmentId: intOrNull('lcRelDept'),
            toEmployeeIds: ids(state.recipients.to),
            ccEmployeeIds: ids(state.recipients.cc),
            bccEmployeeIds: ids(state.recipients.bcc),
            externalReceiver: boot.isOutgoing ? val('lcExternal') : null,
            correspondentPartyId: boot.isOutgoing ? intOrNull('lcParty') : null,
            responseRequired: respReq,
            responseDueDate: respReq ? (val('lcRespDue') || null) : null,
            approvalRequired: apprReq,
            approverUserIds: apprReq ? ids(state.approvers) : [],
            manualNumber: manual || null
        };
    }

    // ---------------------------------------------------------------- alerts / field errors
    var alertsEl = $('lcAlerts');
    function clearIssues() {
        alertsEl.innerHTML = '';
        root.querySelectorAll('[data-error-for]').forEach(function (e) { e.hidden = true; e.textContent = ''; });
        root.querySelectorAll('[aria-invalid="true"]').forEach(function (e) { e.removeAttribute('aria-invalid'); });
    }

    function renderIssues(errors, warnings) {
        clearIssues();
        errors = errors || []; warnings = warnings || [];
        function block(kind, title, items, icon) {
            if (!items.length) return;
            var div = document.createElement('div');
            div.className = 'alert alert-' + kind;
            div.setAttribute('role', kind === 'danger' ? 'alert' : 'status');
            var h = document.createElement('strong');
            h.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i> ';
            h.appendChild(document.createTextNode(title));
            div.appendChild(h);
            var ul = document.createElement('ul');
            items.forEach(function (i) { var li = document.createElement('li'); li.textContent = i.message; ul.appendChild(li); });
            div.appendChild(ul);
            alertsEl.appendChild(div);
        }
        block('danger', T.errorsTitle, errors, 'bi-x-octagon');
        block('warning', T.warningsTitle, warnings, 'bi-exclamation-triangle');

        var first = null;
        errors.forEach(function (e) {
            if (!e.field) return;
            var holder = root.querySelector('[data-error-for="' + e.field + '"]');
            if (holder) {
                holder.textContent = e.message; holder.hidden = false;
                var group = holder.closest('.lc-field');
                var control = group && group.querySelector('input,select,textarea,.ql-editor');
                if (control) { control.setAttribute('aria-invalid', 'true'); if (!first) first = control; }
            }
        });
        if (first && first.focus) first.focus();
        else if (errors.length) alertsEl.scrollIntoView({ block: 'center' });
    }

    // ---------------------------------------------------------------- recipients
    function allRecipientCount() { return state.recipients.to.length + state.recipients.cc.length + state.recipients.bcc.length; }

    function updateRecipientSummary() {
        var n = allRecipientCount();
        text($('lcRcptCount'), n ? n + ' / ' + boot.maxRecipients : '');
        var msgs = [];
        if (n >= boot.warnRecipients) msgs.push({ message: T.manyRecipients.replace('{0}', n) });
        var ext = [];
        ['to', 'cc', 'bcc'].forEach(function (f) { state.recipients[f].forEach(function (r) { if (r.isExternal) ext.push(r.name); }); });
        if (ext.length) msgs.push({ message: T.externalWarn.replace('{0}', ext.slice(0, 5).join(', ') + (ext.length > 5 ? '…' : '')) });
        // هشدارهای زنده فقط در پنل جدا؛ خطاهای سرور را پاک نمی‌کند
        var live = alertsEl.querySelector('.lc-live');
        if (live) live.remove();
        if (msgs.length) {
            var div = document.createElement('div');
            div.className = 'alert alert-warning lc-live';
            div.setAttribute('role', 'status');
            msgs.forEach(function (m) { var p = document.createElement('div'); p.textContent = m.message; div.appendChild(p); });
            alertsEl.insertBefore(div, alertsEl.firstChild);
        }
    }

    function renderChips(field) {
        var box = root.querySelector('[data-chipbox="' + field + '"]');
        var input = box.querySelector('input');
        box.querySelectorAll('.lc-chip').forEach(function (c) { c.remove(); });
        state.recipients[field].forEach(function (r, idx) {
            var chip = document.createElement('span');
            chip.className = 'lc-chip';
            if (r.isExternal) chip.setAttribute('data-external', '1');
            var label = document.createElement('span');
            label.textContent = r.name + (r.isExternal ? ' · ' + T.external : '');
            if (r.position) label.title = r.position;
            var rm = document.createElement('button');
            rm.type = 'button';
            rm.setAttribute('aria-label', T.remove + ' ' + r.name);
            rm.innerHTML = '<i class="bi bi-x" aria-hidden="true"></i>';
            rm.addEventListener('click', function () {
                state.recipients[field].splice(idx, 1);
                renderChips(field); markDirty(); input.focus();
            });
            chip.appendChild(label); chip.appendChild(rm);
            box.insertBefore(chip, input);
        });
        updateRecipientSummary();
    }

    function addRecipient(field, item) {
        var exists = ['to', 'cc', 'bcc'].some(function (f) { return state.recipients[f].some(function (r) { return r.id === item.id; }); });
        if (exists) return false;
        if (allRecipientCount() >= boot.maxRecipients) { renderIssues([{ message: T.tooMany, field: 'recipients' }], []); return false; }
        state.recipients[field].push({ id: item.id, name: item.name, position: item.position, isExternal: !!item.isExternal });
        renderChips(field); markDirty();
        return true;
    }

    var searchCache = {};
    function searchDirectory(q) {
        if (searchCache[q]) return Promise.resolve(searchCache[q]);
        return fetch(boot.urls.directory + '?take=15&q=' + encodeURIComponent(q), { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (list) { searchCache[q] = list; return list; })
            .catch(function () { return []; });
    }

    function wireCombobox(field) {
        var input = $('lcIn_' + field), list = $('lcList_' + field), box = root.querySelector('[data-chipbox="' + field + '"]');
        var items = [], active = -1, timer = null, seq = 0;

        function close() { list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); active = -1; }
        function setActive(i) {
            var opts = list.querySelectorAll('li[role="option"]');
            opts.forEach(function (o, idx) { o.setAttribute('aria-selected', idx === i ? 'true' : 'false'); });
            active = i;
            if (opts[i]) { input.setAttribute('aria-activedescendant', opts[i].id); opts[i].scrollIntoView({ block: 'nearest' }); }
        }
        function choose(i) {
            var it = items[i]; if (!it) return;
            addRecipient(field, it); input.value = ''; close(); input.focus();
        }
        function render() {
            list.innerHTML = '';
            if (!items.length) {
                var empty = document.createElement('li'); empty.className = 'lc-empty'; empty.textContent = T.noResults; list.appendChild(empty);
            }
            items.forEach(function (it, i) {
                var li = document.createElement('li');
                li.id = 'lcOpt_' + field + '_' + i; li.setAttribute('role', 'option'); li.setAttribute('aria-selected', 'false');
                var name = document.createElement('span'); name.textContent = it.name + (it.isExternal ? ' · ' + T.external : '');
                var sub = document.createElement('small'); sub.textContent = [it.position, it.department].filter(Boolean).join(' — ');
                li.appendChild(name); li.appendChild(sub);
                li.addEventListener('mousedown', function (e) { e.preventDefault(); choose(i); });
                list.appendChild(li);
            });
            list.hidden = false; input.setAttribute('aria-expanded', 'true');
        }
        function run() {
            var q = input.value.trim(); var mine = ++seq;
            searchDirectory(q).then(function (res) {
                if (mine !== seq) return;
                var taken = {}; ['to', 'cc', 'bcc'].forEach(function (f) { state.recipients[f].forEach(function (r) { taken[r.id] = true; }); });
                items = res.filter(function (r) { return !taken[r.id]; }); render(); setActive(items.length ? 0 : -1);
            });
        }

        input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(run, 180); });
        input.addEventListener('focus', run);
        input.addEventListener('blur', function () { setTimeout(close, 120); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); if (list.hidden) run(); else setActive(Math.min(active + 1, items.length - 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(Math.max(active - 1, 0)); }
            else if (e.key === 'Enter') { if (!list.hidden && active >= 0) { e.preventDefault(); choose(active); } }
            else if (e.key === 'Escape') { close(); }
            else if (e.key === 'Backspace' && !input.value && state.recipients[field].length) {
                state.recipients[field].pop(); renderChips(field); markDirty();
            }
        });
        box.addEventListener('click', function () { input.focus(); });
        renderChips(field);
    }

    ['to', 'cc', 'bcc'].forEach(wireCombobox);

    // Bcc فقط با درخواست کاربر باز می‌شود (یا اگر از قبل گیرنده‌ی Bcc دارد)
    var bccField = root.querySelector('[data-field="bcc"]');
    var toggleBcc = $('lcToggleBcc');
    function showBcc(show) { bccField.hidden = !show; toggleBcc.setAttribute('aria-expanded', show ? 'true' : 'false'); }
    toggleBcc.addEventListener('click', function () { showBcc(bccField.hidden); if (!bccField.hidden) $('lcIn_bcc').focus(); });
    showBcc(state.recipients.bcc.length > 0);

    // CSV / text import
    $('lcImportBtn').addEventListener('click', function () { $('lcImportFile').click(); });
    $('lcImportFile').addEventListener('change', function () {
        var file = this.files[0]; this.value = '';
        if (!file) return;
        var fd = new FormData();
        fd.append('file', file); fd.append('__RequestVerificationToken', token());
        fetch(boot.urls.resolve, { method: 'POST', body: fd, credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : r.text().then(function (m) { throw new Error(m); }); })
            .then(function (res) {
                var added = 0;
                res.matched.forEach(function (m) { if (addRecipient('to', m)) added++; });
                var msgs = [{ message: T.imported.replace('{0}', added) }];
                if (res.unmatched && res.unmatched.length) msgs.push({ message: T.unmatched.replace('{0}', res.unmatched.length) + ': ' + res.unmatched.slice(0, 10).join(', ') });
                if (res.truncated) msgs.push({ message: T.truncated.replace('{0}', res.max) });
                renderIssues([], msgs);
                updateRecipientSummary();
            })
            .catch(function (e) { renderIssues([{ message: e.message || T.uploadFailed }], []); });
    });

    // contact groups (personal / organizational lists kept in the cartable settings): employees go to "To"; an external party pre-selects the outgoing party
    var groupBtn = $('lcGroupBtn'), groupMenu = $('lcGroupMenu'), groupsLoaded = false;
    function loadGroups() {
        if (groupsLoaded || !boot.urls.groups) return;
        groupsLoaded = true;
        text(groupMenu, '');
        fetch(boot.urls.groups, { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.json() : []; }).then(function (groups) {
            if (!groups.length) { var empty = document.createElement('li'); var s = document.createElement('span'); s.className = 'dropdown-item-text small text-muted'; s.textContent = T.noGroups; empty.appendChild(s); groupMenu.appendChild(empty); return; }
            groups.forEach(function (g) {
                var li = document.createElement('li'), b = document.createElement('button');
                b.type = 'button'; b.className = 'dropdown-item'; b.textContent = g.name + ' (' + g.members.length + ')';
                b.addEventListener('click', function () {
                    var added = 0;
                    g.members.forEach(function (m) {
                        if (m.employeeId) { if (addRecipient('to', { id: m.employeeId, name: m.name, position: m.subtitle })) added++; }
                        else if (m.correspondentPartyId && $('lcParty')) {
                            var sel = $('lcParty'), opt = sel.querySelector('option[value="' + m.correspondentPartyId + '"]');
                            if (opt && !sel.value) { sel.value = String(m.correspondentPartyId); sel.dispatchEvent(new Event('change', { bubbles: true })); }
                        }
                    });
                    renderIssues([], [{ message: T.groupAdded.replace('{0}', added) }]);
                    updateRecipientSummary();
                });
                li.appendChild(b); groupMenu.appendChild(li);
            });
        }).catch(function () { groupsLoaded = false; });
    }
    if (groupBtn && groupMenu) groupBtn.parentNode.addEventListener('show.bs.dropdown', loadGroups);

    // ---------------------------------------------------------------- approval chain
    function renderChain() {
        var ol = $('lcChain'); ol.innerHTML = '';
        state.approvers.forEach(function (a, i) {
            var li = document.createElement('li');
            var name = document.createElement('span'); name.textContent = a.name; li.appendChild(name);
            function btn(label, icon, fn, disabled) {
                var b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-sm btn-outline-secondary';
                b.setAttribute('aria-label', label + ' ' + a.name); b.disabled = !!disabled;
                b.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i>'; b.addEventListener('click', fn); li.appendChild(b);
            }
            btn(T.moveUp, 'bi-arrow-up', function () { swap(i, i - 1); }, i === 0);
            btn(T.moveDown, 'bi-arrow-down', function () { swap(i, i + 1); }, i === state.approvers.length - 1);
            btn(T.remove, 'bi-x-lg', function () { state.approvers.splice(i, 1); renderChain(); markDirty(); });
            ol.appendChild(li);
        });
    }
    function swap(i, j) {
        if (j < 0 || j >= state.approvers.length) return;
        var t = state.approvers[i]; state.approvers[i] = state.approvers[j]; state.approvers[j] = t; renderChain(); markDirty();
    }
    $('lcApproverAdd').addEventListener('click', function () {
        var sel = $('lcApproverPick'); var id = parseInt(sel.value, 10);
        if (!id || state.approvers.some(function (a) { return a.id === id; })) return;
        state.approvers.push({ id: id, name: sel.options[sel.selectedIndex].text.split(' — ')[0] });
        sel.value = ''; renderChain(); markDirty();
    });
    renderChain();

    var submitText = $('lcSubmitText');
    if ($('lcApprReq')) $('lcApprReq').addEventListener('change', function () {
        var need = boot.isOutgoing || this.checked;
        $('lcChainWrap').hidden = !need;
        submitText.textContent = need ? (submitText.getAttribute('data-approval') || submitText.textContent) : (submitText.getAttribute('data-send') || submitText.textContent);
        markDirty();
    });

    // ---------------------------------------------------------------- response due
    $('lcRespReq').addEventListener('change', function () {
        $('lcRespWrap').hidden = !this.checked;
        if (!this.checked) $('lcRespDue').value = '';
        markDirty();
    });
    $('lcDate').addEventListener('change', function () { $('lcRespDue').min = this.value || ''; });
    $('lcRespDue').min = val('lcDate');

    // ---------------------------------------------------------------- subject counter
    var subj = $('lcSubject'), subjLen = $('lcSubjectLen');
    function updateSubject() {
        if (!subjLen) return;   // a referral has a fixed subject and shows no counter
        var n = subj.value.trim().length; subjLen.textContent = n;
        subjLen.parentNode.classList.toggle('over', n > boot.maxSubject);
    }
    subj.addEventListener('input', updateSubject); updateSubject();

    // ---------------------------------------------------------------- dirty tracking + auto-save
    function markDirty() {
        state.dirty = true;
        if (!state.saving) setSaveState('dirty', T.unsaved);
    }
    ['lcSubject', 'lcCategory', 'lcPriority', 'lcConf', 'lcDate', 'lcRef', 'lcRelDept', 'lcProject', 'lcDiscipline', 'lcManual', 'lcExternal', 'lcParty', 'lcRespDue']
        .forEach(function (id) { var el = $(id); if (el) { el.addEventListener('input', markDirty); el.addEventListener('change', markDirty); } });
    $('composeBody').addEventListener('rte-change', markDirty);

    // choosing a registered organisation fills the receiver name (still editable); a name typed by hand is never overwritten
    var partySel = $('lcParty'), extInput = $('lcExternal');
    if (partySel && extInput) {
        partySel.addEventListener('change', function () {
            var opt = partySel.options[partySel.selectedIndex];
            if (partySel.value && (!extInput.value.trim() || extInput.dataset.fromParty === '1')) {
                extInput.value = opt.text; extInput.dataset.fromParty = '1'; markDirty();
            }
        });
        extInput.addEventListener('input', function () { extInput.dataset.fromParty = ''; });
    }

    function applyResult(res, autoSave) {
        if (res.rowVersion) state.rowVersion = res.rowVersion;
        if (res.letterId) {
            var wasNew = !state.letterId;
            state.letterId = res.letterId;
            if (wasNew && window.history && history.replaceState) history.replaceState(null, '', boot.urls.compose + '?id=' + res.letterId);
            $('lcAttNeedSave').hidden = true;
        }
        if (res.letterNumber) text($('lcNumber'), res.letterNumber);
    }

    // autoSave=true: بی‌صدا (فقط هشدارها)؛ ذخیره‌ی دستی خطاها را کامل نشان می‌دهد
    function save(autoSave) {
        if (state.saving) { state.queued = true; return Promise.resolve(false); }
        if (autoSave && !state.letterId && !val('lcSubject').trim()) return Promise.resolve(false);   // شماره‌ی نامه تا وقتی موضوعی نیست مصرف نمی‌شود

        state.saving = true; setSaveState('saving', T.saving);
        var payload = { input: buildInput(), autoSave: !!autoSave, rowVersionBase64: state.rowVersion };
        return postJson(boot.urls.save, payload)
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (res) {
                if (res.success) {
                    applyResult(res, autoSave);
                    state.dirty = false;
                    setSaveState('saved', T.saved + ' ' + res.savedAt);
                    if (!autoSave || (res.warnings && res.warnings.length)) renderIssues([], res.warnings);
                    return true;
                }
                setSaveState('error', T.saveFailed);
                renderIssues(res.errors, res.warnings);
                return false;
            })
            .catch(function () { setSaveState('error', T.saveFailed); return false; })
            .then(function (ok) {
                state.saving = false;
                if (state.queued) { state.queued = false; if (state.dirty) save(true); }
                return ok;
            });
    }

    $('lcSaveBtn').addEventListener('click', function () { save(false); });
    setInterval(function () { if (state.dirty && !state.submitting) save(true); }, boot.autoSaveSeconds * 1000);
    window.addEventListener('beforeunload', function (e) { if (state.dirty && !state.submitting) { e.preventDefault(); e.returnValue = ''; } });

    // ---------------------------------------------------------------- attachments
    var drop = $('lcDrop'), fileInput = $('lcFile'), fileList = $('lcFiles');

    function humanOk(file) {
        var ext = '.' + file.name.split('.').pop().toLowerCase();
        if (file.size > boot.maxAttachmentBytes) return T.fileTooLarge;
        if (boot.allowedExtensions.indexOf(ext) < 0) return T.fileType;
        return null;
    }

    function addFileRow(info) {
        var li = document.createElement('li'); li.setAttribute('data-id', info.id);
        li.innerHTML = '<i class="bi bi-file-earmark" aria-hidden="true"></i>';
        var n = document.createElement('span'); n.textContent = info.name; li.appendChild(n);
        var s = document.createElement('small'); s.className = 'text-muted'; s.textContent = Math.max(1, Math.round(info.size / 1024)) + ' KB'; li.appendChild(s);
        var b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-sm btn-outline-danger';
        b.setAttribute('aria-label', T.remove + ' ' + info.name); b.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        b.addEventListener('click', function () { removeAttachment(info.id, li); });
        li.appendChild(b); fileList.appendChild(li);
    }

    function removeAttachment(id, li) {
        var fd = new FormData(); fd.append('letterId', state.letterId); fd.append('attachmentId', id); fd.append('__RequestVerificationToken', token());
        fetch(boot.urls.removeAttachment, { method: 'POST', body: fd, credentials: 'same-origin' })
            .then(function (r) { if (r.ok) li.remove(); });
    }

    fileList.querySelectorAll('[data-remove-attachment]').forEach(function (b) {
        b.addEventListener('click', function () { removeAttachment(b.getAttribute('data-remove-attachment'), b.closest('li')); });
    });

    function uploadOne(file) {
        var problem = humanOk(file);
        if (problem) { renderIssues([{ message: file.name + ': ' + problem }], []); return Promise.resolve(); }
        var li = document.createElement('li'); li.innerHTML = '<span></span><progress max="100" value="0"></progress>';
        li.firstChild.textContent = file.name; fileList.appendChild(li);
        return new Promise(function (resolve) {
            var xhr = new XMLHttpRequest(); var fd = new FormData();
            fd.append('letterId', state.letterId); fd.append('file', file); fd.append('__RequestVerificationToken', token());
            xhr.open('POST', boot.urls.upload);
            xhr.upload.onprogress = function (e) { if (e.lengthComputable) li.querySelector('progress').value = Math.round(e.loaded / e.total * 100); };
            xhr.onload = function () {
                li.remove();
                if (xhr.status >= 200 && xhr.status < 300) addFileRow(JSON.parse(xhr.responseText));
                else { var msg = T.uploadFailed; try { msg = JSON.parse(xhr.responseText).message || msg; } catch (e) { } renderIssues([{ message: file.name + ': ' + msg }], []); }
                resolve();
            };
            xhr.onerror = function () { li.remove(); renderIssues([{ message: file.name + ': ' + T.uploadFailed }], []); resolve(); };
            xhr.send(fd);
        });
    }

    function handleFiles(files) {
        // پیوست به نامه‌ی ذخیره‌شده وصل می‌شود؛ برای نامه‌ی جدید اول پیش‌نویس ساخته می‌شود
        var ensure = state.letterId ? Promise.resolve(true) : save(false);
        ensure.then(function (ok) {
            if (!ok || !state.letterId) return;
            var chain = Promise.resolve();
            Array.prototype.forEach.call(files, function (f) { chain = chain.then(function () { return uploadOne(f); }); });
        });
    }

    drop.addEventListener('click', function () { fileInput.click(); });
    drop.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); fileInput.click(); } });
    fileInput.addEventListener('change', function () { handleFiles(this.files); this.value = ''; });
    ['dragenter', 'dragover'].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); drop.classList.add('over'); }); });
    ['dragleave', 'drop'].forEach(function (ev) { drop.addEventListener(ev, function (e) { e.preventDefault(); drop.classList.remove('over'); }); });
    drop.addEventListener('drop', function (e) { if (e.dataTransfer && e.dataTransfer.files.length) handleFiles(e.dataTransfer.files); });

    // ---------------------------------------------------------------- preview
    $('lcPreviewBtn').addEventListener('click', function () {
        var body = $('lcPreviewBody'); body.textContent = T.loading;
        var modalEl = $('lcPreviewModal');
        if (window.bootstrap) window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
        postJson(boot.urls.preview, buildInput())
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(function (html) { body.innerHTML = html; })
            .catch(function () { body.textContent = T.previewFailed; });
    });

    // ---------------------------------------------------------------- submit
    $('lcSubmitBtn').addEventListener('click', function () {
        var input = buildInput();
        var needApproval = input.approvalRequired;
        if (!window.confirm(needApproval ? T.confirmSubmit : T.confirmSend)) return;

        var btn = this; btn.disabled = true; state.submitting = true; setSaveState('saving', T.saving);
        postJson(boot.urls.submit, { input: input, autoSave: false, rowVersionBase64: state.rowVersion })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(function (res) {
                if (res.success && res.redirect) { state.dirty = false; window.location.href = res.redirect; return; }
                if (res.rowVersion) state.rowVersion = res.rowVersion;
                if (res.letterId) applyResult(res, false);
                state.submitting = false; btn.disabled = false;
                setSaveState('error', T.saveFailed);
                renderIssues(res.errors, res.warnings);
            })
            .catch(function () { state.submitting = false; btn.disabled = false; setSaveState('error', T.saveFailed); });
    });

    // ---------------------------------------------------------------- discard
    var discardForm = $('lcDiscardForm');
    if (discardForm) discardForm.addEventListener('submit', function (e) {
        if (!window.confirm(T.confirmDiscard)) { e.preventDefault(); return; }
        state.dirty = false;
    });

    updateRecipientSummary();
    if (state.letterId) setSaveState('idle', T.saved);
})();
