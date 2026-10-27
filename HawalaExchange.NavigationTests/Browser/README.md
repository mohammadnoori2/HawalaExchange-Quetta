# Responsive browser checks

Run against an already running application using an isolated, seeded test database. Do not point these checks at production: they log in and open business forms, although they do not submit transactions.

Install dependencies with `npm ci` in this directory. Microsoft Edge must be installed; Playwright uses `channel: 'msedge'`.

Set `HAWALA_UI_CONTEXT` to a local JSON file containing a test `user`, `password`, and seeded numeric IDs: `accountId`, `correspondentId`, `customerId`, `branchId`, `currencyId`, `paymentLocationId`. Keep that file outside the repository. Set `HAWALA_UI_ORIGIN` to the test application origin; the default is `http://127.0.0.1:5112`.

Optional configuration:

- `HAWALA_UI_OUTPUT`: directory for JSON results and screenshots (defaults to a temporary directory).
- `HAWALA_UI_CHART_SCRIPT`: local Chart.js 4.4.7 UMD file for deterministic charts when external assets are blocked.

Run `npm test`. The route sweep discovers exchange routes from Razor page declarations, substitutes the seeded IDs, and checks five viewport widths. The interaction suite checks login fields, mobile navigation, create-form layouts, landscape, reduced motion and enlarged text. Platform/tenant administration and routes requiring an accounting period or import preview batch are excluded.

Remove the temporary credentials and dispose of the isolated database when finished.

`payment-location-edit.cjs` **saves** imported test hawala 98000002 in the location-review fixture. It requires a disposable database, `HAWALA_LOCATION_DISPOSABLE=true`, and context fields `newLocationId`/`newAccountId`. The target location is `lashkargah`, assigned to a correspondent with a different name. The script checks account selection, validation, saving and reloading. Never run it against production.

`account-search.cjs` checks browser-owned typing, clearing, caret preservation, stale acknowledgements, selection and safe text rendering without a server. `account-search-live.cjs` uses an isolated seeded login context and 300ms simulated WebSocket round trips to exercise the account fields in create hawala, transfer and deposit/withdrawal dialogs. It selects and clears fields but never submits those financial forms. Screenshots and JSON results go to `HAWALA_UI_OUTPUT`.

`action-menu.cjs` checks local menu behavior without a server, including stalled callbacks and late refreshes. `action-menu-live.cjs` uses the isolated login context to check accounts, customers and correspondents with 300ms simulated WebSocket round trips. Pass `before` and set `HAWALA_UI_ORIGIN` to the baseline application to capture its first-open timings. Neither menu script submits a financial operation.

`hawala-receipt.cjs` is a separate financial workflow test: it **executes** pending test hawala 26001. It requires the disposable receipt-review dataset (customer `TEST-ID-2601` and cash account `Receipt Test Cash`) and explicit `HAWALA_RECEIPT_DISPOSABLE=true`. It verifies selected customer details, manual printing, one automatic print after successful payment, operation with popups blocked and print-frame cleanup. Native printer output is intercepted; no physical receipt is printed.
