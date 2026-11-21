    // طبق «PEMS — Warehouse and Inventory Barcode Integration»: اسکن فقط فیلد موجود را از
    // قبل پر می‌کند - فرم همان مسیر/اعتبارسنجی/Transaction موجود را طی می‌کند، بدون هیچ تغییر.
    async function pemsResolveBarcodeInto(inputId, selectId, expectedTargetType) {
        const input = document.getElementById(inputId);
        const select = document.getElementById(selectId);
        const code = (input.value || '').trim();
        if (!code) return;

        try {
            const res = await fetch('/x/barcode-resolve-json?code=' + encodeURIComponent(code));
            const data = await res.json();

            if (!data.success) {
                window.PemsDialog.alert('Barcode not resolved: ' + (data.errorMessage || data.errorCode));
                return;
            }
            if (data.targetType !== expectedTargetType) {
                window.PemsDialog.alert('This barcode belongs to a ' + data.targetType + ', not a ' + expectedTargetType + '.');
                return;
            }

            var matched = false;
            for (var i = 0; i < select.options.length; i++) {
                if (select.options[i].value == data.targetId) {
                    select.value = select.options[i].value;
                    matched = true;
                    break;
                }
            }
            if (!matched) {
                window.PemsDialog.alert('Resolved to ' + (data.label || ('Material #' + data.targetId)) + ', but it is not in this list.');
                return;
            }

            input.value = '';
        } catch (e) {
            window.PemsDialog.alert('Could not resolve barcode.');
        }
    }

    document.getElementById('spareUsageScanBtn')?.addEventListener('click', function () {
        pemsResolveBarcodeInto('spareUsageScanInput', 'spareUsageMaterialSelect', 'MaterialItem');
    });
    document.getElementById('requiredMaterialScanBtn')?.addEventListener('click', function () {
        pemsResolveBarcodeInto('requiredMaterialScanInput', 'requiredMaterialSelect', 'MaterialItem');
    });
