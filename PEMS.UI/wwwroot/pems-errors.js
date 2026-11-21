// PEMS.UI client error handling (SCRUM-467). One fetch wrapper, one toast, global capture of script errors. Vanilla, offline, no inline handlers.
//   PemsFetch.request(url, options) -> Promise<Response>   rejects with PemsFetchError on a network failure, a timeout or a non-2xx status
//   PemsFetch.json(url, options)    -> Promise<any>        the same, and parses a JSON body (204 gives null)
//     options: method, headers, body, timeout (ms, default 30000), retry (extra tries for GET and HEAD only, default 2), signal, quiet (true: no toast)
//     adds the antiforgery header on unsafe verbs (the page's __RequestVerificationToken input), aborts in-flight calls when the page is left,
//     reads application/problem+json (RFC 9457) and the X-Trace-Id header (the same reference the server logged), retries only idempotent requests
//     with backoff on a network failure or a 502/503/504, and shows ONE toast per failed call: the localised message plus the reference with a copy button.
//   PemsFetch.toast(error)          shows the toast for an error the caller caught (does nothing twice for the same error)
//   PemsErrors.show(message, traceId)   the same toast for a message that did not come from a request
// A failure nobody caught (window.onerror, unhandledrejection) shows the generic message; a PemsFetchError that was already shown stays silent.
// Texts are inputs: the layout puts the localised messages on <body data-pems-error-generic / -offline / -timeout / -signin / -forbidden / -notfound /
// -conflict / -invalid / -toomany / -unavailable / -server / -reference / -copy / -copied / -close>.
(function () {
    'use strict';

    var inflight = new Set();
    var areaNode = null;
    var recent = Object.create(null);

    function text(name, fallback) {
        var body = document.body;
        return (body && body.getAttribute('data-pems-error-' + name)) || fallback || '';
    }

    function PemsFetchError(message, details) {
        var e = new Error(message);
        e.name = 'PemsFetchError';
        e.status = details.status || 0;
        e.traceId = details.traceId || '';
        e.problem = details.problem || null;
        e.kind = details.kind || 'http';       // http, offline, timeout
        e.reported = false;
        return e;
    }

    function messageFor(err) {
        if (err.kind === 'offline') return text('offline', 'No connection.');
        if (err.kind === 'timeout') return text('timeout', 'The server took too long to answer.');
        switch (err.status) {
            case 401: return text('signin', 'Sign in again to continue.');
            case 403: return text('forbidden', 'You are not allowed to do this.');
            case 404: return text('notfound', 'The record was not found.');
            case 409: case 412: return text('conflict', 'The record was changed by someone else.');
            case 400: case 422: return text('invalid', 'The data is not valid.');
            case 429: return text('toomany', 'Too many requests, wait a moment.');
            case 503: return text('unavailable', 'The service is not available, try again.');
            default: return err.status >= 500 ? text('server', 'The server could not complete the request.') : text('generic', 'Something went wrong.');
        }
    }

    function area() {
        if (areaNode && document.body.contains(areaNode)) return areaNode;
        areaNode = window.PemsTemplate.clone('pems-error-toasts');
        document.body.appendChild(areaNode);
        return areaNode;
    }

    function copy(value, button) {
        function done() { button.textContent = text('copied', 'Copied'); }
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(value).then(done, function () { });
        else {
            var tmp = window.PemsTemplate.clone('pems-copy-buffer'); tmp.value = value; document.body.appendChild(tmp); tmp.select();
            try { document.execCommand('copy'); done(); } catch (e) { /* not copied */ }
            document.body.removeChild(tmp);
        }
    }

    function show(message, traceId) {
        if (!document.body) return;
        var key = message + '|' + (traceId || '');
        var now = Date.now();
        if (recent[key] && now - recent[key] < 2000) return;          // the same failure twice in a row is one toast
        recent[key] = now;

        var toast = window.PemsTemplate.fill(window.PemsTemplate.clone('pems-error-toast'), {
            message: message,
            ref: traceId ? {} : null,
            label: text('reference', 'Reference') + ': ',
            code: traceId || '',
            copy: text('copy', 'Copy'),
            close: { text: '×', attrs: { 'aria-label': text('close', 'Close') } }
        });
        var copyBtn = toast.querySelector('[data-slot=copy]');
        if (copyBtn) copyBtn.addEventListener('click', function () { copy(traceId, copyBtn); });
        toast.querySelector('[data-slot=close]').addEventListener('click', function () { toast.remove(); });
        area().appendChild(toast);
        setTimeout(function () { toast.remove(); }, 12000);
    }

    function toast(err) {
        if (!err || err.reported) return;
        err.reported = true;
        show(messageFor(err), err.traceId);
    }

    function antiforgery() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function isUnsafe(method) { return !/^(GET|HEAD|OPTIONS)$/i.test(method); }

    function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

    function once(url, options, timeout) {
        var controller = new AbortController();
        var timedOut = false;
        var timer = setTimeout(function () { timedOut = true; controller.abort(); }, timeout);
        if (options.signal) options.signal.addEventListener('abort', function () { controller.abort(); });
        inflight.add(controller);
        var init = {
            method: options.method || 'GET', headers: options.headers || {}, body: options.body,
            credentials: options.credentials || 'same-origin', cache: options.cache, signal: controller.signal
        };
        return fetch(url, init).then(function (res) {
            return res;
        }, function (e) {
            if (timedOut) throw PemsFetchError('timeout', { kind: 'timeout' });
            if (e && e.name === 'AbortError') { var a = PemsFetchError('aborted', { kind: 'aborted' }); a.reported = true; throw a; }
            throw PemsFetchError('offline', { kind: 'offline' });
        }).finally(function () { clearTimeout(timer); inflight.delete(controller); });
    }

    function readProblem(res) {
        var type = res.headers.get('content-type') || '';
        if (type.indexOf('json') === -1) return Promise.resolve(null);
        return res.clone().json().then(function (j) { return j; }, function () { return null; });
    }

    function request(url, options) {
        options = options || {};
        var method = (options.method || 'GET').toUpperCase();
        var headers = {};
        Object.keys(options.headers || {}).forEach(function (k) { headers[k] = options.headers[k]; });
        if (isUnsafe(method)) {
            var token = antiforgery();
            var api = /^\/api(\/|$)/.test(url);
            if (token && !headers['RequestVerificationToken'] && !headers['X-CSRF-TOKEN']) headers[api ? 'X-CSRF-TOKEN' : 'RequestVerificationToken'] = token;
        }
        var opts = { method: method, headers: headers, body: options.body, signal: options.signal, credentials: options.credentials, cache: options.cache };
        var timeout = options.timeout || 30000;
        var tries = /^(GET|HEAD)$/.test(method) ? (options.retry == null ? 2 : options.retry) : 0;

        function attempt(n) {
            return once(url, opts, timeout).then(function (res) {
                if (res.ok) return res;
                var retryable = tries > n && (res.status === 502 || res.status === 503 || res.status === 504);
                if (retryable) return sleep(300 * Math.pow(2, n)).then(function () { return attempt(n + 1); });
                return readProblem(res).then(function (problem) {
                    throw PemsFetchError('http ' + res.status, { status: res.status, traceId: res.headers.get('X-Trace-Id') || (problem && problem.traceId) || '', problem: problem });
                });
            }, function (e) {
                if (e.kind === 'offline' && tries > n) return sleep(300 * Math.pow(2, n)).then(function () { return attempt(n + 1); });
                throw e;
            });
        }
        return attempt(0).catch(function (e) {
            if (e && e.name === 'PemsFetchError' && !options.quiet) toast(e);
            throw e;
        });
    }

    function json(url, options) {
        return request(url, options).then(function (res) {
            return res.status === 204 ? null : res.json();
        });
    }

    // leaving the page cancels what is still running, without a toast
    window.addEventListener('pagehide', function () { inflight.forEach(function (c) { c.abort(); }); });

    // a failure nobody caught
    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        if (r && r.name === 'PemsFetchError') { toast(r); return; }
        if (r && r.name === 'AbortError') return;
        if (window.console) console.error(r);
        show(text('generic', 'Something went wrong.'));
    });
    window.addEventListener('error', function (e) {
        if (e.target && e.target !== window) return;                       // a missing image or script is not a script error
        if (/ResizeObserver loop/.test(e.message || '')) return;
        if (window.console) console.error(e.error || e.message);
        show(text('generic', 'Something went wrong.'));
    });

    window.PemsFetch = { request: request, json: json, toast: toast };
    window.PemsErrors = { show: show };
})();
