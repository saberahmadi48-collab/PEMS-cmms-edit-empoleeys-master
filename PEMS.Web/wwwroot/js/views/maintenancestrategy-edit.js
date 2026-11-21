        (function () {
            var classSel = document.getElementById('classSelect');
            var modeSel = document.getElementById('modeSelect');
            var causeSel = document.getElementById('causeSelect');
            var search = document.getElementById('modeSearch');
            var typeSel = document.getElementById('typeSelect');
            var consSel = document.getElementById('consequenceSelect');
            var hiddenChk = document.getElementById('hiddenChk');
            var cbm = document.getElementById('cbmSection');
            var rtfWarn = document.getElementById('rtfWarning');
            var isFa = document.documentElement.lang === 'fa' || document.dir === 'rtl';
            var selectedMode = modeSel.dataset.selected || '';
            var selectedCause = causeSel.dataset.selected || '';
            var modes = [];

            function label(m) { return (isFa && m.nameFa ? m.nameFa : m.name) + ' (' + m.code + ')'; }

            function renderModes() {
                var keep = modeSel.value || selectedMode;
                PemsTemplate.clear(modeSel);
                modes.forEach(function (m) {
                    var o = document.createElement('option');
                    o.value = m.failureModeId; o.textContent = label(m);
                    if (String(m.failureModeId) === String(keep)) o.selected = true;
                    modeSel.appendChild(o);
                });
                renderCauses();
            }

            function renderCauses() {
                var m = modes.find(function (x) { return String(x.failureModeId) === String(modeSel.value); });
                PemsTemplate.options(causeSel, [{ value: '', text: '—' }]);
                (m ? m.causes : []).forEach(function (c) {
                    var o = document.createElement('option');
                    o.value = c.failureCauseId; o.textContent = (isFa && c.nameFa ? c.nameFa : c.name);
                    if (String(c.failureCauseId) === String(selectedCause)) o.selected = true;
                    causeSel.appendChild(o);
                });
            }

            var timer = null;
            function loadModes() {
                clearTimeout(timer);
                timer = setTimeout(function () {
                    var url = modeSel.dataset.url + '?classId=' + encodeURIComponent(classSel.value || '') + '&q=' + encodeURIComponent(search.value || '');
                    fetch(url).then(function (r) { return r.ok ? r.json() : []; }).then(function (data) { modes = data; renderModes(); });
                }, 200);
            }

            function syncRules() {
                var c = consSel.value, t = typeSel.value;
                var needsProactive = c === 'Safety' || c === 'Environmental' || c === 'Hidden' || hiddenChk.checked;
                rtfWarn.hidden = !((needsProactive && (t === 'RunToFailure' || t === 'Corrective')));
                cbm.hidden = !((t === 'CBM' || t === 'Predictive'));
            }

            classSel.addEventListener('change', function () { selectedMode = ''; loadModes(); });
            search.addEventListener('input', loadModes);
            modeSel.addEventListener('change', function () { selectedCause = ''; renderCauses(); });
            [consSel, typeSel, hiddenChk].forEach(function (el) { el.addEventListener('change', syncRules); });

            loadModes();
            syncRules();
        })();
