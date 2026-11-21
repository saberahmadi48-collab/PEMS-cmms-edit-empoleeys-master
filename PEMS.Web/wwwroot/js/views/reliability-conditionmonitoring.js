document.getElementById('raiseFindingModal')?.addEventListener('show.bs.modal', function (event) {
    var button = event.relatedTarget;
    document.getElementById('raiseFindingReadingId').value = button.getAttribute('data-reading-id');
    document.getElementById('raiseFindingPointLabel').textContent = button.getAttribute('data-point-label');
});
