// picking an equipment from the suggestions fills the hidden filter field and applies the filters
(function () {
    var input = document.getElementById('strategyFilterSearch');
    input.addEventListener('pems:search-select', function (e) {
        if (e.detail) { document.getElementById('strategyFilterEquipmentId').value = e.detail.id; input.form.submit(); }
    });
})();
