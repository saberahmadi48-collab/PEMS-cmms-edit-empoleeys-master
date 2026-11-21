---
name: pems-shared-modal
description: A PEMS list or page that opens the same modal form for many rows: one pems-modal filled from the opening button (data-fill-*), with its test registration.
---

# PEMS shared modal (guideline 02 registry row "Shared modal"; script `pems-modal-fill.js`, SCRUM-500)

Never one modal per row (`id="editModal_@row.Id"` inside a loop). One `<pems-modal id="xModal">` per view; the opening button carries the row.

1. **Opener:** `<pems-button ... data-bs-toggle="modal" data-bs-target="#xModal" data-fill-name="@row.Name" data-fill-id="@row.Id" ...>`. Every `data-fill-<name>` fills the form field `name="<name>"` (case-insensitive) when the modal opens; the form is reset first, so nothing leaks from the previous row.
2. **Text parts** (titles, hints, read-only values) are `<span data-fill-text="<name>">` or any element with that attribute; filled with `textContent`, never markup.
3. **A form action or file filter that depends on the row:** `data-fill-formaction="@Url.Action(...)"` sets the form's `action`; `data-fill-accept=".xlsx"` sets the file input's `accept` (ImportCenter).
4. **Fields that depend on the row's own data** (an option list that differs per row, inactive options) are the exception: keep that modal per row and say why in the view comment (Employee edit).
5. **Register it:** add `[InlineData("Views/<Area>/<View>.cshtml", "xModal")]` to the Theory `A_list_has_one_shared_modal_and_its_openers_carry_the_row_values` in `PEMS.UI.Tests/ConfirmScriptTests.cs` (it checks exactly one modal, no id with a row suffix, and `data-bs-target="#xModal"` followed by `data-fill-`), and extend the registry row in guideline 02.
6. Verify: `dotnet test PEMS.UI.Tests`; the browser check (open for two rows: the second shows its own values, the first row's values are gone) is a QA line.
