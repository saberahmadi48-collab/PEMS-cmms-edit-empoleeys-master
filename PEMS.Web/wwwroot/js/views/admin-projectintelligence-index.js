(function () {
    var form = document.getElementById('refreshForm');
    var btn = document.getElementById('refreshBtn');
    form && form.addEventListener('submit', function () {
        btn.disabled = true;
        var busy = PemsTemplate.fragment('scan-busy');
        busy.querySelector('[data-slot="text"]').textContent = 'Scanning...';
        btn.replaceChildren(busy);
    });
})();
(function () {
    var input = document.getElementById('moduleSearch');
    var cards = [].slice.call(document.querySelectorAll('.module-card'));
    input && input.addEventListener('input', function () {
        var q = this.value.trim().toLowerCase();
        cards.forEach(function (c) {
            var show = !q || c.getAttribute('data-module').indexOf(q) !== -1;
            c.style.display = show ? '' : 'none';
        });
    });
})();
