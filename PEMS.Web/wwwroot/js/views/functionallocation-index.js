document.querySelectorAll('.edit-fl-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
        document.getElementById('editFlId').value = btn.dataset.id;
        document.getElementById('editFlCode').value = btn.dataset.code;
        document.getElementById('editFlName').value = btn.dataset.name;
        document.getElementById('editFlParentId').value = btn.dataset.parentid || '';
        document.getElementById('editFlPlant').value = btn.dataset.plant || '';
        document.getElementById('editFlArea').value = btn.dataset.area || '';
        document.getElementById('editFlUnit').value = btn.dataset.unit || '';
        new bootstrap.Modal(document.getElementById('editFlModal')).show();
    });
});
