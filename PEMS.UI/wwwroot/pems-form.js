// PEMS.UI form shell (SCRUM-352, SCRUM-488). The one leave guard of the product: the browser is asked to confirm leaving the page while any
// registered guard reports unsaved work. The browser shows its own standard text, so the library holds no message.
//  - A form with data-pems-dirty-guard is guarded once a value was changed; submitting or resetting it clears that.
//  - A page whose state is not a plain form (letter editor, register screen) calls PemsForm.guard(function () { return unsavedAndNotSubmitting; }).
(function () {
    'use strict';

    var guards = [];
    var listening = false;

    function guard(isDirty) {
        guards.push(isDirty);
        if (listening) return;
        listening = true;
        window.addEventListener('beforeunload', function (e) {
            for (var i = 0; i < guards.length; i++) {
                if (guards[i]()) { e.preventDefault(); e.returnValue = ''; return; }
            }
        });
    }

    function init(scope) {
        (scope || document).querySelectorAll('form[data-pems-dirty-guard]').forEach(function (form) {
            if (form.dataset.pemsInit) return;
            form.dataset.pemsInit = '1';
            var dirty = false;
            form.addEventListener('input', function () { dirty = true; });
            form.addEventListener('change', function () { dirty = true; });
            form.addEventListener('submit', function () { dirty = false; });
            form.addEventListener('reset', function () { dirty = false; });
            guard(function () { return dirty; });
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { init(); });
    else init();
    window.PemsForm = { init: init, guard: guard };
})();
