document.getElementById('overrideModal').addEventListener('show.bs.modal', function (e) {
    var b = e.relatedTarget;
    document.getElementById('overrideAssignmentId').value = b.getAttribute('data-assignment');
    document.getElementById('overrideTag').textContent = b.getAttribute('data-tag');
});
