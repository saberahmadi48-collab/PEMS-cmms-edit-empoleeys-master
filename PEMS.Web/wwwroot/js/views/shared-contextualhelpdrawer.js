    (function () {
        var drawer = document.getElementById('pemsHelpDrawer');
        if (!drawer) return;

        var loadedForKey = null;

        drawer.addEventListener('show.bs.offcanvas', function (evt) {
            var trigger = evt.relatedTarget;
            var helpKey = trigger ? trigger.getAttribute('data-help-key') : null;
            if (!helpKey || helpKey === loadedForKey) return;

            var body = document.getElementById('pemsHelpDrawerBody');
            PemsSkeleton.show(body, 'card');

            fetch(drawer.dataset.contextUrl + '?key=' + encodeURIComponent(helpKey))
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    loadedForKey = helpKey;
                    if (!data.found) {
                        var empty = PemsTemplate.fragment('help-nocontent');
                        PemsTemplate.fill(empty.firstElementChild, { text: drawer.dataset.textNoContent });
                        PemsTemplate.fill(empty.lastElementChild, { link: { text: drawer.dataset.textBrowse, attrs: { href: drawer.dataset.helpIndexUrl } } });
                        body.replaceChildren(empty);
                        return;
                    }
                    // the title, summary and content come from the Help service as finished HTML: parsed inertly, never concatenated
                    var article = PemsTemplate.fragment('help-article');
                    var root = article;
                    PemsTemplate.html(root.querySelector('[data-slot="title"]'), data.title);
                    PemsTemplate.html(root.querySelector('[data-slot="summary"]'), data.summary);
                    PemsTemplate.html(root.querySelector('[data-slot="content"]'), data.content);
                    PemsTemplate.fill(root, {
                        diagram: data.hasVisual ? { attrs: { href: data.articleUrl } } : null,
                        diagramText: drawer.dataset.textDiagram,
                        open: { attrs: { href: data.articleUrl } },
                        openText: drawer.dataset.textOpen,
                        relatedLabel: drawer.dataset.textRelated
                    });
                    var relatedBox = root.querySelector('[data-slot="relatedBox"]');
                    var relatedList = (data.relatedArticles || []);
                    if (relatedList.length === 0) {
                        if (relatedBox) relatedBox.remove();
                    } else {
                        var host = root.querySelector('[data-slot="related"]');
                        relatedList.forEach(function (r) {
                            var link = PemsTemplate.clone('help-related-link');
                            PemsTemplate.html(link, r.title);
                            link.setAttribute('href', '/Help/Article/' + r.code);
                            host.appendChild(link);
                        });
                    }
                    body.replaceChildren.apply(body, Array.prototype.slice.call(root.childNodes));
                })
                .catch(function () {
                    body.replaceChildren(PemsTemplate.fill(PemsTemplate.clone('help-load-error'), { text: drawer.dataset.textError }));
                });
        });
    })();
