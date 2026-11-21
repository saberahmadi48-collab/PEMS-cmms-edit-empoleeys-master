document.getElementById('permPicker').addEventListener('change', function (e) {
    var v = e.target.value || '';
    var dot = v.indexOf('.');
    if (dot > 0) {
        document.querySelector('[name="ResourceName"]').value = v.substring(0, dot);
        document.querySelector('[name="ActionName"]').value = v.substring(dot + 1);
    }
});
