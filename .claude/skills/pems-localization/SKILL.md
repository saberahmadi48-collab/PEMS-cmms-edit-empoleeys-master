---
name: pems-localization
description: Add or change a user-visible text in PEMS (English and Persian resource keys, component texts, scripts) without failing the key coverage test.
---

# PEMS localization (guideline 02; tests: `LocalizationKeyCoverageTests`, `ShellAccessibilityTests`)

Every user-visible text is a resource key in **both** `PEMS.Web/Resources/SharedResource.resx` (English) and `SharedResource.fa.resx` (Persian). A constant `Localizer["Key"]` in a view or controller without a Persian entry fails `LocalizationKeyCoverageTests` (a one-second test; the whole Web namespace is not needed to catch it).

1. Use `@Localizer["Key"]` in views; a key may contain spaces and be the English text itself (`Localizer["New Project"]`). Never a hard-coded sentence in markup or JS.
2. Add the key to **both** files, before `</root>`: `<data name="Key" xml:space="preserve"><value>text</value></data>`. English value = the key text or a clearer sentence; Persian value is real Persian, not a copy. Keep the file's BOM and line endings (CRLF): edit with a script that preserves them.
3. Components take texts as inputs: pass `Localizer[...]` as attributes (`texts="@ListPageTexts.From(Localizer)"`); scripts get them from `data-*` attributes on `<body>` or the component (`data-pems-error-*`, `data-pems-confirm-*`), never literals.
4. A dynamic key (`Localizer["Prefix_" + x]`) is not checked by the test: add every concrete key it can produce.
5. Run `python tools/test-gates/gates.py`: it adds `LocalizationKeyCoverageTests` for any view, resource or Localizer change. A missing Persian key is a failure to fix, never to list as not run.
6. Direction: use logical CSS properties and `dir="ltr"` only for codes, numbers and references (`<span dir="ltr">`), so fa (RTL) and en (LTR) both read correctly; the browser check of both languages is a QA line.
