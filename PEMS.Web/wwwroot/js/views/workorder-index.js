// picking an equipment from the suggestions fills the hidden filter field
document.getElementById('woEquipmentFilterSearch').addEventListener('pems:search-select', function (e) {
    if (e.detail) document.getElementById('woEquipmentFilterHidden').value = e.detail.id;
});
