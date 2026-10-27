# Account search input — 2026-10-26

Account search previously bound the visible input to server-rendered text and sent an input event for every keystroke. A delayed Blazor render could replace a newer draft or disturb its caret while typing or deleting. Opening and filtering the dropdown also required server interaction.

The shared `AccountSearchSelect` now keeps query text, filtering and dropdown interaction in the browser. Its visible input has no server-controlled value attribute. Editing a selected account sends one invalidation; choosing a result sends one selection. Server acknowledgements include revisions so that a late clear cannot erase a newer choice. Selection callbacks are serialized and the server checks that the account is in the current allowed list, active and selectable. Disabled selectors reject changes.

The create-hawala modal now also clears its bound account ID and generated outgoing number when the user removes a selection. Its existing positive-selection callback remains responsible for dependent data. The shared control is used at 27 account selection sites, including hawalas, transfers, deposits/withdrawals, exchanges, expenses and reports. Existing presentation, clear options, account-code visibility and balance summaries are retained. Keyboard arrows, Enter, Escape and touch selection are supported; account text is added with `textContent`.

## Verification

- 38 component/lifecycle tests passed, including invalid, archived, disabled and out-of-order selections, serialized callbacks, and clearing the bound hawala account.
- Standalone Edge checks passed for Persian typing, deletion, replacement, caret preservation, late acknowledgements, keyboard selection, no per-key server updates and safe rendering of account names containing markup.
- Live Edge checks used an isolated SQL Server database and simulated 300ms WebSocket round trips. All five account fields across create hawala, transfer and deposit/withdrawal forms passed selection, rapid Persian editing/deletion, late server echo and caret checks. Account lists ranged from 10 to 1,012 options.
- Mobile dropdown geometry was checked at 375px. No financial form was submitted during browser verification.
- Release publish succeeded. Raw evidence: [browser results](account-search-20261026/results.json), [mobile screenshot](account-search-20261026/mobile.png).

## Delivery

Complete release output: `publish/account-search-20261026`. It includes earlier responsive, receipt and operation-menu fixes. IIS was not modified. Deploy all output files, including the new fingerprinted account-search module, while preserving production configuration, `App_Data` and uploaded files. No migration or database command is required for this change.
