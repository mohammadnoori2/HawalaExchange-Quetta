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
