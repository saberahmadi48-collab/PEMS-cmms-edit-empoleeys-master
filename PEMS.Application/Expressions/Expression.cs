using System.Globalization;
using System.Text;

namespace PEMS.Application.Expressions;

// SCRUM-39 (#15), ADR-0015: the expression language of the definitions (visible, required, readonly, validation conditions, computed values).
// A small language with no statements, no loops, no assignment and no calls into .NET: only the functions below. The same grammar and semantics are
// implemented in the browser (PEMS.UI/wwwroot/pems-expr.js); tools/js-tests/expression-cases.json is run by both, so one expression gives one result everywhere.

public sealed class ExpressionLimits
{
    public int MaxLength { get; init; } = 2000;
    public int MaxNodes { get; init; } = 500;
    public int MaxDepth { get; init; } = 32;
    public int MaxSteps { get; init; } = 10_000;
    public int MaxString { get; init; } = 10_000;
    public int MaxArray { get; init; } = 1_000;
}

public sealed class ExpressionException(string code, string message, int position = -1) : Exception(message)
{
    /// <summary>A short stable code: syntax, unknown-variable, unknown-function, arity, type, divide-by-zero, limit-length, limit-nodes, limit-depth, limit-steps, limit-size.</summary>
    public string Code { get; } = code;
    public int Position { get; } = position;
}

/// <summary>The values an expression can see (<c>values.status</c>, <c>user.roles</c>, <c>request.culture</c> ...) and the clock (<c>today()</c> comes from here, so a result is repeatable).</summary>
public sealed class ExpressionContext(IReadOnlyDictionary<string, object?> roots, DateTime now)
{
    /// <summary>The roots in the value model of the language (converted once here, not at every read).</summary>
    public IReadOnlyDictionary<string, object?> Roots { get; } = roots.ToDictionary(p => p.Key, p => ExpressionValues.Normalize(p.Value), StringComparer.Ordinal);
    public DateTime Now { get; } = now;
}

/// <summary>Turns JSON and .NET values into the value model of the language: null, bool, double, string, list, dictionary.</summary>
public static class ExpressionValues
{
    public static object? FromJson(System.Text.Json.Nodes.JsonNode? node) => node switch
    {
        null => null,
        System.Text.Json.Nodes.JsonObject o => o.ToDictionary(p => p.Key, p => FromJson(p.Value), StringComparer.Ordinal),
        System.Text.Json.Nodes.JsonArray a => a.Select(FromJson).ToList(),
        System.Text.Json.Nodes.JsonValue v => v.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number => v.GetValue<double>(),
            System.Text.Json.JsonValueKind.String => v.GetValue<string>(),
            _ => null
        },
        _ => null
    };

    public static object? Normalize(object? v) => v switch
    {
        null => null,
        bool or string => v,
        double => v,
        int or long or short or byte or float or decimal => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        System.Text.Json.Nodes.JsonNode n => FromJson(n),
        IReadOnlyDictionary<string, object?> d => d.ToDictionary(p => p.Key, p => Normalize(p.Value), StringComparer.Ordinal),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Normalize).ToList(),
        _ => v.ToString()
    };
}

public static class ExpressionCompiler
{
    /// <summary>Parses and checks an expression. <paramref name="allowedRoots"/> are the variable names the place of use provides; an unknown one is an error at load, not at run time.</summary>
    public static CompiledExpression Compile(string source, IReadOnlyCollection<string>? allowedRoots = null, ExpressionLimits? limits = null)
    {
        limits ??= new ExpressionLimits();
        if (source.Length > limits.MaxLength) throw new ExpressionException("limit-length", $"The expression is longer than {limits.MaxLength} characters.");
        var parser = new Parser(source, limits);
        var root = parser.ParseAll();
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        Collect(root, roots);
        if (allowedRoots is not null)
            foreach (var r in roots)
                if (!allowedRoots.Contains(r)) throw new ExpressionException("unknown-variable", $"\"{r}\" is not a variable here (available: {string.Join(", ", allowedRoots.OrderBy(x => x, StringComparer.Ordinal))}).");
        return new CompiledExpression(source, root, roots, limits);
    }

    public static bool TryCompile(string source, out CompiledExpression? compiled, out ExpressionException? error, IReadOnlyCollection<string>? allowedRoots = null, ExpressionLimits? limits = null)
    {
        try { compiled = Compile(source, allowedRoots, limits); error = null; return true; }
        catch (ExpressionException ex) { compiled = null; error = ex; return false; }
    }

    private static void Collect(Node n, SortedSet<string> roots)
    {
        switch (n)
        {
            case Ident i: roots.Add(i.Name); break;
            case Member m: Collect(m.Target, roots); break;
            case Index x: Collect(x.Target, roots); Collect(x.At, roots); break;
            case Unary u: Collect(u.Operand, roots); break;
            case Binary b: Collect(b.Left, roots); Collect(b.Right, roots); break;
            case Ternary t: Collect(t.Test, roots); Collect(t.Then, roots); Collect(t.Else, roots); break;
            case Call c: foreach (var a in c.Args) Collect(a, roots); break;
            case ArrayLit a: foreach (var e in a.Items) Collect(e, roots); break;
        }
    }

    // ---- syntax tree
    internal abstract record Node;
    internal sealed record Literal(object? Value) : Node;
    internal sealed record Ident(string Name) : Node;
    internal sealed record Member(Node Target, string Name) : Node;
    internal sealed record Index(Node Target, Node At) : Node;
    internal sealed record Unary(string Op, Node Operand) : Node;
    internal sealed record Binary(string Op, Node Left, Node Right) : Node;
    internal sealed record Ternary(Node Test, Node Then, Node Else) : Node;
    internal sealed record Call(string Name, IReadOnlyList<Node> Args) : Node;
    internal sealed record ArrayLit(IReadOnlyList<Node> Items) : Node;

    private enum T { Number, String, Ident, Punct, End }
    private readonly record struct Token(T Kind, string Text, int Pos, double Number = 0);

    private sealed class Parser
    {
        private static readonly string[] Puncts = ["==", "!=", "<=", ">=", "&&", "||", "(", ")", "[", "]", ",", ".", "?", ":", "+", "-", "*", "/", "%", "!", "<", ">"];
        private readonly List<Token> _tokens = [];
        private readonly ExpressionLimits _limits;
        private int _p, _nodes, _depth;

        public Parser(string s, ExpressionLimits limits)
        {
            _limits = limits;
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c is >= '0' and <= '9')
                {
                    var st = i;
                    while (i < s.Length && s[i] is >= '0' and <= '9') i++;
                    if (i + 1 < s.Length && s[i] == '.' && s[i + 1] is >= '0' and <= '9') { i++; while (i < s.Length && s[i] is >= '0' and <= '9') i++; }
                    _tokens.Add(new Token(T.Number, s[st..i], st, double.Parse(s[st..i], CultureInfo.InvariantCulture)));
                    continue;
                }
                if (c is '\'' or '"')
                {
                    var st = i++; var sb = new StringBuilder();
                    while (true)
                    {
                        if (i >= s.Length) throw new ExpressionException("syntax", "A text is not closed.", st);
                        if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1] switch { 'n' => '\n', 't' => '\t', var o => o }); i += 2; continue; }
                        if (s[i] == c) { i++; break; }
                        sb.Append(s[i++]);
                        if (sb.Length > limits.MaxString) throw new ExpressionException("limit-size", "A text in the expression is too long.", st);
                    }
                    _tokens.Add(new Token(T.String, sb.ToString(), st));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    var st = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    _tokens.Add(new Token(T.Ident, s[st..i], st));
                    continue;
                }
                var punct = Puncts.FirstOrDefault(p => string.CompareOrdinal(s, i, p, 0, p.Length) == 0);
                if (punct is null) throw new ExpressionException("syntax", $"Unexpected character \"{c}\".", i);
                _tokens.Add(new Token(T.Punct, punct, i));
                i += punct.Length;
            }
            _tokens.Add(new Token(T.End, "", s.Length));
        }

        private Token Peek => _tokens[_p];
        private bool IsPunct(string t) => Peek.Kind == T.Punct && Peek.Text == t;
        private bool IsWord(string t) => Peek.Kind == T.Ident && Peek.Text == t;

        public Node ParseAll()
        {
            var n = ParseTernary();
            if (Peek.Kind != T.End) throw new ExpressionException("syntax", $"Unexpected \"{Peek.Text}\".", Peek.Pos);
            return n;
        }

        private T1 Count<T1>(T1 node)
        {
            if (++_nodes > _limits.MaxNodes) throw new ExpressionException("limit-nodes", $"The expression has more than {_limits.MaxNodes} parts.");
            return node;
        }

        private Node Nested(Func<Node> parse)
        {
            if (++_depth > _limits.MaxDepth) throw new ExpressionException("limit-depth", $"The expression is nested deeper than {_limits.MaxDepth} levels.");
            try { return parse(); } finally { _depth--; }
        }

        private Node ParseTernary()
        {
            var test = ParseOr();
            if (!IsPunct("?")) return test;
            _p++;
            var a = Nested(ParseTernary);
            if (!IsPunct(":")) throw new ExpressionException("syntax", "Expected \":\" in the condition.", Peek.Pos);
            _p++;
            var b = Nested(ParseTernary);
            return Count(new Ternary(test, a, b));
        }

        private Node ParseOr()
        {
            var l = ParseAnd();
            while (IsPunct("||") || IsWord("or")) { _p++; l = Count(new Binary("or", l, ParseAnd())); }
            return l;
        }

        private Node ParseAnd()
        {
            var l = ParseNot();
            while (IsPunct("&&") || IsWord("and")) { _p++; l = Count(new Binary("and", l, ParseNot())); }
            return l;
        }

        private Node ParseNot()
        {
            if (IsPunct("!") || IsWord("not")) { _p++; return Count(new Unary("not", Nested(ParseNot))); }
            return ParseCompare();
        }

        private Node ParseCompare()
        {
            var l = ParseAdd();
            while (true)
            {
                string? op = null;
                if (Peek.Kind == T.Punct && Peek.Text is "==" or "!=" or "<" or "<=" or ">" or ">=") op = Peek.Text;
                else if (IsWord("in")) op = "in";
                if (op is null) return l;
                _p++;
                l = Count(new Binary(op, l, ParseAdd()));
            }
        }

        private Node ParseAdd()
        {
            var l = ParseMul();
            while (IsPunct("+") || IsPunct("-")) { var op = Peek.Text; _p++; l = Count(new Binary(op, l, ParseMul())); }
            return l;
        }

        private Node ParseMul()
        {
            var l = ParseUnary();
            while (IsPunct("*") || IsPunct("/") || IsPunct("%")) { var op = Peek.Text; _p++; l = Count(new Binary(op, l, ParseUnary())); }
            return l;
        }

        private Node ParseUnary()
        {
            if (IsPunct("-")) { _p++; return Count(new Unary("neg", Nested(ParseUnary))); }
            if (IsPunct("!")) { _p++; return Count(new Unary("not", Nested(ParseUnary))); }
            return ParsePostfix();
        }

        private Node ParsePostfix()
        {
            var n = ParsePrimary();
            while (true)
            {
                if (IsPunct("."))
                {
                    _p++;
                    if (Peek.Kind != T.Ident) throw new ExpressionException("syntax", "Expected a name after \".\".", Peek.Pos);
                    n = Count(new Member(n, Peek.Text)); _p++;
                }
                else if (IsPunct("[")) { _p++; var at = Nested(ParseTernary); Expect("]"); n = Count(new Index(n, at)); }
                else return n;
            }
        }

        private void Expect(string p)
        {
            if (!IsPunct(p)) throw new ExpressionException("syntax", $"Expected \"{p}\".", Peek.Pos);
            _p++;
        }

        private Node ParsePrimary()
        {
            var t = Peek;
            switch (t.Kind)
            {
                case T.Number: _p++; return Count(new Literal(t.Number));
                case T.String: _p++; return Count(new Literal(t.Text));
                case T.Ident:
                    _p++;
                    if (t.Text is "true") return Count(new Literal(true));
                    if (t.Text is "false") return Count(new Literal(false));
                    if (t.Text is "null") return Count(new Literal(null));
                    if (t.Text is "and" or "or" or "not" or "in") throw new ExpressionException("syntax", $"Unexpected \"{t.Text}\".", t.Pos);
                    if (IsPunct("("))
                    {
                        _p++;
                        var args = new List<Node>();
                        if (!IsPunct(")"))
                            do { args.Add(Nested(ParseTernary)); } while (IsPunct(",") && ++_p > 0);
                        Expect(")");
                        if (!Functions.Arity.TryGetValue(t.Text, out var ar)) throw new ExpressionException("unknown-function", $"\"{t.Text}\" is not a function.", t.Pos);
                        if (args.Count < ar.Min || args.Count > ar.Max) throw new ExpressionException("arity", $"{t.Text} takes {(ar.Min == ar.Max ? ar.Min.ToString() : ar.Min + " to " + (ar.Max == int.MaxValue ? "any number of" : ar.Max.ToString()))} argument(s).", t.Pos);
                        return Count(new Call(t.Text, args));
                    }
                    return Count(new Ident(t.Text));
                case T.Punct when t.Text == "(":
                    _p++; var inner = Nested(ParseTernary); Expect(")"); return inner;
                case T.Punct when t.Text == "[":
                    _p++;
                    var items = new List<Node>();
                    if (!IsPunct("]"))
                        do { items.Add(Nested(ParseTernary)); } while (IsPunct(",") && ++_p > 0);
                    Expect("]");
                    return Count(new ArrayLit(items));
                default: throw new ExpressionException("syntax", t.Kind == T.End ? "The expression ends too early." : $"Unexpected \"{t.Text}\".", t.Pos);
            }
        }
    }

    // ---- functions (name -> arity); the implementations are in the evaluator
    internal static class Functions
    {
        public static readonly Dictionary<string, (int Min, int Max)> Arity = new(StringComparer.Ordinal)
        {
            ["isEmpty"] = (1, 1), ["len"] = (1, 1), ["contains"] = (2, 2), ["startsWith"] = (2, 2), ["endsWith"] = (2, 2),
            ["lower"] = (1, 1), ["upper"] = (1, 1), ["trim"] = (1, 1), ["normalize"] = (1, 1), ["number"] = (1, 1), ["text"] = (1, 1),
            ["round"] = (1, 2), ["abs"] = (1, 1), ["min"] = (1, int.MaxValue), ["max"] = (1, int.MaxValue), ["sum"] = (1, 1),
            ["coalesce"] = (1, int.MaxValue), ["if"] = (3, 3),
            ["today"] = (0, 0), ["year"] = (1, 1), ["month"] = (1, 1), ["day"] = (1, 1), ["addDays"] = (2, 2), ["diffDays"] = (2, 2),
            ["jalaliYear"] = (1, 1), ["jalaliMonth"] = (1, 1), ["jalaliDay"] = (1, 1)
        };
    }

    internal static class Eval
    {
        public sealed class Run(ExpressionContext ctx, ExpressionLimits limits)
        {
            public ExpressionContext Ctx { get; } = ctx;
            public ExpressionLimits Limits { get; } = limits;
            public int Steps;
            public void Step() { if (++Steps > Limits.MaxSteps) throw new ExpressionException("limit-steps", $"The expression needed more than {Limits.MaxSteps} steps."); }
        }

        public static object? Eval1(Node n, Run r)
        {
            r.Step();
            switch (n)
            {
                case Literal l: return l.Value;
                case Ident i: return r.Ctx.Roots.TryGetValue(i.Name, out var v) ? v : null;
                case Member m: return Get(Eval1(m.Target, r), m.Name);
                case Index x: return GetAt(Eval1(x.Target, r), Eval1(x.At, r));
                case ArrayLit a:
                    if (a.Items.Count > r.Limits.MaxArray) throw new ExpressionException("limit-size", "A list in the expression is too long.");
                    return a.Items.Select(e => Eval1(e, r)).ToList();
                case Unary u: return UnaryOp(u, Eval1(u.Operand, r));
                case Ternary t: return Truth(Eval1(t.Test, r), "condition") ? Eval1(t.Then, r) : Eval1(t.Else, r);
                case Binary b: return BinaryOp(b, r);
                case Call c: return CallFn(c, r);
            }
            throw new ExpressionException("syntax", "Unknown expression part.");
        }

        private static bool Truth(object? v, string what)
        {
            if (v is null) return false;
            if (v is bool b) return b;
            throw new ExpressionException("type", $"A {what} must be true or false, not {Kind(v)}.");
        }

        private static string Kind(object? v) => v switch { null => "nothing", bool => "a boolean", double => "a number", string => "a text", System.Collections.IList => "a list", _ => "an object" };

        private static object? Get(object? target, string name)
            => target is IReadOnlyDictionary<string, object?> d && d.TryGetValue(name, out var v) ? v
             : target is Dictionary<string, object?> d2 && d2.TryGetValue(name, out var v2) ? v2
             : target is System.Collections.IList l && name == "length" ? (double)l.Count
             : target is string s && name == "length" ? (double)s.Length : null;

        private static object? GetAt(object? target, object? at)
        {
            if (target is System.Collections.IList list && at is double d) { var i = (int)d; return d == i && i >= 0 && i < list.Count ? list[i] : null; }
            if (target is IReadOnlyDictionary<string, object?> m && at is string k) return m.TryGetValue(k, out var v) ? v : null;
            if (target is Dictionary<string, object?> m2 && at is string k2) return m2.TryGetValue(k2, out var v2) ? v2 : null;
            return null;
        }

        private static object? UnaryOp(Unary u, object? v)
        {
            if (u.Op == "not") return !Truth(v, "condition");
            if (v is null) return null;
            if (v is double d) return -d;
            throw new ExpressionException("type", $"A minus needs a number, not {Kind(v)}.");
        }

        public static bool Equal(object? a, object? b) => (a, b) switch
        {
            (null, null) => true,
            (bool x, bool y) => x == y,
            (double x, double y) => x == y,
            (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
            _ => false
        };

        private static object? BinaryOp(Binary b, Run r)
        {
            if (b.Op == "and") { var l = Truth(Eval1(b.Left, r), "condition"); return l && Truth(Eval1(b.Right, r), "condition"); }
            if (b.Op == "or") { var l = Truth(Eval1(b.Left, r), "condition"); return l || Truth(Eval1(b.Right, r), "condition"); }
            var left = Eval1(b.Left, r); var right = Eval1(b.Right, r);
            switch (b.Op)
            {
                case "==": return Equal(left, right);
                case "!=": return !Equal(left, right);
                case "in":
                    if (right is string hay && left is string needle) return hay.Contains(needle, StringComparison.Ordinal);
                    if (right is System.Collections.IList list) return list.Cast<object?>().Any(e => Equal(left, e));
                    return false;
                case "<" or "<=" or ">" or ">=":
                    if (left is null || right is null) return false;
                    int cmp;
                    if (left is double x && right is double y) cmp = x.CompareTo(y);
                    else if (left is string s && right is string t) cmp = string.CompareOrdinal(s, t);
                    else throw new ExpressionException("type", $"Cannot compare {Kind(left)} with {Kind(right)}.");
                    return b.Op switch { "<" => cmp < 0, "<=" => cmp <= 0, ">" => cmp > 0, _ => cmp >= 0 };
            }
            if (left is null || right is null) return null;
            if (b.Op == "+" && left is string ls && right is string rs)
            {
                if ((long)ls.Length + rs.Length > r.Limits.MaxString) throw new ExpressionException("limit-size", "A text in the expression became too long.");
                return ls + rs;
            }
            if (left is double a && right is double c)
                return b.Op switch
                {
                    "+" => a + c, "-" => a - c, "*" => a * c,
                    "/" => c == 0 ? throw new ExpressionException("divide-by-zero", "Division by zero.") : a / c,
                    "%" => c == 0 ? throw new ExpressionException("divide-by-zero", "Division by zero.") : a % c,
                    _ => throw new ExpressionException("syntax", "Unknown operator.")
                };
            throw new ExpressionException("type", $"\"{b.Op}\" needs two numbers (or two texts for +), not {Kind(left)} and {Kind(right)}.");
        }

        private static string Str(object? v, string fn)
            => v is null ? "" : v as string ?? throw new ExpressionException("type", $"{fn} needs a text, not {Kind(v)}.");

        private static double Num(object? v, string fn)
            => v is double d ? d : throw new ExpressionException("type", $"{fn} needs a number, not {Kind(v)}.");

        public static string NumberText(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);

        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == 'ي' || ch == 'ى') sb.Append('ی');
                else if (ch == 'ك') sb.Append('ک');
                else if (ch is 'ـ' or '‌' or '​' or '‍') continue;
                else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else sb.Append(ch);
            }
            var t = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return t.ToLowerInvariant();
        }

        private static bool TryDate(object? v, out DateTime d)
        {
            d = default;
            if (v is not string s || s.Length < 10) return false;
            return DateTime.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static object? CallFn(Call c, Run r)
        {
            if (c.Name == "if") return Truth(Eval1(c.Args[0], r), "condition") ? Eval1(c.Args[1], r) : Eval1(c.Args[2], r);
            if (c.Name == "coalesce") { foreach (var a in c.Args) { var v = Eval1(a, r); if (v is not null) return v; } return null; }
            var args = c.Args.Select(a => Eval1(a, r)).ToList();
            switch (c.Name)
            {
                case "isEmpty": return args[0] is null || (args[0] is string es && string.IsNullOrWhiteSpace(es)) || (args[0] is System.Collections.IList el && el.Count == 0);
                case "len": return args[0] switch { null => 0d, string ls => (double)ls.Length, System.Collections.IList ll => (double)ll.Count, _ => throw new ExpressionException("type", $"len needs a text or a list, not {Kind(args[0])}.") };
                case "contains":
                    if (args[0] is string h && args[1] is string n) return h.Contains(n, StringComparison.Ordinal);
                    if (args[0] is System.Collections.IList list) return list.Cast<object?>().Any(e => Equal(e, args[1]));
                    return false;
                case "startsWith": return args[0] is string a1 && args[1] is string b1 && a1.StartsWith(b1, StringComparison.Ordinal);
                case "endsWith": return args[0] is string a2 && args[1] is string b2 && a2.EndsWith(b2, StringComparison.Ordinal);
                case "lower": return args[0] is null ? null : Str(args[0], "lower").ToLowerInvariant();
                case "upper": return args[0] is null ? null : Str(args[0], "upper").ToUpperInvariant();
                case "trim": return args[0] is null ? null : Str(args[0], "trim").Trim();
                case "normalize": return args[0] is null ? null : Normalize(Str(args[0], "normalize"));
                case "number":
                    if (args[0] is double) return args[0];
                    if (args[0] is string ns)
                    {
                        var t = Normalize(ns);
                        return t.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(t, @"^-?\d+(\.\d+)?$") ? double.Parse(t, CultureInfo.InvariantCulture) : null;
                    }
                    return null;
                case "text": return args[0] switch { null => "", bool tb => tb ? "true" : "false", double td => NumberText(td), string ts => ts, _ => throw new ExpressionException("type", $"text cannot show {Kind(args[0])}.") };
                case "round":
                    if (args[0] is null) return null;
                    var digits = args.Count > 1 ? (int)Num(args[1], "round") : 0;
                    if (digits is < 0 or > 6) throw new ExpressionException("type", "round takes 0 to 6 digits.");
                    var f = Math.Pow(10, digits); var x = Num(args[0], "round");
                    return Math.Sign(x) * Math.Floor(Math.Abs(x) * f + 0.5) / f;
                case "abs": return args[0] is null ? null : Math.Abs(Num(args[0], "abs"));
                case "min" or "max":
                    var nums = args.Where(a => a is not null).Select(a => Num(a, c.Name)).ToList();
                    return nums.Count == 0 ? null : c.Name == "min" ? nums.Min() : nums.Max();
                case "sum": return args[0] is System.Collections.IList sl ? sl.Cast<object?>().Where(e => e is not null).Sum(e => Num(e, "sum")) : null;
                case "today": return Iso(r.Ctx.Now);
                case "year" or "month" or "day":
                    if (!TryDate(args[0], out var dt)) return null;
                    return c.Name == "year" ? dt.Year : c.Name == "month" ? dt.Month : (double)dt.Day;
                case "addDays": return TryDate(args[0], out var d0) && args[1] is double n1 ? Iso(d0.AddDays(n1)) : null;
                case "diffDays": return TryDate(args[0], out var da) && TryDate(args[1], out var db) ? (da - db).TotalDays : null;
                case "jalaliYear" or "jalaliMonth" or "jalaliDay":
                    if (!TryDate(args[0], out var jd)) return null;
                    var cal = new PersianCalendar();
                    return c.Name == "jalaliYear" ? cal.GetYear(jd) : c.Name == "jalaliMonth" ? cal.GetMonth(jd) : (double)cal.GetDayOfMonth(jd);
            }
            throw new ExpressionException("unknown-function", $"\"{c.Name}\" is not a function.");
        }
    }
}

/// <summary>A checked expression, ready to run against a context any number of times.</summary>
public sealed class CompiledExpression
{
    private readonly ExpressionCompiler.Node _root;
    private readonly ExpressionLimits _limits;

    internal CompiledExpression(string source, ExpressionCompiler.Node root, IReadOnlyCollection<string> roots, ExpressionLimits limits)
    {
        Source = source; _root = root; Roots = roots; _limits = limits;
    }

    public string Source { get; }
    /// <summary>The variable names the expression reads (the first part of <c>values.status</c> is <c>values</c>); used for the references of a definition.</summary>
    public IReadOnlyCollection<string> Roots { get; }

    /// <summary>Runs the expression. Whole numbers come back as <see cref="double"/>; lists as <see cref="List{T}"/>; an expression that breaks a rule or a limit throws <see cref="ExpressionException"/>.</summary>
    public object? Evaluate(ExpressionContext context)
        => ExpressionCompiler.Eval.Eval1(_root, new ExpressionCompiler.Eval.Run(context, _limits));

    /// <summary>For conditions (visible, required, readonly): true, false or nothing (= false); any other result is an error.</summary>
    public bool EvaluateCondition(ExpressionContext context)
    {
        var v = Evaluate(context);
        if (v is null) return false;
        if (v is bool b) return b;
        throw new ExpressionException("type", "A condition must give true or false.");
    }
}
