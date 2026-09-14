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
