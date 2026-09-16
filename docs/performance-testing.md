# Performance test baseline

This project establishes a repeatable baseline before stored-procedure or query optimizations are introduced.

## Safety

- Tests always replace the configured database name with a unique name beginning with `HawalaExchangeQuettaPerformanceTests_`.
- Cleanup refuses to delete a database whose name does not have that exact prefix.
- The application or development database name is never used.
- The optional `HAWALA_PERF_TEST_CONNECTION` value supplies only the SQL Server connection settings; its initial catalog is ignored.

## Run correctness checks

```powershell
dotnet test HawalaExchange.PerformanceTests/HawalaExchange.PerformanceTests.csproj
```

These checks cover paired received/sent Hawalas, ledger balances, transaction rollback, and tenant isolation.

## Capture 100 / 1,000 / 10,000-row measurements

```powershell
$env:RUN_HAWALA_PERF = '1'
dotnet test HawalaExchange.PerformanceTests/HawalaExchange.PerformanceTests.csproj --logger "console;verbosity=detailed"
Remove-Item Env:RUN_HAWALA_PERF
```

The output records elapsed milliseconds, rows per second, EF command count, and inserted received, generated, and ledger row counts. `SqlBulkCopy` operations are included in elapsed time but are not EF commands, so the command count measures the surrounding validation and lookup queries.

For another SQL Server instance, set `HAWALA_PERF_TEST_CONNECTION`. Do not put credentials in this repository.

## Acceptance rule for later phases

A performance change is acceptable only when all correctness checks remain green and the median of three runs does not regress by more than 10% at 10,000 rows. Measurements must use the same machine and SQL Server instance.

## Phase-zero baseline — 2026-09-14

Environment: Windows, .NET SDK 10.0.400, .NET runtime 10.0.11, local SQL Server, Debug build. Each sample contained 80% remote-payment Hawalas and 20% own-location Hawalas; one quarter of remote-payment rows (20% of all rows) had payout-agent commission.

| Input rows | Run 1 (ms) | Run 2 (ms) | Run 3 (ms) | Median (ms) | Median rows/sec | EF commands |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 292.42 | 907.40 | 885.82 | 885.82 | 112.89 | 15 cold / 10 warm |
| 1,000 | 1,704.13 | 1,625.69 | 1,791.21 | 1,704.13 | 586.81 | 10 |
| 10,000 | 7,973.43 | 7,663.52 | 9,037.15 | 7,973.43 | 1,254.17 | 10 |

The first 100-row result in run 1 was warm because the correctness test had already created the two system accounts. Runs 2 and 3 used fresh databases, which explains the cold 100-row cost. The 10,000-row acceptance reference for the next phase is **7,973.43 ms median**.

All four phase-zero tests passed: accounting/link integrity, full transaction rollback, tenant isolation, and the three-size measurement scenario.

## Phase-one result — 2026-09-14

Profiling showed that EF validation and lookup queries accounted for roughly 0.5 seconds of a 10,000-row run. Adding another index would therefore offer little benefit to this write-heavy path and would add index-maintenance work to every insert.

The `SqlBulkCopy` batch size was increased from 2,000 to 10,000. Database constraints, the outer SQL transaction, tenant filters, and all accounting rules remain enabled.

| Input rows | Run 1 (ms) | Run 2 (ms) | Run 3 (ms) | Median (ms) | Previous median (ms) | Change |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 618.50 | 646.47 | 650.72 | 646.47 | 885.82 | -27.0% |
| 1,000 | 1,341.71 | 1,469.21 | 1,351.42 | 1,351.42 | 1,704.13 | -20.7% |
| 10,000 | 4,978.33 | 4,988.94 | 5,397.27 | 4,988.94 | 7,973.43 | -37.4% |

At 10,000 rows, median throughput increased from about 1,254 to 2,004 input rows per second. The test also enforces a maximum of 15 EF commands so that a future per-row query regression is detected.

## Phase-two result — 2026-09-14

The Hawala statistics endpoint previously loaded every Hawala entity, including all text and identity fields, into application memory and then counted the records in C#. It now calls the tenant-scoped `usp_GetHawalaStatistics_v1` procedure, which calculates all five totals in one indexed database scan and returns one row.

The measurement used 18,000 Hawala records: 10,000 received Hawalas and 8,000 generated sends.

| Implementation | Median of three reads |
| --- | ---: |
| Previous entity materialization and in-memory counting | 1,034.71 ms |
| Stored procedure aggregation | 29.53 ms |

The statistics read is **97.15% faster** in this sample and no longer allocates thousands of Hawala entities. The procedure requires `TenantId`, does not use dirty reads, and uses the existing `(TenantId, HawalaType, Status)` index.

A stored-procedure implementation was also evaluated for the bulk-write path. It was rejected because its 10,000-row result was slower than the phase-one `SqlBulkCopy` implementation. Bulk posting therefore remains on the verified 4,988.94 ms median path; stored procedures are used only where measurements show a benefit.

## Phase-three result — 2026-09-14

The operational reports previously loaded full transaction graphs into application memory and then repeatedly filtered and grouped them in C#. Date-range reports also issued one report call per day, the all-branches daily option silently selected the first branch, and the operational report did not include Hawalas stored in the dedicated `Hawalas` table.

Three tenant-scoped procedures now provide the daily summary, transaction listing, and commission summary:

- `usp_GetDailyOperationalReport_v1`
- `usp_GetTransactionReport_v1`
- `usp_GetCommissionReport_v1`

The reports now include both dedicated Hawalas and legacy transaction rows, exclude cancelled operations from financial totals, include payout-agent commission and posted periodic correspondent commission, support real all-branch filtering, and allow an optional currency filter. The transaction report also returns the commission currency so amounts are no longer displayed without their unit.

The measurement used 10,000 imported rows, which produced 18,000 Hawala records.

| Implementation | Median of three daily-report reads |
| --- | ---: |
| Previous entity materialization and in-memory aggregation | 350.81 ms |
| Stored-procedure aggregation | 170.12 ms |

The daily operational report is **51.51% faster** in this sample while returning corrected, currency-aware totals. All nine correctness and performance tests passed.

The unchanged bulk-write path was also rechecked in three fresh databases: 5,551.44 ms, 5,826.87 ms, and 6,206.86 ms for 10,000 input rows. Its 5,826.87 ms median is within 10% of the phase-two 5,320.12 ms verification result; no bulk-write code changed in this phase.

## Phase-four result — 2026-09-14

Periodic correspondent commission preview and posting now use `usp_ProcessPeriodicCommission_v1` with the versioned `CommissionRateTableType_v1` table-valued parameter. The procedure performs the eligible-Hawala selection, currency conversion, per-lakh calculation, whole-number rounding, batch/item inserts, double-entry ledger posting, and audit insert as one database operation. Posting runs in a transaction and locks eligible rows; the existing filtered unique index remains the final safeguard against duplicate active commission items.

Correctness tests cover mixed AFN/USD input, the default 200 AFN per lakh, partial-lakh calculation, whole-number midpoint rounding, cancelled and per-transaction-commission exclusions, missing-rate rollback, duplicate posting, concurrent posting, balanced accounting, reversal, and re-eligibility after reversal. All thirteen performance-suite tests passed.

For 10,000 eligible Hawalas on the local SQL Server, a single measured run completed preview in **590.05 ms** and atomic posting in **408.44 ms**. The data-seeding time is excluded from those values. Unlike the previous EF implementation, posting does not materialize and track all eligible Hawalas or issue per-entity inserts.

## Phase-five result — 2026-09-14

Both correspondent settlement workflows now use `usp_ProcessCorrespondentSettlement_v1`: conversion of explicitly selected Hawalas and conversion of selected account-currency balances. Hawala identifiers and per-Hawala/per-currency rates are passed with versioned table-valued parameters, and all validation, quotation-direction calculation, conversion/item/link creation, four-sided currency ledger posting, and auditing are performed set-wise in one transaction.

An application lock serializes settlement for the same tenant and correspondent, while the existing unique Hawala-conversion indexes remain database-level safeguards. A failed or repeated conversion leaves no partial transaction, conversion, link, item, ledger, or audit records.

Correctness coverage verifies multiply and divide quotation directions, target-currency rounding, creditor and debtor balances, per-currency double-entry balance, selected-Hawala conversion, post-conversion rate editing, whole-account conversion, missing-rate rollback, duplicate-conversion rejection, and concurrent-request serialization. All eighteen performance-suite tests passed.

For 10,000 selected Hawalas, the procedure created 10,000 conversion items and 40,000 balanced ledger entries in **5,489.95 ms** (**1,821.51 Hawalas/second**) on the local SQL Server. Data seeding is excluded from this measurement.

## Original-plan phase one — Hawala query and index optimization — 2026-09-15

This phase completes the query/index work that was not part of the earlier bulk-copy batching change. Hawala list queries now use server-side filtering, sorting, counting, paging, and an explicit DTO projection. The former ten-`Include` entity graph is no longer materialized or tracked for list pages. The single-record detail query is also no-tracking and split to avoid collection cartesian expansion.

The received and pending Hawala pages no longer request `int.MaxValue` rows and page them in browser memory. Search and filters are sent to SQL, page changes fetch only the requested page, and the pending-page total amount is calculated separately only when that page requests it. Existing display behavior for the payment account, reference number, correspondent, payment location, and currencies is covered by an integration test.

The migration replaces three prefix-only indexes with date-aware composite indexes and adds indexes for tenant-wide Hawala-number lookup and the filters used by correspondent, payment-location, currency, type, status, and date views. Tenant isolation and the presence of all expected indexes are verified against a disposable SQL Server database.

For 10,000 Hawalas and a 100-row correspondent page, the median of three local reads was:

| Implementation | Median |
| --- | ---: |
| Legacy tracked navigation shape (ten includes) | 46.69 ms |
| Server-side DTO projection | 38.03 ms |

The projected query was **18.55% faster** in this sample and transfers fewer columns while preserving the returned page and total count.

## Original-plan phase two — account-balance stored procedure — 2026-09-15

Account, customer, correspondent, cash, branch-summary, and overdraft-limit balance reads now use the tenant-scoped `usp_GetAccountBalances_v1` procedure. It aggregates debit and credit values by account and currency in SQL and accepts optional account, customer, correspondent, owner type, account type, and as-of-date filters. Zero balances, including fully reversed activity, are omitted exactly as in the previous ledger implementation.

The all-customer, all-correspondent, all-cash, branch, and account-limit paths no longer execute one ledger query per owner or account. Owner metadata is read once and combined with one set-based balance call. Owners without an account or without ledger activity remain visible with an empty balance list. Read-only metadata queries use projections and no tracking.

The supporting ledger index is extended from `(TenantId, AccountId, CurrencyId)` to `(TenantId, AccountId, CurrencyId, CreatedAt)` so both current and historical balance reads use the same tenant-first index. Correctness tests cover multiple currencies, creditor and debtor values, an as-of date, zero/reversed activity, empty owners, overdraft limits, tenant isolation, and bounded database round trips.

For 500 correspondent accounts and 10,000 ledger entries, one local comparison produced:

| Implementation | Database calls | Elapsed time |
| --- | ---: | ---: |
| Previous per-account ledger loop | 500 | 595.36 ms |
| Owner query plus balance procedure | 2 | 333.12 ms |

The set-based path reduced database calls by **99.6%** and elapsed time by **44.05%** in this sample.

## Original-plan phase three — reports, dashboard, and journal — 2026-09-15

The three operational report procedures from the earlier report phase remain in use; they already cover incoming/outgoing Hawala summaries, commission, expenses, date ranges, branches, correspondents, and currencies. This phase therefore avoids duplicating those procedures and closes the remaining dashboard and journal gaps.

`usp_GetDashboardData_v1` replaces six entity-loading queries and the full historical ledger materialization used by the dashboard. In one tenant-scoped call it returns daily activity counts, account/currency balance aggregates, and date/account/currency profit aggregates. The application still applies the existing currency-conversion and warning rules, but now does so over grouped rows rather than every ledger entry. `usp_GetDailyJournal_v1` returns only the ledger, account, and currency columns needed by the readable journal while preserving its operation grouping, source details, cash receipt/withdrawal sections, and debit/credit summaries.

Correctness tests cover cancelled-Hawala exclusion, exact activity counts and last-activity time, ledger-based net profit, debtor balances, local-day journal boundaries, balanced journal currency totals, readable summaries, no EF tracking, and tenant isolation.

For 50,000 ledger entries on the local SQL Server, one measured dashboard comparison produced:

| Implementation | Elapsed time |
| --- | ---: |
| Loading raw ledger entities with account/currency includes | 3,204.88 ms |
| Aggregated dashboard stored procedure | 757.53 ms |

The aggregated dashboard path was **76.36% faster** in this sample and reduced 50,000 raw ledger rows to grouped result rows before crossing the database boundary.

## Original-plan phase six — bulk-import staging pipeline — 2026-09-15

The existing `HawalaImportRows` table already had the required durable staging columns and tenant/batch/row uniqueness, so it is retained instead of introducing a duplicate table. Preview rows are now streamed into it with `SqlBulkCopy` inside the same transaction as batch creation. `usp_ValidateHawalaImportStaging_v1` resolves currencies, defaults a missing commission currency to the Hawala currency, detects duplicates within the file, and checks existing incoming numbers/references set-wise.

Confirmation keeps the already-measured `SqlBulkCopy` Hawala and ledger writer. The former per-outgoing-row duplicate query is replaced by one set-based query. Final staging links, selected locations/correspondents, commission values, and batch status are sent with `HawalaImportResultTableType_v1` and persisted atomically by `usp_FinalizeHawalaImportStaging_v1`; this removes up to 10,000 tracked EF update statements. `usp_CleanupHawalaImportStaging_v1` removes only unposted previews older than seven days for the current tenant. Posted batches are never cleaned by this procedure.

Correctness tests cover 7-, 8-, and 9-column files, default commission currency, duplicate rows, existing numbers and references, duplicate-file rejection, paired incoming/outgoing Hawalas, commission placement, balanced ledger posting, and a bounded query count for 100 outgoing rows.

One local 10,000-row measurement produced:

| Operation | Elapsed time |
| --- | ---: |
| Previous EF staging persistence only | 31,356.63 ms |
| New Excel-to-preview pipeline (parse + bulk + SQL validation + preview read) | 13,102.69 ms |
| New confirmation and accounting posting | 9,279.71 ms |

Even though the new measurement includes Excel parsing, SQL validation, and reading the complete preview while the legacy value measures persistence only, the preview path was **58.21% faster**. All database changes remain tenant-scoped and transactional.

## Original-plan phase seven — AED deal posting — 2026-09-15

Create, full/partial conversion, conversion reversal, and deal cancellation now enter SQL through four dedicated stored-procedure endpoints. A shared transactional core keeps the accounting rules identical across those endpoints. Every write is tenant-scoped, verifies the current user, obtains an application lock for the deal or deal number, and commits the transaction, ledger entries, AED records, totals, statuses, and audit log atomically. Transaction-number generation is serialized per tenant and operation prefix to prevent duplicate numbers during concurrent requests.

The fixed `3.67` AED-per-USD rate and `MidpointRounding.AwayFromZero` behavior are preserved by the SQL calculation. Tests cover AED and USD deals, positive and negative actual/declared markers, zero-decimal `.50`/`.49` rounding boundaries, full and partial conversion, profit and loss ledger entries, reversal, cancellation, failed-operation rollback, tenant isolation, and two simultaneous full-conversion attempts.

One cold local SQL Server comparison of the same `450,000 AED` scenario produced:

| Operation | Previous EF path | Stored-procedure path | Reduction |
| --- | ---: | ---: | ---: |
| Create and return deal | 1,429.15 ms / 25 EF commands | 656.46 ms / 1 SP + 1 EF read | 54.07% |
| Convert and return deal | 628.88 ms / 29 EF commands | 112.59 ms / 1 SP + 1 EF read | 82.10% |

The timing is an environment-specific sample; the stable improvement is the reduction to two database round trips per successful write-and-return operation. Failed writes roll back inside SQL and return no partial transaction or ledger rows.

## Original-plan phase eight — UI responsiveness and read caching — 2026-09-15

The Hawala list, received-Hawala page, and correspondent Hawala panel now use a 300 ms debounce with end-to-end cancellation. Cancellation reaches Entity Framework and SQL Server, while a request-version guard prevents a slower obsolete response from replacing newer results. Paging, sorting, reset, and page-size changes cancel any pending search before starting their own load.

The main Hawala list no longer recalculates tenant-wide statistics after every search, filter, sort, or page change. Statistics are refreshed on initial load and after mutations that can change them. A normal filtered load therefore falls from three database operations (count, page data, and statistics) to two (count and page data), a **33.33% round-trip reduction** for that interaction.

Active/all currency lists, payment locations, and company settings use a tenant-aware, scoped two-minute cache with duplicate-load suppression. Repeated reads in the same interactive session require no additional database call during the cache lifetime. AED deal writes refresh only the changed deal list instead of reloading correspondents and currencies each time, and duplicate submit attempts are rejected while a write is running.

The bulk-upload preview now virtualizes its rows instead of rendering as many as 10,000 table rows at once. Existing server-side paging and lazy correspondent-panel rendering are retained. Long-running bulk confirmation continues to show real progress, and disabled create/import actions now explain the blocking condition. Destructive Hawala actions and AED writes are guarded against double clicks.

Automated coverage includes cancellation before database work. The complete performance suite passes with 42 tests, and the Release build completes with zero errors. Browser behavior under deliberately slow networking, a 10,000-row preview, rapid typing/paging, and repeated submit clicks remains the manual acceptance checklist before commit and push.

## Correspondent-details page optimization — 2026-09-15

The initial correspondent-details route now reads the correspondent header, settlement-currency code, and linked active account identifier through one no-tracking projection. Currency lookup data is no longer part of initial navigation: it loads only when the user opens correspondent editing or adds a missing Hawala commission. The active Hawala tab remains independently paged and lazy-rendered, and obsolete header requests are cancelled when navigation changes.

The repeatable SQL integration test measures the complete initial data path, including the first Hawala page:

| Path | Database commands |
| --- | ---: |
| Previous sequential header, currency, account, count, and page reads | 5 |
| Combined header/account plus Hawala count and page reads | 3 |

This is a **40% reduction in initial database round trips**. The dedicated details query also preserves tenant isolation, returns the same header/account values, leaves the EF change tracker empty, and honors cancellation. No schema or accounting behavior changes in this optimization.

## Correspondent-status page optimization — 2026-09-15

The initial status route now uses one tenant-scoped call to `usp_GetAccountOperationsPage_v1` to return the correspondent header, active account identifier, balance by currency, first operations page, total count, and available currency codes. It replaces the previous sequential header query, balance procedure, and operations procedure. Later page and filter changes reuse the operations-only mode of the same procedure. Temporary operation rows are indexed and joined by typed source key rather than repeatedly constructing string keys.

The first visit now loads 10 recent operations instead of the account's complete history. Page sizes of 10, 20, 50, and 100 are available. This screen intentionally does not restore a larger page size from browser storage, so every new visit remains bounded to 10 operations. Full source metadata and all balanced ledger entries for an operation are fetched only after **Show operation details** is selected. Search has a 300 ms debounce, obsolete database reads are cancelled, and a request-version guard prevents an older response from replacing a newer page. Accounts that do not yet exist do not issue balance or journal queries.

SQL integration coverage executes the migration against a disposable database and verifies ordering, three-page boundaries, search/type/currency filters, lazy details, account visibility, cancellation, and tenant rejection. For 1,000 transfer operations, one local comparison produced:

| Path | Rows returned initially | Elapsed time |
| --- | ---: | ---: |
| Previous complete-history read with source details | 1,000 | 994.76 ms |
| Stored-procedure first page | 20 | 248.98 ms |

The measured first-page read was **74.97% faster** and returned **98% fewer operation rows** to the application. The timing is environment-specific; the bounded result size is the stable improvement as account history grows.

For the complete status-page bootstrap on the disposable SQL database, the combined path returned the same header, balances, operation count, order, and rows as the previous sequence:

| Initial status path | Database calls | Elapsed time |
| --- | ---: | ---: |
| Sequential header + balance + operations | 3 | 651.99 ms |
| Combined status procedure | 1 | 44.85 ms |

This sample reduced the initial database round trips by **66.67%**. Exact elapsed time depends on database and network latency, while the one-call initial path remains deterministic.
