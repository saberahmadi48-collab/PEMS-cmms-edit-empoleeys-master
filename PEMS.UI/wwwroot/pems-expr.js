// PEMS expression language in the browser (SCRUM-39, ADR-0015). The same grammar and meaning as PEMS.Application/Expressions/Expression.cs: tools/js-tests/expression-cases.json
// is run by both, so one expression gives one result on the server and in the browser. No statements, no loops, no assignment, no access to the page or to any object
// outside the context: only the functions below.
//   var e = PemsExpr.compile("values.amount > 100 and 'Admin' in user.roles", ['values', 'user']);
//   e.evaluate({ values: {...}, user: {...} }, '2026-11-16')   // the second argument is "today" (yyyy-MM-dd), so a result is repeatable
//   e.condition(ctx, today)                                    // true / false (nothing = false), any other result throws
(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.PemsExpr = factory();
})(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    var LIMITS = { length: 2000, nodes: 500, depth: 32, steps: 10000, string: 10000, array: 1000 };

    function ExprError(code, message, position) {
        var e = new Error(message);
        e.name = 'ExpressionError'; e.code = code; e.position = position === undefined ? -1 : position;
        return e;
    }

    var PUNCT = ['==', '!=', '<=', '>=', '&&', '||', '(', ')', '[', ']', ',', '.', '?', ':', '+', '-', '*', '/', '%', '!', '<', '>'];
    var ARITY = {
        isEmpty: [1, 1], len: [1, 1], contains: [2, 2], startsWith: [2, 2], endsWith: [2, 2], lower: [1, 1], upper: [1, 1], trim: [1, 1], normalize: [1, 1],
        number: [1, 1], text: [1, 1], round: [1, 2], abs: [1, 1], min: [1, Infinity], max: [1, Infinity], sum: [1, 1], coalesce: [1, Infinity], 'if': [3, 3],
        today: [0, 0], year: [1, 1], month: [1, 1], day: [1, 1], addDays: [2, 2], diffDays: [2, 2], jalaliYear: [1, 1], jalaliMonth: [1, 1], jalaliDay: [1, 1]
    };

    function isDigit(c) { return c >= '0' && c <= '9'; }
    function isLetter(c) { return /[\p{L}_]/u.test(c); }
    function isLetterOrDigit(c) { return /[\p{L}\p{Nd}_]/u.test(c); }

    function tokenize(s) {
        var tokens = [], i = 0;
        while (i < s.length) {
            var c = s[i];
            if (/\s/.test(c)) { i++; continue; }
            if (isDigit(c)) {
                var st = i;
                while (i < s.length && isDigit(s[i])) i++;
                if (i + 1 < s.length && s[i] === '.' && isDigit(s[i + 1])) { i++; while (i < s.length && isDigit(s[i])) i++; }
                tokens.push({ kind: 'number', text: s.slice(st, i), pos: st, number: parseFloat(s.slice(st, i)) });
                continue;
            }
            if (c === '\'' || c === '"') {
                var q = c, from = i++, out = '';
                for (;;) {
                    if (i >= s.length) throw ExprError('syntax', 'A text is not closed.', from);
                    if (s[i] === '\\' && i + 1 < s.length) { var n = s[i + 1]; out += n === 'n' ? '\n' : n === 't' ? '\t' : n; i += 2; continue; }
                    if (s[i] === q) { i++; break; }
                    out += s[i++];
                    if (out.length > LIMITS.string) throw ExprError('limit-size', 'A text in the expression is too long.', from);
                }
                tokens.push({ kind: 'string', text: out, pos: from });
                continue;
            }
            if (isLetter(c)) {
                var from2 = i;
                while (i < s.length && isLetterOrDigit(s[i])) i++;
                tokens.push({ kind: 'ident', text: s.slice(from2, i), pos: from2 });
                continue;
            }
            var punct = null;
            for (var k = 0; k < PUNCT.length; k++) if (s.substr(i, PUNCT[k].length) === PUNCT[k]) { punct = PUNCT[k]; break; }
            if (!punct) throw ExprError('syntax', 'Unexpected character "' + c + '".', i);
            tokens.push({ kind: 'punct', text: punct, pos: i });
            i += punct.length;
        }
        tokens.push({ kind: 'end', text: '', pos: s.length });
        return tokens;
    }

    function parse(source) {
        var tokens = tokenize(source), p = 0, nodes = 0, depth = 0;
        function peek() { return tokens[p]; }
        function isPunct(t) { return peek().kind === 'punct' && peek().text === t; }
        function isWord(t) { return peek().kind === 'ident' && peek().text === t; }
        function count(n) { if (++nodes > LIMITS.nodes) throw ExprError('limit-nodes', 'The expression has more than ' + LIMITS.nodes + ' parts.'); return n; }
        function nested(f) {
            if (++depth > LIMITS.depth) throw ExprError('limit-depth', 'The expression is nested deeper than ' + LIMITS.depth + ' levels.');
            try { return f(); } finally { depth--; }
        }
        function expect(t) { if (!isPunct(t)) throw ExprError('syntax', 'Expected "' + t + '".', peek().pos); p++; }

        function ternary() {
            var test = or();
            if (!isPunct('?')) return test;
            p++;
            var a = nested(ternary);
            if (!isPunct(':')) throw ExprError('syntax', 'Expected ":" in the condition.', peek().pos);
            p++;
            var b = nested(ternary);
            return count({ t: 'ternary', test: test, a: a, b: b });
        }
        function or() { var l = and(); while (isPunct('||') || isWord('or')) { p++; l = count({ t: 'bin', op: 'or', l: l, r: and() }); } return l; }
        function and() { var l = not(); while (isPunct('&&') || isWord('and')) { p++; l = count({ t: 'bin', op: 'and', l: l, r: not() }); } return l; }
        function not() {
            if (isPunct('!') || isWord('not')) { p++; return count({ t: 'un', op: 'not', e: nested(not) }); }
            return compare();
        }
        function compare() {
            var l = add();
            for (;;) {
                var op = null, t = peek();
                if (t.kind === 'punct' && (t.text === '==' || t.text === '!=' || t.text === '<' || t.text === '<=' || t.text === '>' || t.text === '>=')) op = t.text;
                else if (isWord('in')) op = 'in';
                if (!op) return l;
                p++;
                l = count({ t: 'bin', op: op, l: l, r: add() });
            }
        }
        function add() { var l = mul(); while (isPunct('+') || isPunct('-')) { var op = peek().text; p++; l = count({ t: 'bin', op: op, l: l, r: mul() }); } return l; }
        function mul() { var l = unary(); while (isPunct('*') || isPunct('/') || isPunct('%')) { var op = peek().text; p++; l = count({ t: 'bin', op: op, l: l, r: unary() }); } return l; }
        function unary() {
            if (isPunct('-')) { p++; return count({ t: 'un', op: 'neg', e: nested(unary) }); }
            if (isPunct('!')) { p++; return count({ t: 'un', op: 'not', e: nested(unary) }); }
            return postfix();
        }
        function postfix() {
            var n = primary();
            for (;;) {
                if (isPunct('.')) {
                    p++;
                    if (peek().kind !== 'ident') throw ExprError('syntax', 'Expected a name after ".".', peek().pos);
                    n = count({ t: 'member', target: n, name: peek().text }); p++;
                } else if (isPunct('[')) { p++; var at = nested(ternary); expect(']'); n = count({ t: 'index', target: n, at: at }); }
                else return n;
            }
        }
        function list(close) {
            var items = [];
            if (!isPunct(close)) { do { items.push(nested(ternary)); } while (isPunct(',') && ++p > 0); }
            expect(close);
            return items;
        }
        function primary() {
            var t = peek();
            if (t.kind === 'number') { p++; return count({ t: 'lit', v: t.number }); }
            if (t.kind === 'string') { p++; return count({ t: 'lit', v: t.text }); }
            if (t.kind === 'ident') {
                p++;
                if (t.text === 'true') return count({ t: 'lit', v: true });
                if (t.text === 'false') return count({ t: 'lit', v: false });
                if (t.text === 'null') return count({ t: 'lit', v: null });
                if (t.text === 'and' || t.text === 'or' || t.text === 'not' || t.text === 'in') throw ExprError('syntax', 'Unexpected "' + t.text + '".', t.pos);
                if (isPunct('(')) {
                    p++;
                    var args = list(')');
                    if (!Object.prototype.hasOwnProperty.call(ARITY, t.text)) throw ExprError('unknown-function', '"' + t.text + '" is not a function.', t.pos);
                    var ar = ARITY[t.text];
                    if (args.length < ar[0] || args.length > ar[1]) throw ExprError('arity', t.text + ' takes a different number of arguments.', t.pos);
                    return count({ t: 'call', name: t.text, args: args });
                }
                return count({ t: 'id', name: t.text });
            }
            if (t.kind === 'punct' && t.text === '(') { p++; var inner = nested(ternary); expect(')'); return inner; }
            if (t.kind === 'punct' && t.text === '[') { p++; return count({ t: 'array', items: list(']') }); }
            throw ExprError('syntax', t.kind === 'end' ? 'The expression ends too early.' : 'Unexpected "' + t.text + '".', t.pos);
        }

        var root = ternary();
        if (peek().kind !== 'end') throw ExprError('syntax', 'Unexpected "' + peek().text + '".', peek().pos);
        return root;
    }

    function collect(n, set) {
        switch (n.t) {
            case 'id': set[n.name] = true; break;
            case 'member': collect(n.target, set); break;
            case 'index': collect(n.target, set); collect(n.at, set); break;
            case 'un': collect(n.e, set); break;
            case 'bin': collect(n.l, set); collect(n.r, set); break;
            case 'ternary': collect(n.test, set); collect(n.a, set); collect(n.b, set); break;
            case 'call': n.args.forEach(function (a) { collect(a, set); }); break;
            case 'array': n.items.forEach(function (a) { collect(a, set); }); break;
        }
    }

    // ---- values: null, boolean, number, string, array, plain object
    function kind(v) { return v === null || v === undefined ? 'nothing' : typeof v === 'boolean' ? 'a boolean' : typeof v === 'number' ? 'a number' : typeof v === 'string' ? 'a text' : Array.isArray(v) ? 'a list' : 'an object'; }
    function norm(v) { return v === undefined ? null : v; }
    function truth(v, what) {
        if (v === null || v === undefined) return false;
        if (typeof v === 'boolean') return v;
        throw ExprError('type', 'A ' + what + ' must be true or false, not ' + kind(v) + '.');
    }
    function equal(a, b) {
        a = norm(a); b = norm(b);
        if (a === null && b === null) return true;
        if (typeof a !== typeof b || a === null || b === null) return false;
        if (typeof a === 'object') return false;
        return a === b;
    }
    function hasOwn(o, k) { return Object.prototype.hasOwnProperty.call(o, k); }
    function get(target, name) {
        if (Array.isArray(target)) return name === 'length' ? target.length : null;
        if (typeof target === 'string') return name === 'length' ? target.length : null;
        if (target !== null && typeof target === 'object' && hasOwn(target, name)) return norm(target[name]);
        return null;
    }
    function getAt(target, at) {
        if (Array.isArray(target) && typeof at === 'number') return Number.isInteger(at) && at >= 0 && at < target.length ? norm(target[at]) : null;
        if (target !== null && typeof target === 'object' && !Array.isArray(target) && typeof at === 'string') return hasOwn(target, at) ? norm(target[at]) : null;
        return null;
    }
    function str(v, fn) { if (v === null) return ''; if (typeof v === 'string') return v; throw ExprError('type', fn + ' needs a text, not ' + kind(v) + '.'); }
    function num(v, fn) { if (typeof v === 'number') return v; throw ExprError('type', fn + ' needs a number, not ' + kind(v) + '.'); }

    function normalizeText(s) {
        var out = '';
        for (var i = 0; i < s.length; i++) {
            var ch = s[i];
            if (ch === 'ي' || ch === 'ى') out += 'ی';
            else if (ch === 'ك') out += 'ک';
            else if (ch === 'ـ' || ch === '‌' || ch === '​' || ch === '‍') continue;
            else if (ch >= '۰' && ch <= '۹') out += String.fromCharCode(48 + ch.charCodeAt(0) - 0x06F0);
            else if (ch >= '٠' && ch <= '٩') out += String.fromCharCode(48 + ch.charCodeAt(0) - 0x0660);
            else out += ch;
        }
        return out.replace(/\s+/g, ' ').trim().toLowerCase();
    }
    function numberText(d) { return Number.isInteger(d) && Math.abs(d) < 1e15 ? String(d) : String(d); }
    function parseDate(v) {
        if (typeof v !== 'string' || v.length < 10) return null;
        var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(v);
        if (!m) return null;
        var y = +m[1], mo = +m[2], d = +m[3], dt = new Date(Date.UTC(y, mo - 1, d));
        if (y < 1 || dt.getUTCFullYear() !== y || dt.getUTCMonth() !== mo - 1 || dt.getUTCDate() !== d) return null;
        return dt;
    }
    function iso(dt) { return dt.toISOString().slice(0, 10); }
    var persian = typeof Intl !== 'undefined' ? new Intl.DateTimeFormat('en-u-ca-persian-nu-latn', { year: 'numeric', month: 'numeric', day: 'numeric', timeZone: 'UTC' }) : null;
    function jalali(dt) {
        var o = {};
        persian.formatToParts(dt).forEach(function (x) { if (x.type === 'year' || x.type === 'month' || x.type === 'day') o[x.type] = +x.value; });
        return o;
    }

    function evaluate(root, ctx, today, state) {
        function step() { if (++state.steps > LIMITS.steps) throw ExprError('limit-steps', 'The expression needed more than ' + LIMITS.steps + ' steps.'); }
        function ev(n) {
            step();
            switch (n.t) {
                case 'lit': return n.v;
                case 'id': return hasOwn(ctx, n.name) ? norm(ctx[n.name]) : null;
                case 'member': return get(ev(n.target), n.name);
                case 'index': return getAt(ev(n.target), ev(n.at));
                case 'array':
                    if (n.items.length > LIMITS.array) throw ExprError('limit-size', 'A list in the expression is too long.');
                    return n.items.map(ev);
                case 'un': {
                    var v = ev(n.e);
                    if (n.op === 'not') return !truth(v, 'condition');
                    if (v === null) return null;
                    if (typeof v === 'number') return -v;
                    throw ExprError('type', 'A minus needs a number, not ' + kind(v) + '.');
                }
                case 'ternary': return truth(ev(n.test), 'condition') ? ev(n.a) : ev(n.b);
                case 'bin': return bin(n);
                case 'call': return call(n);
            }
            throw ExprError('syntax', 'Unknown expression part.');
        }
        function bin(n) {
            if (n.op === 'and') { var l = truth(ev(n.l), 'condition'); return l && truth(ev(n.r), 'condition'); }
            if (n.op === 'or') { var l2 = truth(ev(n.l), 'condition'); return l2 || truth(ev(n.r), 'condition'); }
            var a = ev(n.l), b = ev(n.r);
            switch (n.op) {
                case '==': return equal(a, b);
                case '!=': return !equal(a, b);
                case 'in':
                    if (typeof b === 'string' && typeof a === 'string') return b.indexOf(a) >= 0;
                    if (Array.isArray(b)) return b.some(function (e) { return equal(a, e); });
                    return false;
                case '<': case '<=': case '>': case '>=': {
                    if (a === null || b === null) return false;
                    var cmp;
                    if (typeof a === 'number' && typeof b === 'number') cmp = a < b ? -1 : a > b ? 1 : 0;
                    else if (typeof a === 'string' && typeof b === 'string') cmp = a < b ? -1 : a > b ? 1 : 0;   // UTF-16 code unit order, the same as an ordinal compare
                    else throw ExprError('type', 'Cannot compare ' + kind(a) + ' with ' + kind(b) + '.');
                    return n.op === '<' ? cmp < 0 : n.op === '<=' ? cmp <= 0 : n.op === '>' ? cmp > 0 : cmp >= 0;
                }
            }
            if (a === null || b === null) return null;
            if (n.op === '+' && typeof a === 'string' && typeof b === 'string') {
                if (a.length + b.length > LIMITS.string) throw ExprError('limit-size', 'A text in the expression became too long.');
                return a + b;
            }
            if (typeof a === 'number' && typeof b === 'number') {
                switch (n.op) {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    case '/': if (b === 0) throw ExprError('divide-by-zero', 'Division by zero.'); return a / b;
                    case '%': if (b === 0) throw ExprError('divide-by-zero', 'Division by zero.'); return a % b;
                }
            }
            throw ExprError('type', '"' + n.op + '" needs two numbers (or two texts for +), not ' + kind(a) + ' and ' + kind(b) + '.');
        }
        function call(n) {
            if (n.name === 'if') return truth(ev(n.args[0]), 'condition') ? ev(n.args[1]) : ev(n.args[2]);
            if (n.name === 'coalesce') { for (var i = 0; i < n.args.length; i++) { var v = ev(n.args[i]); if (v !== null) return v; } return null; }
            var a = n.args.map(ev);
            switch (n.name) {
                case 'isEmpty': return a[0] === null || (typeof a[0] === 'string' && a[0].trim() === '') || (Array.isArray(a[0]) && a[0].length === 0);
                case 'len':
                    if (a[0] === null) return 0;
                    if (typeof a[0] === 'string' || Array.isArray(a[0])) return a[0].length;
                    throw ExprError('type', 'len needs a text or a list, not ' + kind(a[0]) + '.');
                case 'contains':
                    if (typeof a[0] === 'string' && typeof a[1] === 'string') return a[0].indexOf(a[1]) >= 0;
                    if (Array.isArray(a[0])) return a[0].some(function (e) { return equal(e, a[1]); });
                    return false;
                case 'startsWith': return typeof a[0] === 'string' && typeof a[1] === 'string' && a[0].lastIndexOf(a[1], 0) === 0;
                case 'endsWith': return typeof a[0] === 'string' && typeof a[1] === 'string' && (a[1].length === 0 || (a[0].length >= a[1].length && a[0].slice(a[0].length - a[1].length) === a[1]));
                case 'lower': return a[0] === null ? null : str(a[0], 'lower').toLowerCase();
                case 'upper': return a[0] === null ? null : str(a[0], 'upper').toUpperCase();
                case 'trim': return a[0] === null ? null : str(a[0], 'trim').trim();
                case 'normalize': return a[0] === null ? null : normalizeText(str(a[0], 'normalize'));
                case 'number':
                    if (typeof a[0] === 'number') return a[0];
                    if (typeof a[0] === 'string') { var t = normalizeText(a[0]); return /^-?\d+(\.\d+)?$/.test(t) ? parseFloat(t) : null; }
                    return null;
                case 'text':
                    if (a[0] === null) return '';
                    if (typeof a[0] === 'boolean') return a[0] ? 'true' : 'false';
                    if (typeof a[0] === 'number') return numberText(a[0]);
                    if (typeof a[0] === 'string') return a[0];
                    throw ExprError('type', 'text cannot show ' + kind(a[0]) + '.');
                case 'round': {
                    if (a[0] === null) return null;
                    var digits = a.length > 1 ? Math.trunc(num(a[1], 'round')) : 0;
                    if (digits < 0 || digits > 6) throw ExprError('type', 'round takes 0 to 6 digits.');
                    var f = Math.pow(10, digits), x = num(a[0], 'round');
                    return Math.sign(x) * Math.floor(Math.abs(x) * f + 0.5) / f;
                }
                case 'abs': return a[0] === null ? null : Math.abs(num(a[0], 'abs'));
                case 'min': case 'max': {
                    var nums = a.filter(function (e) { return e !== null; }).map(function (e) { return num(e, n.name); });
                    return nums.length === 0 ? null : n.name === 'min' ? Math.min.apply(null, nums) : Math.max.apply(null, nums);
                }
                case 'sum': return Array.isArray(a[0]) ? a[0].filter(function (e) { return e !== null; }).reduce(function (s, e) { return s + num(e, 'sum'); }, 0) : null;
                case 'today': return today;
                case 'year': case 'month': case 'day': {
                    var d = parseDate(a[0]);
                    if (!d) return null;
                    return n.name === 'year' ? d.getUTCFullYear() : n.name === 'month' ? d.getUTCMonth() + 1 : d.getUTCDate();
                }
                case 'addDays': { var d0 = parseDate(a[0]); return d0 && typeof a[1] === 'number' ? iso(new Date(d0.getTime() + a[1] * 86400000)) : null; }
                case 'diffDays': { var da = parseDate(a[0]), db = parseDate(a[1]); return da && db ? (da.getTime() - db.getTime()) / 86400000 : null; }
                case 'jalaliYear': case 'jalaliMonth': case 'jalaliDay': {
                    var jd = parseDate(a[0]);
                    if (!jd) return null;
                    var j = jalali(jd);
                    return n.name === 'jalaliYear' ? j.year : n.name === 'jalaliMonth' ? j.month : j.day;
                }
            }
            throw ExprError('unknown-function', '"' + n.name + '" is not a function.');
        }
        return ev(root);
    }

    function compile(source, allowedRoots) {
        if (typeof source !== 'string') throw ExprError('syntax', 'An expression is a text.');
        if (source.length > LIMITS.length) throw ExprError('limit-length', 'The expression is longer than ' + LIMITS.length + ' characters.');
        var root = parse(source), set = {};
        collect(root, set);
        var roots = Object.keys(set).sort();
        if (allowedRoots) roots.forEach(function (r) {
            if (allowedRoots.indexOf(r) < 0) throw ExprError('unknown-variable', '"' + r + '" is not a variable here (available: ' + allowedRoots.slice().sort().join(', ') + ').');
        });
        return {
            source: source,
            roots: roots,
            evaluate: function (ctx, today) { return evaluate(root, ctx || {}, today || new Date().toISOString().slice(0, 10), { steps: 0 }); },
            condition: function (ctx, today) {
                var v = this.evaluate(ctx, today);
                if (v === null) return false;
                if (typeof v === 'boolean') return v;
                throw ExprError('type', 'A condition must give true or false.');
            }
        };
    }

    return { compile: compile, limits: LIMITS };
});
