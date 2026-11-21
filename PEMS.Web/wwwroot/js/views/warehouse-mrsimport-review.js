(function () {
    // طبق ممیزی «Global 500-Row Batch Limitation Remediation»: فیلتر Match Status حالا سمت سرور
    // است (لینک‌های ?matchStatusFilter= بالا) و روی کل Batch اعمال می‌شود، نه فقط ردیف‌های همین
    // صفحه - این جستجو فقط داخل توضیح ردیف‌های همین صفحه‌ی رندرشده فیلتر می‌کند.
    var searchInput = document.getElementById('rowSearch');
    var rows = [].slice.call(document.querySelectorAll('.row-item'));

    function applyFilters() {
        var q = (searchInput.value || '').trim().toLowerCase();
        rows.forEach(function (r) {
            var descOk = !q || r.getAttribute('data-desc').indexOf(q) !== -1;
            r.style.display = descOk ? '' : 'none';
        });
    }

    searchInput.addEventListener('input', applyFilters);
})();

(function () {
    var selectAll = document.getElementById('selectAllPending');
    var checkboxes = [].slice.call(document.querySelectorAll('.row-select'));
    var rejectCheckboxes = [].slice.call(document.querySelectorAll('.row-select-reject'));
    var commitBtn = document.getElementById('bulkCommitBtn');
    var rejectBtn = document.getElementById('bulkRejectBtn');
    var countSpan = document.getElementById('selectedCount');

    function sync(i) { rejectCheckboxes[i].checked = checkboxes[i].checked; }

    function updateCount() {
        var checked = checkboxes.filter(function (c) { return c.checked; }).length;
        countSpan.textContent = checked;
        commitBtn.disabled = checked === 0;
        rejectBtn.disabled = checked === 0;
    }

    selectAll.addEventListener('change', function () {
        checkboxes.forEach(function (c, i) {
            var row = c.closest('tr');
            if (row.style.display !== 'none') { c.checked = selectAll.checked; sync(i); }
        });
        updateCount();
    });

    checkboxes.forEach(function (c, i) { c.addEventListener('change', function () { sync(i); updateCount(); }); });
    updateCount();
})();

(function () {
    [].slice.call(document.querySelectorAll('.meta-toggle')).forEach(function (btn) {
        btn.addEventListener('click', function () {
            var target = document.getElementById(btn.getAttribute('data-target'));
            if (target) target.classList.toggle('d-none');
        });
    });
})();
