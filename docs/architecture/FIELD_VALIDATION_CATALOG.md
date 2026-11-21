# Field validation catalog and error contract

Status: **v1, 2026-11-19** (SCRUM-372 catalog, SCRUM-373 engine, SCRUM-380 error contract; plan rows 355, 356, 363). Owner: PM.
Code: `PEMS.Application/Validation/FieldValidator.cs` (constraints, codes, the validator), `PEMS.Web/Definitions/ValidationMessages.cs` (texts, mapping to the API error list), `PEMS.Web/Definitions/FormEngine.cs` (forms use the same validator).
Test: `PEMS.Tests/Validation/FieldValidatorTests.cs` fails when this table, the code constants and the message texts disagree.

## 1. Decision (ADR-validation-1)

One validator, one list of stable constraint codes, one error shape.

- **The server is the authority.** A direct API call, an import row and a form post are checked by the same `FieldValidator`; the browser repeats the checks only to give feedback (OWASP Input Validation Cheat Sheet: validate on the server, syntactic and semantic, allow-list over deny-list).
- **A constraint is data** of the field (`FieldConstraints`), built from a form field, an entity column or an import column. A new constraint is a new code, a new case in the dispatch and a new row below; nothing else changes.
- **Hard and advisory.** A field marked `Advisory` reports its violations with `advisory: true`; they do not make the result invalid and are not part of the 422 error list.
- **Create and update.** Create requires every required field. Update (partial) checks only the keys that are present; an omitted key is never "missing" and never clears a stored value. Whitespace-only counts as empty.
- **All violations, capped.** The validator returns every violation, cut at `Validation:MaxErrors` (default 200, the ASP.NET Core default); the result says when it was cut.
- **Validation never replaces** authorisation, parameterised queries, output encoding or HTML sanitisation (rich text still goes through `FormValueSanitizer`).
- **Persian and Arabic-Indic digits** are read as digits and thousands separators are ignored in numbers, so the same number typed either way is the same number.
- **A pattern that cannot be decided within 250 ms is a refusal**, never an allow (protection against catastrophic backtracking).

Sources: OWASP Input Validation Cheat Sheet; Microsoft Learn, ASP.NET Core model validation (default maximum of 200 errors); WCAG 2.2 criteria 3.3.1 (error identification) and 3.3.3 (error suggestion); RFC 9457 (problem details for HTTP APIs).

## 2. Constraints by field type

Codes are stable: a client, a test or a translation may depend on them. Message keys are `Val_` plus the code in PascalCase. "Where" says who enforces it besides the validator.

| Code | Applies to | Parameters | Hard / advisory | Default | Column rule (where) |
|---|---|---|---|---|---|
| `required` | every type | - | hard | not required | NOT NULL on the column |
| `max-length` | text, text area, rich text, password, select, radio, tags | `max` | hard | none | column length = the constraint (nvarchar(n)) |
| `min-length` | text, text area, password | `min` | hard | none | - |
| `not-a-number` | number, money | - | hard | always | decimal column |
| `min` | number, money | `min` | hard | none | - |
| `max` | number, money | `max` | hard | none | - |
| `scale` | number, money | `scale` | hard | none | column scale = the constraint (decimal(p,s)) |
| `not-a-date` | date | - | hard | `yyyy-MM-dd` | date column |
| `date-before` | date | `min` (day) | hard | none | - |
| `date-after` | date | `max` (day) | hard | none | - |
| `not-a-time` | time | - | hard | `hh:mm` or `hh:mm:ss` | time column |
| `not-allowed` | select, radio | - | hard | when options are given | FK or check constraint |
| `pattern` | any text-like type | - | hard | none | - |
| `min-items` | tags | `min` | hard | none | - |
| `max-items` | tags | `max` | hard | none | - |
| `file-too-large` | file | `max` (MB) | hard | upload limit setting | enforced by the upload step (SCRUM-596) with the same code |
| `file-type` | file | - | hard | allow-list of extensions | upload step (SCRUM-596) |
| `file-count` | file | `max` | hard | one file | upload step (SCRUM-596) |

Types without a constraint of their own beyond `required`: checkbox and switch (an unticked box is "false", which counts as empty for `required`), rich text (sanitised, not parsed), JSON and computed fields (computed values are never posted).
Lookup existence ("active only") is checked by the data source of the field (`optionsFrom`), not by the validator, because only the data source knows the rows.

## 3. Error contract (RFC 9457)

An invalid API call, form post or import row returns the same list. In the API it is the body of the 422 response `application/problem+json`; `type` is `urn:pems:problem:validation`:

```json
{
  "type": "urn:pems:problem:validation",
  "title": "The request is not valid.",
  "status": 422,
  "errors": [
    { "pointer": "/quantity", "code": "min", "messageKey": "Val_Min", "message": "The smallest allowed value is 1.", "params": { "min": 1 } }
  ]
}
```

- `pointer` is a JSON Pointer to the field (`/quantity`, `/items/2/quantity` for nested data).
- `code` is from section 2 (or a rule code of a definition, such as `unknown-property`); `messageKey` and `params` let a client translate; `message` is already in the user's language.
- No stack trace, SQL or internal name is ever returned. The list is capped by `Validation:MaxErrors`.
- A form shows the first violation of a field next to the field (the definition may override the text per code with `messages`); an import report lists the same errors per row.

## 4. Settings

`Validation:MaxErrors` (default 200) in every `env/*.env`.
