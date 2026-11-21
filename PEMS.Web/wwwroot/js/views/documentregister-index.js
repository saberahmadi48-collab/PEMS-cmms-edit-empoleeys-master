// this page's own filter rule: the ticked disciplines travel as one comma-separated value; a saved filter stores the filter form
(function () {
    var hidden = document.getElementById('drDisciplines');
    var checks = document.querySelectorAll('[data-dr-discipline]');
    checks.forEach(function (c) {
        c.addEventListener('change', function () {
            hidden.value = Array.prototype.filter.call(checks, function (x) { return x.checked; })
                .map(function (x) { return x.value; }).join(',');
        });
    });
    var modal = document.getElementById('saveFilterModal');
    if (modal) modal.addEventListener('show.bs.modal', function () {
        var params = new URLSearchParams(new FormData(document.getElementById('drFilterForm')));
        Array.from(params.keys()).forEach(function (k) { if (!params.get(k)) params.delete(k); });
        document.getElementById('saveFilterQueryString').value = params.toString();
    });
})();
