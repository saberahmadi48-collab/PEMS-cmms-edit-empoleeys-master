# ADR-0008: Component texts are inputs: TranslatableText, formatted values, locale service

- **Status:** Accepted
- **Date:** 2026-10-27
- **Decided by:** Hamid Maleki, project owner and Member A (requirement given on 2026-10-27)
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, by giving the requirement and the go-ahead in the SCRUM-416 session
- **Plan rows:** #386 (this step, SCRUM-416); follow-ups #387 (SCRUM-417), #388 (SCRUM-418), #389 (SCRUM-419); scope notes on #321, #322, #327
- **Sources:** owner requirement of 2026-10-27; ADR-0002, ADR-0007; Instructions rule 5 (Persian first, Jalali dates, Gregorian/UTC storage); code in `PEMS.UI.Abstractions` and `PEMS.UI`

## Context

PEMS is moving to a dynamic BPMS. Form definitions, and later pages and modules, will live in the database and be built by users. The
texts such a form shows (labels, placeholders, help, notes, errors, empty and loading texts) therefore cannot live in component code or in
`.resx` files that are compiled into the application. Today the shared partials read `SharedResource` of `PEMS.Web` (`_PageHeader`,
`_Pagination`), which a component library cannot depend on, and the spike components of ADR-0007 carried English defaults ("Save",
"No records") in their code.

## Decision

1. **Every user-visible text of a PEMS.UI component is an input.** A component contains no literal text and no default phrase. A test
   (`LiteralTextGateTests`) scans the markup of the library and fails when a literal word appears between tags or in `title`, `aria-label`,
   `placeholder`, `alt` or `label` attributes. Symbols such as `*` are not text.
2. **The input type is `TranslatableText`** (`PEMS.UI.Abstractions`): an immutable map from a language slug to a text, serialised as JSON
   `{"fa":"ذخیره","en":"Save"}`. This is the shape a database form row will store.
   - Slugs are BCP 47 style (`fa`, `en`, `fa-ir`), stored lower case and compared case-insensitively; an invalid slug is rejected.
   - The slug `*` holds a language-neutral text (a code, a symbol, a text that is the same everywhere). A plain JSON string is read as
     the neutral text; a plain C# string converts to it. Writing always uses the object form, so the stored shape never drifts.
   - A blank text counts as absent, so "missing" has one meaning.
   - **Fallback chain** (`Resolve`): the current culture, then its parents (`fa-IR` then `fa`), then the default language and its parents,
     then the neutral text, then the first text by slug. A page therefore never shows nothing while any text exists.
   - `MissingLanguages(required)` tells a designer what is still untranslated (the neutral text satisfies every language).
3. **Data is never translated, only formatted.** Values a user entered or the system stored (names, tags, dates, amounts, ids, option
   values) pass through `IPemsLocale.Format`: dates are shown as `yyyy/MM/dd` in the Persian calendar for Persian cultures and as ISO
   `yyyy-MM-dd` otherwise, with `HH:mm` when a time is present; numbers use the culture's separators; digits follow the language (Persian digits ۱۲۳ for Persian, Latin digits 123 for English). This is display only: values users type, post or store are never rewritten (tested). Table rows carry values of any type
   (`object?`), not preformatted strings. A boolean shows the column's `TrueText` / `FalseText`, which are inputs like any other text.
4. **`IPemsLocale` is the one service components ask** (`Text`, `Format`, `Culture`, `DefaultLanguage`, `IsRightToLeft`). The abstraction is in
   `PEMS.UI.Abstractions`, the default in `PEMS.UI` (current UI culture plus `PemsUiOptions`), and the host may register its own (for
   example reading the language from the user profile). Components never look up resources and never format by themselves.
5. **Descriptors are text-typed and JSON round-trippable.** `FieldDescriptor`, `FieldOption`, `ColumnDescriptor`, `ViewDescriptor` and
   `FormDefinition` take `TranslatableText` for label, help, placeholder, option label, header, title, empty text and submit label;
   validation errors in `FormState` are `TranslatableText` too. `PemsJson.Options` is the one JSON configuration (camel case, enums as
   strings, Persian kept readable, not `\uXXXX`).
6. **The request-free renderer accepts a culture**, so a designer preview, an e-mail or a PDF renders in any language without a request.
7. **MVC pages keep working unchanged.** The `<pems-field asp-for>` facade passes the already-localised MVC strings as neutral texts.
8. **No resource files in PEMS.UI.** The host passes texts in. For `PEMS.Web` pages that still use `SharedResource`, a thin adapter in
   `PEMS.Web` fills the inputs (task #388). This replaces the idea, noted in SCRUM-29, of giving `PEMS.UI` its own resources.

### Example (what a form row will hold)

```json
{ "id": "instrument-form", "title": { "fa": "ثبت ابزار", "en": "Register instrument" }, "action": "/instruments",
  "submitLabel": { "fa": "ذخیره", "en": "Save" },
  "fields": [ { "name": "tag", "label": { "fa": "تگ", "en": "Tag" }, "kind": "text", "required": true,
                "helpText": { "fa": "شناسه یکتا", "en": "Unique identifier" }, "placeholder": { "*": "PT-0001" } } ] }
```

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| `IStringLocalizer` and `.resx` keys in the library | Texts are compiled in; a user cannot add a form or a language without a deploy; contradicts the dynamic BPMS. Still right for static application chrome outside PEMS.UI. |
| A separate translation table of (key, language, text) rows | Normalised but needs a join and a key per text for every render; one JSON value per text keeps a form row self-contained, cacheable and exportable as one document, as the owner asked. |
| One column per language | Adding a language changes the schema; the number of columns is unbounded. |
| ICU MessageFormat / gettext | Needed only for texts with parameters and plurals; not required by the forms in scope. Can be added later as a richer text type without changing the JSON of plain texts. |

## Consequences

- Good: forms, labels and errors can be created and translated in the database without a deploy; components stay pure and testable
  (a render with a fixed culture gives a fixed result); the same definition renders in the page, in a preview and in a PDF.
- Cost: every caller must supply texts (a `string` converts implicitly, so existing call sites keep compiling); a missing translation is a
  data problem, so the designers need the completeness report (#389).
- The two partials moved in SCRUM-29 (`_KpiCard`, `_PemsLoadingOverlay`) still contain their existing text; the gate skips them until #388.
- Not covered by this step: loading from and saving to the database (#387), the `PEMS.Web` settings for the default language, calendar and
  digits (read from the env files when the first page uses a component, #388), texts with parameters and plurals, time-zone conversion of
  stored UTC values (backlog #390, callers pass values already converted).

## Decisions on the defaults (owner, 2026-10-27)

1. **Persian digits for Persian cultures: yes** (`PemsUiOptions.UseNativeDigits` is on by default; English and other cultures keep Latin digits).
2. **Dates: Persian calendar for Persian cultures, ISO `yyyy-MM-dd` for everything else.** No other culture-specific date format for now.
3. **Default language: `fa`**, set by the host.
4. **Time zone of displayed date-times: not now.** Callers pass values already converted; a policy is a lowest-priority backlog task (#390, SCRUM-420).

## Finding from the data-entry check (SCRUM-421), fixed

The check that display formatting leaves entered data alone also showed two existing problems on Persian pages, independent of PEMS.UI: a decimal with a dot (`12.5`) from a number input was rejected because the `fa` culture's decimal separator is `٫`, and typed Persian digits were rejected. Dates were safe because `PEMS.Web` keeps the Gregorian calendar on `fa`. Fixed by one model binder (`PEMS.UI.Binding.PemsNumericModelBinder`, registered once in `PEMS.Web` with `AddPemsInputBinding`): input is normalised (Persian and Arabic-Indic digits to Latin, `٫` to a dot, the thousands mark dropped), numbers are parsed with the invariant culture and dates as ISO first, then with the request culture; empty values, nullable types and MVC's error messages are unchanged. Input and display stay separate: the binder only reads, `IPemsLocale` only displays.
