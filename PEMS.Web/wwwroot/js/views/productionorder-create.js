function fillUom(select) {
    var opt = select.options[select.selectedIndex];
    var uomId = opt.getAttribute('data-uom');
    if (uomId) document.getElementById('uomSelect').value = uomId;
}
