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
