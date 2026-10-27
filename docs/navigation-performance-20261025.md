# Navigation performance — 2026-10-25

Business pages now share an Interactive Server router. `HeadOutlet` uses the same render mode, prerendering is disabled for business routes, and account endpoints retain static rendering through their existing `ExcludeFromInteractiveRouting` metadata. Login and cookie-changing endpoints retain their HTTP request lifecycle.

The accounts list projects owner names in the account query, removing the customer, correspondent, and currency loads from initial page entry. Those form options load when Create/Edit opens. Correspondent currencies load when a form opens. Hawala payment account/customer lists load on each payment-form opening, rather than on page entry. Lookup failures leave the form closed and permit retry. Repeated clicks do not start overlapping form loads.

DTO list reads use no tracking so a retained circuit does not return old tracked values or overwrite unsaved form edits. Currency reference data uses the existing scoped, tenant-aware cache. Financial balances and transaction results were not cached. Existing account/correspondent table paging and filter semantics remain unchanged; this change does not introduce server-side paging for those two lists.

## Validation

- Release publish succeeded.
- 11 navigation lifecycle tests passed, including create/edit options, payment refresh, failed lookups, repeated clicks, and static authentication endpoints.
- 56 related SQL/security/accounting regression tests passed, including tenant isolation and preservation of unsaved edits.
- The SQL test fixture now seeds platform roles required by its existing authentication tests, matching application bootstrap.
- Browser checks passed for login, navigation, server-side sort interactions, and opening the create forms of all three pages. No forms were submitted.
- Twelve rapid navigations followed by opening the account form passed without page errors.

## Browser comparison

The baseline was the previously published application from `C:\inetpub\wwwroot`; the candidate was the new Release publish. Both ran on separate loopback ports against the same disposable test database, with 1,000 additional accounts, 600 customers, and 200 correspondents. The hawala table was empty. Three rounds of Accounts → Correspondents → Hawalas → Accounts were measured. External jsDelivr requests were blocked equally for both applications to isolate navigation from CDN variability.

A browser `MutationObserver` measured from initiating the menu link until the destination heading and table existed and the loading indicator was absent. These numbers measure DOM/data readiness, not full visual paint or public Internet latency. Server-driven sorting was also exercised after each navigation to verify interactivity; Playwright's polling/actionability time is not included in DOM readiness.

| Destination | Baseline median (ms) | Candidate median (ms) | Candidate range (ms) |
| --- | ---: | ---: | ---: |
| Accounts | 398 | 20 | 13–22 |
| Correspondents | 395 | 18 | 16–402 |
| Hawalas | 388 | 21 | 20–400 |

The candidate eliminated the per-navigation page HTTP request (one → zero). There were no new WebSocket connections during the measured navigations. The first visits to Correspondents and Hawalas still had roughly 400 ms outliers; warm repeated navigation produced the lower medians. This is not a promise of the same percentage improvement on the live domain or with a large hawala dataset.

Raw samples: [navigation-benchmark-20261025.json](navigation-benchmark-20261025.json).

## IIS rollout

Prepared output: `publish/navigation-optimized-20261025`. The live IIS files were not replaced during development.

From an Administrator PowerShell at the repository root, run:

```powershell
.\tools\Deploy-NavigationOptimization.ps1
```

The script validates source and target paths, backs up the two changed assemblies and their symbols outside the web root, stops only the matching application pool, replaces the files, starts the pool, and checks the login page for HTTP 200. If deployment or the health check fails after stopping the pool, it attempts rollback. Configuration, uploaded files, and the existing data-protection keys are preserved. Use `-WhatIf` to preview its action.
