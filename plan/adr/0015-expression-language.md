# ADR-0015: The expression language of the definitions

- **Status:** Accepted
- **Date:** 2026-11-16
- **Decided by:** Hamid Maleki (owner): conditions, validation and computed values are data in the definitions, never code (2026-11-16)
- **Plan rows:** #15 (SCRUM-39); used by #437, #438, the form engine (#578) and the page engine; builds on ADR-0011 and ADR-0014
- **Sources:** library check below; [NCalc](https://github.com/ncalc/ncalc) (MIT), [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) (MIT)

## Context

`visible`, `required`, `readonly`, validation conditions and computed values must live in the JSON definitions (ADR-0014) and give **the same result on the server and in the browser**; a hostile expression must be stopped; texts and dates must be Persian-aware (Jalali, Persian digits and letters); the product runs offline.

## Decision

A small language of our own, implemented twice with one shared conformance file:

- **C#:** `PEMS.Application/Expressions/Expression.cs` (`ExpressionCompiler`, `CompiledExpression`, `ExpressionContext`).
- **Browser:** `PEMS.UI/wwwroot/pems-expr.js` (`PemsExpr.compile`).
- **One answer:** `tools/js-tests/expression-cases.json` (105 cases) is run by `ExpressionEngineTests` and by `tools/js-tests/pems-expr.test.js`; a change to the language starts with a new case in that file.

**Grammar and meaning**
- Values: `null`, `true/false`, numbers (double), texts (`'a'` or `"a"`, escapes `\\n \\t \\'`), lists `[1,2]`, objects (read only). Names with dots and indexes: `values.items[0].price`; a missing name or index gives `null` (null-safe).
- Operators, low to high: `?:`, `or`/`||`, `and`/`&&`, `not`/`!`, `== != < <= > >= in`, `+ -`, `* / %`, unary `-`. `and`/`or`/`?:` short-circuit; a condition must be `true`, `false` or `null` (= false), anything else is a `type` error.
- `==` never converts types (`1 == '1'` is false); ordering compares two numbers or two texts (ordinal); `null` in arithmetic gives `null`; `+` also joins two texts; `in` tests a list or a substring.
- Functions (fixed list, checked at compile time): `isEmpty len contains startsWith endsWith lower upper trim normalize number text round abs min max sum coalesce if today year month day addDays diffDays jalaliYear jalaliMonth jalaliDay`. `normalize` folds Arabic letters (ي ك), removes tatweel and zero-width marks, turns Persian and Arabic digits into Latin ones and collapses spaces; `number('۱۲۳')` is 123. Dates are ISO texts (`2026-03-20`); Jalali parts use the .NET `PersianCalendar` and the browser `Intl` persian calendar (proven equal on 1300 to 1500 by SCRUM-225).
- `today()` reads the clock of the context, so a result can be repeated and tested.
- **Variables** are the roots the place of use offers (a page: `user request values item`); an unknown variable is an error **when the definition is loaded**, not at run time. A page instance's `visible` is checked this way by `PageDefinitionKind`.

**Safety** (no statements, no loops, no assignment, no recursion, no user functions, no calls into .NET or the page): at most 2000 characters, 500 parts, depth 32, 10,000 evaluation steps, texts of 10,000 characters, lists of 1,000; errors have stable codes (`syntax unknown-variable unknown-function arity type divide-by-zero limit-length limit-nodes limit-depth limit-steps limit-size`).

## Alternatives considered

| Option | Why not |
|---|---|
| NCalc (MIT) | Server only; no browser twin, so "same result on client and server" would need a second library with different rules; unknown variables are found at run time. |
| DynamicExpresso (MIT) | Compiles C#: it can reach .NET types unless every type is locked out; no browser twin; not a safe base for stored expressions. |
| A JavaScript sandbox (Jint) on the server | Heavy, a second language on the server, still no static check of variables at load. |
| Expressions as JSON trees only | Not editable by hand; the text form is parsed into the same tree anyway. |

## Consequences

- Good: one language for visible, required, readonly, validation and computed values; checked at load; safe by construction; both engines tested by one file.
- Cost: two small implementations to keep equal (the shared cases catch drift); no regular expressions and no loops on purpose.
- Follow-up: hydrators (data sources, #437) and the page host (#438) evaluate `visible`; the form engine (#578) uses it for validation rules, required and readonly.

## Amendments

None yet.
