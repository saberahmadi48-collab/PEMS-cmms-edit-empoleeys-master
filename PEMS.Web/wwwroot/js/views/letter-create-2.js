(function () {
    var cb = document.getElementById('responseRequired');
    var wrap = document.getElementById('responseDueWrap');
    var due = document.getElementById('responseDueDate');
    var letterDate = document.getElementById('letterDate');
    function sync() {
        wrap.hidden = !cb.checked;
        due.required = cb.checked;
        due.min = letterDate.value || '';
        if (!cb.checked) due.value = '';
    }
    cb.addEventListener('change', sync);
    letterDate.addEventListener('change', sync);
    sync();
})();
