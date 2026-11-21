// links and forms that leave this page act on the whole window, not inside the small pane
document.addEventListener('submit', function (e) { var f = e.target; if (f && f.tagName === 'FORM' && !f.target) f.target = '_top'; }, true);
document.addEventListener('click', function (e) {
    var a = e.target.closest ? e.target.closest('a[href]') : null;
    if (!a || a.target) return;
    var href = a.getAttribute('href') || '';
    if (href.charAt(0) === '#' || href.indexOf('javascript:') === 0 || a.hasAttribute('data-bs-toggle')) return;
    a.target = '_top';
}, true);
