// Summary chips jump to the matching tab (Bootstrap tabs only activate from inside a .nav/tablist, so drive the real tab button)
document.querySelectorAll('[data-goto-tab]').forEach(function (chip) {
    chip.addEventListener('click', function () {
        var btn = document.querySelector('#equipmentTabs [data-bs-target="' + chip.getAttribute('data-goto-tab') + '"]');
        if (btn) bootstrap.Tab.getOrCreateInstance(btn).show();
    });
});
