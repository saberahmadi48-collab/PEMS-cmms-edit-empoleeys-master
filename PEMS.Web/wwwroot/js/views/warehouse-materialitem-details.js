document.querySelectorAll('.adjust-stock-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
        document.getElementById('adjustWarehouseId').value = btn.dataset.warehouseId;
        document.getElementById('adjustWarehouseLabel').textContent = '(' + btn.dataset.warehouseCode + ')';
        document.getElementById('adjustNewOnHand').value = btn.dataset.onhand;
        new bootstrap.Modal(document.getElementById('adjustStockModal')).show();
    });
});
document.querySelectorAll('.transfer-stock-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
        document.getElementById('transferFromWarehouseId').value = btn.dataset.warehouseId;
        document.getElementById('transferFromWarehouseLabel').textContent = '(' + btn.dataset.warehouseCode + ')';
        document.getElementById('transferAvailable').textContent = btn.dataset.available;
        new bootstrap.Modal(document.getElementById('transferStockModal')).show();
    });
});

// طبق «PEMS — Warehouse and Inventory Barcode Integration»: اسکن فقط <select name="warehouseId">
// موجود را از قبل پر می‌کند - ReceiveStock همون مسیر/سرویس موجود (IMaterialStockService) را طی می‌کند.
document.getElementById('receiveWarehouseScanBtn')?.addEventListener('click', async function () {
    var input = document.getElementById('receiveWarehouseScanInput');
    var select = document.getElementById('receiveWarehouseSelect');
    var code = (input.value || '').trim();
    if (!code) return;

    try {
        var res = await fetch('/x/barcode-resolve-json?code=' + encodeURIComponent(code));
        var data = await res.json();

        if (!data.success) { showLiveToast(input.dataset.resolveFailed, true); return; }
        if (data.targetType !== 'Warehouse') { showLiveToast(input.dataset.wrongTarget, true); return; }

        var matched = false;
        for (var i = 0; i < select.options.length; i++) {
            if (select.options[i].value == data.targetId) { select.value = select.options[i].value; matched = true; break; }
        }
        if (!matched) { showLiveToast(input.dataset.notInList, true); return; }

        input.value = '';
    } catch (e) {
        showLiveToast(input.dataset.resolveFailed, true);
    }
});
