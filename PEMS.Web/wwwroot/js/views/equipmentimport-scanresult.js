document.getElementById('selectAllTags')?.addEventListener('click', function () {
    document.querySelectorAll('.scan-tag-checkbox:not(:disabled)').forEach(function (cb) { cb.checked = true; });
});
document.getElementById('deselectAllTags')?.addEventListener('click', function () {
    document.querySelectorAll('.scan-tag-checkbox').forEach(function (cb) { cb.checked = false; });
});
