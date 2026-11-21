// Searchable multi-select (SCRUM-407): enhances <select multiple data-pems-multiselect> with the vendored Tom Select
// (wwwroot/lib/tom-select). The markup stays a plain multi-select, so the form still posts without JavaScript.
// Optional attributes: data-placeholder, data-no-results (localised texts from the view).
(function () {
    'use strict';

    function init(select) {
        if (select.tomselect || typeof window.TomSelect !== 'function') return;

        var noResults = select.getAttribute('data-no-results') || '';
        new window.TomSelect(select, {
            plugins: {
                remove_button: { title: select.getAttribute('data-remove-title') || '' },
                checkbox_options: {}
            },
            placeholder: select.getAttribute('data-placeholder') || '',
            hidePlaceholder: false,
            closeAfterSelect: false,
            maxOptions: null,
            // the list is attached to <body> so a form card with overflow:hidden cannot clip it
            dropdownParent: 'body',
            // option groups keep the order of the markup; search covers value and label
            searchField: ['text', 'value'],
            render: {
                no_results: function () {
                    return window.PemsTemplate.fill(window.PemsTemplate.clone('sel-no-results'), { text: noResults });
                }
            }
        });
    }

    function boot() {
        document.querySelectorAll('select[multiple][data-pems-multiselect]').forEach(init);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
