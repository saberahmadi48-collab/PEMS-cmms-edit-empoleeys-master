/*
 * pems-definition-client.js (SCRUM-454): the one way a designer or the definition workbench calls the definition store API (/api/v1/definitions).
 * Offline, no framework. Keeps the ETag of the last read per resource and sends it as If-Match, sends the antiforgery token as X-CSRF-TOKEN, maps problem+json
 * to field messages, raises one standard "pems-definition-conflict" event on 412, autosaves a draft with a debounce, retries only safe reads and sends an
 * Idempotency-Key on creates.
 *
 *   const client = PemsDefinitions.create({ base: "/api/v1/definitions" });
 *   const draft  = await client.getDraft(12);                  // { ok, status, data, etag }
 *   const saved  = await client.saveDraft(12, payloadObject);  // 200 or { ok:false, problem, fieldErrors }
 *   const auto   = client.autosave(12, () => currentPayload(), { onState: s => badge.textContent = s });   // saved | unsaved | saving | conflict | error
 *   auto.touch();                                              // call on every edit
 *
 * Events (dispatched on document): "pems-definition-conflict" { id, resource } on 412; "pems-definition-unauthorized" { status } on 401 or 403.
 */
(function () {
    "use strict";

    function token() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        if (meta) return meta.getAttribute("content");
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    function newKey() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return "k" + Date.now() + "-" + Math.random().toString(16).slice(2);
    }

    /** problem+json { errors:[{pointer,code,message}] } to { "/payload/fields/0": ["message"] } and a summary line. */
    function mapProblem(problem) {
        var fieldErrors = {};
        var list = (problem && (problem.errors || problem.fieldErrors)) || [];
        for (var i = 0; i < list.length; i++) {
            var e = list[i];
            var p = e.pointer || "/";
            (fieldErrors[p] = fieldErrors[p] || []).push(e.message || e.code || "");
        }
        var summary = (problem && (problem.detail || problem.title)) || "";
        return { fieldErrors: fieldErrors, summary: summary };
    }

    function create(options) {
        var base = (options && options.base) || "/api/v1/definitions";
        var etags = {};   // resource path -> last ETag

        function emit(name, detail) { document.dispatchEvent(new CustomEvent(name, { detail: detail })); }

        async function call(method, path, body, opts) {
            opts = opts || {};
            var url = base + path;
            var headers = { "Accept": "application/json" };
            var csrf = token();
            if (csrf) headers["X-CSRF-TOKEN"] = csrf;
            if (body !== undefined) headers["Content-Type"] = "application/json";
            var etagKey = opts.etagKey || path;
            if (opts.ifMatch !== false && method !== "GET" && etags[etagKey]) headers["If-Match"] = etags[etagKey];
            if (opts.idempotent) headers["Idempotency-Key"] = newKey();

            var attempts = method === "GET" ? 3 : 1;   // only safe reads are retried
            var response = null;
            for (var n = 0; n < attempts; n++) {
                try {
                    response = await fetch(url, { method: method, headers: headers, credentials: "same-origin", body: body === undefined ? undefined : JSON.stringify(body) });
                    if (response.status < 500) break;
                } catch (err) {
                    if (n === attempts - 1) return { ok: false, status: 0, problem: null, fieldErrors: {}, summary: String(err) };
                }
            }
            var data = null;
            var text = await response.text();
            if (text) { try { data = JSON.parse(text); } catch (e) { data = null; } }
            var etag = response.headers.get("ETag");
            if (etag) etags[etagKey] = etag;
            if (response.ok) return { ok: true, status: response.status, data: data, etag: etag };

            if (response.status === 412) emit("pems-definition-conflict", { resource: path });
            if (response.status === 401 || response.status === 403) emit("pems-definition-unauthorized", { status: response.status });
            var mapped = mapProblem(data);
            return { ok: false, status: response.status, problem: data, fieldErrors: mapped.fieldErrors, summary: mapped.summary };
        }

        function autosave(id, read, opts) {
            opts = opts || {};
            var delay = opts.delay || 1500;
            var timer = null;
            var state = "saved";
            function set(s) { state = s; if (opts.onState) opts.onState(s); }
            async function flush() {
                timer = null;
                set("saving");
                var result = await api.saveDraft(id, read());
                set(result.ok ? "saved" : result.status === 412 ? "conflict" : "error");
                if (!result.ok && opts.onError) opts.onError(result);
            }
            return {
                touch: function () { set("unsaved"); if (timer) clearTimeout(timer); timer = setTimeout(flush, delay); },
                flush: function () { if (timer) clearTimeout(timer); return flush(); },
                state: function () { return state; }
            };
        }

        var api = {
            list: function (query) { return call("GET", query ? "?" + new URLSearchParams(query).toString() : ""); },
            get: function (id) { return call("GET", "/" + id); },
            getVersion: function (id, n) { return call("GET", "/" + id + "/versions/" + n); },
            getDraft: function (id) { return call("GET", "/" + id + "/draft", undefined, { etagKey: "/" + id + "/draft" }); },
            saveDraft: function (id, payload, changeSummary) { return call("PUT", "/" + id + "/draft", { payload: payload, changeSummary: changeSummary || null }, { etagKey: "/" + id + "/draft" }); },
            createDraft: function (id, fromVersion) { return call("POST", "/" + id + "/actions/create-draft", { fromVersion: fromVersion || null }, { etagKey: "/" + id }); },
            validate: function (id) { return call("POST", "/" + id + "/actions/validate", undefined, { etagKey: "/" + id + "/draft" }); },
            publish: function (id) { return call("POST", "/" + id + "/actions/publish", undefined, { etagKey: "/" + id + "/draft" }); },
            rollback: function (id, toVersion) { return call("POST", "/" + id + "/actions/rollback", { toVersion: toVersion }, { etagKey: "/" + id }); },
            discardDraft: function (id) { return call("DELETE", "/" + id + "/draft", undefined, { etagKey: "/" + id + "/draft" }); },
            exportDefinition: function (id, version) { return call("GET", "/" + id + "/export" + (version ? "?version=" + version : "")); },
            importPackage: function (pkg) { return call("POST", "/import", pkg, { idempotent: true, ifMatch: false }); },
            autosave: autosave,
            mapProblem: mapProblem
        };
        return api;
    }

    window.PemsDefinitions = { create: create, mapProblem: mapProblem };
})();
