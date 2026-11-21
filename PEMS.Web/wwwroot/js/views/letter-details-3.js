document.addEventListener('click', function (e) {
    var b = e.target.closest ? e.target.closest('[data-cv-toggle]') : null;
    if (!b) return;
    var open = b.getAttribute('data-cv-toggle') === 'open';
    document.querySelectorAll('#conversationPanel details').forEach(function (d) { d.open = open; });
});
