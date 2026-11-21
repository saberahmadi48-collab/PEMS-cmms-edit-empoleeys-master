// picking a suggestion opens that equipment's passport; Enter filters the list
document.getElementById('equipmentQuickSearch').addEventListener('pems:search-select', function (e) {
    if (e.detail && e.detail.url) window.location.href = e.detail.url;
});
