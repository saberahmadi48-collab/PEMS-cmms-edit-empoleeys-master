(function () {
    var searchInput = document.getElementById('rowSearch');
    var statusButtons = [].slice.call(document.querySelectorAll('.status-filter'));
    var rows = [].slice.call(document.querySelectorAll('.row-item'));
    var activeStatus = 'all';

    function applyFilters() {
        var q = (searchInput.value || '').trim().toLowerCase();
        rows.forEach(function (r) {
            var statusOk = activeStatus === 'all' || r.getAttribute('data-status') === activeStatus;
            var tagOk = !q || r.getAttribute('data-tag').indexOf(q) !== -1;
            r.style.display = (statusOk && tagOk) ? '' : 'none';
        });
    }

    searchInput.addEventListener('input', applyFilters);
    statusButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            statusButtons.forEach(function (b) { b.classList.remove('active'); });
            btn.classList.add('active');
            activeStatus = btn.getAttribute('data-status');
            applyFilters();
        });
    });
})();

(function () {
    var selectAll = document.getElementById('selectAllMatched');
    var checkboxes = [].slice.call(document.querySelectorAll('.row-select'));
    var confirmBtn = document.getElementById('bulkConfirmBtn');
    var countSpan = document.getElementById('selectedCount');

    function updateCount() {
        var checked = checkboxes.filter(function (c) { return c.checked; }).length;
        countSpan.textContent = checked;
        confirmBtn.disabled = checked === 0;
    }

    selectAll.addEventListener('change', function () {
        // فقط ردیف‌های در حال حاضر قابل‌مشاهده (بعد از فیلتر) انتخاب/لغو می‌شوند
        checkboxes.forEach(function (c) {
            var row = c.closest('tr');
            if (row.style.display !== 'none') c.checked = selectAll.checked;
        });
        updateCount();
    });

    checkboxes.forEach(function (c) { c.addEventListener('change', updateCount); });
    updateCount();
})();
