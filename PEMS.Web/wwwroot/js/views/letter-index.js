document.addEventListener('DOMContentLoaded', function () {
    PemsDataGrid.initColumnResize('Letter');
});
document.getElementById('lpSearchInput').addEventListener('pems:search-select', function (e) {
    if (e.detail && e.detail.url) window.location.href = e.detail.url;
});
