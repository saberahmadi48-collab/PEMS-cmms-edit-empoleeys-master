// همان رفتارهای سایدبار قدیمی: چرخش Chevron، باز/بسته با Enter/Space، Tooltip برای حالت Icon-Rail
document.querySelectorAll('.pems-sidebar .nav-section-title').forEach(function (title) {
    var target = document.querySelector(title.getAttribute('data-bs-target'));
    var chevron = title.querySelector('.nav-section-chevron');
    if (target && chevron) {
        target.addEventListener('show.bs.collapse', function () { chevron.classList.add('rotated'); });
        target.addEventListener('hide.bs.collapse', function () { chevron.classList.remove('rotated'); });
    }
    title.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); title.click(); }
    });
});
document.querySelectorAll('.pems-sidebar .nav-link, .pems-sidebar .nav-section-title').forEach(function (el) {
    if (!el.hasAttribute('title')) {
        var label = el.querySelector('.nav-label, span');
        var text = (label ? label.textContent : el.textContent).trim();
        if (text) el.setAttribute('title', text);
    }
});
