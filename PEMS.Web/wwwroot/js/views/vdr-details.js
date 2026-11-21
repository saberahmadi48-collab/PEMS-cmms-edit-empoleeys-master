        (function () {
            var form = document.getElementById('vdrStartForm');
            if (!form) return;

            // دکمه‌ی شروع تا ورود شماره‌ی ترنسمیتال غیرفعال است و دلیلش دیده می‌شود (بدون JS دکمه فعال می‌ماند و سرور خودش اعتبارسنجی می‌کند)
            var input = document.getElementById('vdrTransmittal');
            var button = document.getElementById('vdrStartBtn');
            var why = document.getElementById('vdrWhyDisabled');
            function syncStart() {
                var ok = input.value.trim().length > 0;
                button.disabled = !ok;
                why.hidden = ok;
            }
            input.addEventListener('input', syncStart);
            syncStart();

            var boxes = Array.prototype.slice.call(form.querySelectorAll('input[name="disciplineIds"]'));
            var summary = document.getElementById('vdrDiscSummary');
            var side = document.getElementById('vdrSummaryDisc');
            // SCRUM-560: the side value's own first text is its "none" text; a missing element is skipped, never a script error
            var sideNone = side ? (side.getAttribute('data-none') || side.textContent.trim()) : '';
            function syncDisciplines() {
                var names = boxes.filter(function (b) { return b.checked; }).map(function (b) { return b.getAttribute('data-name'); });
                if (summary) {
                    summary.textContent = names.length
                        ? summary.getAttribute('data-some').replace('{0}', names.length).replace('{1}', names.join(', '))
                        : summary.getAttribute('data-none');
                }
                if (side) side.textContent = names.length ? names.join(', ') : sideNone;
            }
            boxes.forEach(function (b) { b.addEventListener('change', syncDisciplines); });
            syncDisciplines();
        })();
