document.getElementById('addLimitModal')?.addEventListener('show.bs.modal', function (event) {
    var button = event.relatedTarget;
    document.getElementById('addLimitProfileId').value = button.getAttribute('data-profile-id');
    document.getElementById('addLimitProfileLabel').textContent = button.getAttribute('data-profile-code');
});
