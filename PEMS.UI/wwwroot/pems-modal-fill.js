// PEMS.UI shared modal for many rows (SCRUM-500). One Bootstrap modal per list instead of one per row: the button that opens it carries the row's values.
//   <button data-bs-toggle="modal" data-bs-target="#replyModal" data-fill-commentid="12" data-fill-commentno="C-7" data-fill-commenttext="...">
//   inside the modal:  <input name="commentId">  gets 12 (form fields are matched by name, case-insensitive; checkboxes and radios by value),
//                      <span data-fill-text="commentno"> gets the text "C-7" (textContent, never HTML).
//   Two keys are not fields: data-fill-formaction sets the action of the modal's form, data-fill-accept the accept list of its file input.
// The form of the modal is reset before every opening, so a value of the previous row never stays. Vanilla, offline, no inline handlers.
(function () {
    'use strict';

    function fields(modal) {
        return Array.prototype.slice.call(modal.querySelectorAll('input[name], select[name], textarea[name]'));
    }

    function set(field, value) {
        if (field.type === 'checkbox') field.checked = /^(true|1|on|yes)$/i.test(value);
        else if (field.type === 'radio') field.checked = field.value === value;
        else if (field.type !== 'file') field.value = value;
    }

    document.addEventListener('show.bs.modal', function (e) {
        var modal = e.target;
        var opener = e.relatedTarget;
        if (!modal || !opener || !opener.attributes) return;
        var values = {};
        var any = false;
        Array.prototype.forEach.call(opener.attributes, function (a) {
            if (a.name.indexOf('data-fill-') === 0) { values[a.name.slice(10)] = a.value; any = true; }
        });
        if (!any) return;
        var form = modal.querySelector('form');
        if (form) form.reset();
        if (form && Object.prototype.hasOwnProperty.call(values, 'formaction')) form.setAttribute('action', values.formaction);
        var file = modal.querySelector('input[type=file]');
        if (file && Object.prototype.hasOwnProperty.call(values, 'accept')) file.setAttribute('accept', values.accept);
        fields(modal).forEach(function (field) {
            var key = (field.getAttribute('name') || '').toLowerCase();
            if (Object.prototype.hasOwnProperty.call(values, key)) set(field, values[key]);
        });
        Array.prototype.forEach.call(modal.querySelectorAll('[data-fill-text]'), function (node) {
            var key = (node.getAttribute('data-fill-text') || '').toLowerCase();
            node.textContent = Object.prototype.hasOwnProperty.call(values, key) ? values[key] : '';
        });
    });
})();
