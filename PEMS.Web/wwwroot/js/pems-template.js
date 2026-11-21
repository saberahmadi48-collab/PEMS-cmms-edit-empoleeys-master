// PemsTemplate: the one way a PEMS script gets markup (SCRUM-508 item 6, guideline 02 section 7, ADR-0013).
// No script writes a markup string. The server renders the markup once as a registered component (`script-templates`,
// `<template data-pems-template="name">`); a script clones it and fills the slots (`data-slot="x"`) with textContent and attributes.
// Markup that comes from the server as a finished fragment (a search highlight, a preview partial) goes in through html(), which parses it
// inertly (DOMParser: scripts in it never run) and moves the nodes, so a script still never concatenates a tag.
(function (root, factory) {
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PemsTemplate = factory();
})(typeof self !== "undefined" ? self : this, function () {
    "use strict";

    function find(name, doc) {
        var t = (doc || document).querySelector('template[data-pems-template="' + name + '"]');
        if (!t) throw new Error('PemsTemplate: the page has no <template data-pems-template="' + name + '">');
        return t;
    }

    // The whole template as a fresh fragment (use it when the template has several top-level nodes).
    function fragment(name, doc) { return find(name, doc).content.cloneNode(true); }

    // The single top-level element of the template, cloned.
    function clone(name, doc) {
        var f = fragment(name, doc);
        if (f.childElementCount !== 1) throw new Error('PemsTemplate: "' + name + '" must have exactly one top-level element, use fragment()');
        return f.firstElementChild;
    }

    // Fills the slots of a clone. A string or number sets textContent; null removes the slot node (an optional part);
    // an object sets { text, attrs: {name: value}, addClass } on the slot node. Slots the map does not name are left as the template wrote them.
    function fill(el, values) {
        var nodes = Array.prototype.slice.call(el.querySelectorAll("[data-slot]"));
        if (el.matches && el.matches("[data-slot]")) nodes.unshift(el);
        nodes.forEach(function (n) {
            var name = n.getAttribute("data-slot");
            if (!Object.prototype.hasOwnProperty.call(values, name)) return;
            var v = values[name];
            if (v === null || v === undefined) { if (n !== el) n.remove(); return; }
            if (typeof v === "object") {
                if (v.text !== undefined) n.textContent = String(v.text);
                if (v.attrs) Object.keys(v.attrs).forEach(function (a) { n.setAttribute(a, v.attrs[a]); });
                if (v.addClass) String(v.addClass).split(/\s+/).forEach(function (c) { if (c) n.classList.add(c); });
            } else {
                n.textContent = String(v);
            }
        });
        return el;
    }

    function clear(el) { el.replaceChildren(); return el; }

    // A <select>'s options from data: [{ value, text, selected }]. Options are created as DOM nodes, never as a string.
    function options(select, items) {
        var doc = select.ownerDocument;
        select.replaceChildren.apply(select, items.map(function (i) {
            return new Option(i.text == null ? "" : i.text, i.value == null ? "" : i.value, false, !!i.selected);
        }));
        return select;
    }

    // A finished fragment that the server wrote (and encoded): parsed inertly, its nodes replace the children of el.
    function html(el, markup) {
        var parsed = new DOMParser().parseFromString(String(markup == null ? "" : markup), "text/html");
        el.replaceChildren.apply(el, Array.prototype.slice.call(parsed.body.childNodes));
        return el;
    }

    return { fragment: fragment, clone: clone, fill: fill, clear: clear, options: options, html: html };
});
