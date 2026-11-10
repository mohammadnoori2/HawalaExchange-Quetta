-- Generated SELECT; source SHA256 a7f8ef30cfd03560b4872d573d07380a405aabe0c63134c3db951a82720b6b2b
WITH RECURSIVE "Dates" AS (
  SELECT
    CAST(@FromUtc + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate"
  UNION ALL
  SELECT
    CAST("ReportDate" + INTERVAL '1 DAY' AS DATE)
  FROM "Dates"
  WHERE
    "ReportDate" < (
      CAST(@ToUtcExclusive + (
        INTERVAL '1 MINUTE' * @UtcOffsetMinutes
      ) AS DATE) + INTERVAL '-1 DAY'
    )
), "FilteredTransactions" AS (
  SELECT
    "Id",
    "TransactionType",
    "CreatedAt"
  FROM "public"."Transactions"
  WHERE
    "TenantId" = @TenantId
    AND "CreatedAt" >= @FromUtc
    AND "CreatedAt" < @ToUtcExclusive
    AND (
      @BranchId IS NULL OR "BranchId" = @BranchId
    )
    AND "Status" <> 'Cancel'
    AND (
      @CurrencyId IS NULL
      OR EXISTS(
        SELECT
          1
        FROM "public"."TransactionDetails" AS "fd"
        WHERE
          "fd"."TenantId" = @TenantId
          AND "fd"."TransactionId" = "Transactions"."Id"
          AND (
            "fd"."FromCurrencyId" = @CurrencyId
            OR "fd"."ToCurrencyId" = @CurrencyId
            OR "fd"."CommissionCurrencyId" = @CurrencyId
            OR "fd"."AgentCommissionCurrencyId" = @CurrencyId
          )
      )
    )
), "FilteredHawalas" AS (
  SELECT
    "h".*
  FROM "public"."Hawalas" AS "h"
  INNER JOIN "public"."Users" AS "u"
    ON "u"."TenantId" = @TenantId AND "u"."Id" = "h"."CreatedBy"
  WHERE
    "h"."TenantId" = @TenantId
    AND "h"."CreatedAt" >= @FromUtc
    AND "h"."CreatedAt" < @ToUtcExclusive
    AND (
      @BranchId IS NULL OR "u"."BranchId" = @BranchId
    )
    AND "h"."Status" <> 'Cancel'
    AND (
      @CurrencyId IS NULL
      OR "h"."FromCurrencyId" = @CurrencyId
      OR "h"."ToCurrencyId" = @CurrencyId
      OR "h"."CommissionCurrencyId" = @CurrencyId
      OR "h"."AgentCommissionCurrencyId" = @CurrencyId
    )
), "ActivityCounts" AS (
  SELECT
    "ReportDate",
    COUNT(*) AS "TotalTransactions"
  FROM (
    SELECT
      CAST("CreatedAt" + (
        INTERVAL '1 MINUTE' * @UtcOffsetMinutes
      ) AS DATE) AS "ReportDate"
    FROM "FilteredTransactions"
    UNION ALL
    SELECT
      CAST("CreatedAt" + (
        INTERVAL '1 MINUTE' * @UtcOffsetMinutes
      ) AS DATE)
    FROM "FilteredHawalas"
  ) AS "a"
  GROUP BY
    "ReportDate"
), "TransactionAmounts" AS (
  SELECT
    CAST("t"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate",
    SUM(
      CASE
        WHEN "t"."TransactionType" = 'HawalaSend'
        AND (
          @CurrencyId IS NULL OR "d"."FromCurrencyId" = @CurrencyId
        )
        THEN COALESCE("d"."FromAmount", 0)
        ELSE 0
      END
    ) AS "TotalSendAmount",
    SUM(
      CASE
        WHEN "t"."TransactionType" = 'HawalaReceive'
        AND (
          @CurrencyId IS NULL OR "d"."ToCurrencyId" = @CurrencyId
        )
        THEN COALESCE("d"."ToAmount", 0)
        ELSE 0
      END
    ) AS "TotalReceiveAmount",
    SUM(
      CASE
        WHEN @CurrencyId IS NULL OR "d"."CommissionCurrencyId" = @CurrencyId
        THEN COALESCE("d"."CommissionAmount", 0)
        ELSE 0
      END
    ) AS "TotalCommission",
    SUM(
      CASE
        WHEN @CurrencyId IS NULL OR "d"."AgentCommissionCurrencyId" = @CurrencyId
        THEN COALESCE("d"."AgentCommissionAmount", 0)
        ELSE 0
      END
    ) AS "TotalAgentCommission"
  FROM "FilteredTransactions" AS "t"
  INNER JOIN "public"."TransactionDetails" AS "d"
    ON "d"."TenantId" = @TenantId AND "d"."TransactionId" = "t"."Id"
  GROUP BY
    CAST("t"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE)
), "HawalaAmounts" AS (
  SELECT
    CAST("CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate",
    SUM(
      CASE
        WHEN "HawalaType" = 'HawalaSend'
        AND (
          @CurrencyId IS NULL OR "FromCurrencyId" = @CurrencyId
        )
        THEN "FromAmount"
        ELSE 0
      END
    ) AS "TotalSendAmount",
    SUM(
      CASE
        WHEN "HawalaType" = 'HawalaReceive'
        AND (
          @CurrencyId IS NULL OR "ToCurrencyId" = @CurrencyId
        )
        THEN COALESCE("ToAmount", "FromAmount")
        ELSE 0
      END
    ) AS "TotalReceiveAmount",
    SUM(
      CASE
        WHEN @CurrencyId IS NULL OR "CommissionCurrencyId" = @CurrencyId
        THEN COALESCE("CommissionAmount", 0)
        ELSE 0
      END
    ) AS "TotalCommission",
    SUM(
      CASE
        WHEN @CurrencyId IS NULL OR "AgentCommissionCurrencyId" = @CurrencyId
        THEN COALESCE("AgentCommissionAmount", 0)
        ELSE 0
      END
    ) AS "TotalAgentCommission"
  FROM "FilteredHawalas"
  GROUP BY
    CAST("CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE)
), "PeriodicCommissionAmounts" AS (
  SELECT
    CAST("b"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate",
    SUM("b"."TotalCommissionUsd") AS "TotalCommission"
  FROM "public"."CorrespondentCommissionBatches" AS "b"
  WHERE
    "b"."TenantId" = @TenantId
    AND "b"."CreatedAt" >= @FromUtc
    AND "b"."CreatedAt" < @ToUtcExclusive
    AND "b"."Status" = 'Posted'
    AND @BranchId IS NULL
    AND (
      @CurrencyId IS NULL
      OR @CurrencyId = (
        SELECT
          "Id"
        FROM "public"."Currencies"
        WHERE
          "TenantId" = @TenantId AND "Code" = 'USD'
        LIMIT 1
      )
    )
  GROUP BY
    CAST("b"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE)
), "ExpenseAmounts" AS (
  SELECT
    CAST("ExpenseDate" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate",
    SUM("Amount") AS "TotalExpenses"
  FROM "public"."Expenses"
  WHERE
    "TenantId" = @TenantId
    AND "IsDeleted" = FALSE
    AND "ExpenseDate" >= @FromUtc
    AND "ExpenseDate" < @ToUtcExclusive
    AND (
      @CurrencyId IS NULL OR "CurrencyId" = @CurrencyId
    )
  GROUP BY
    CAST("ExpenseDate" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE)
)
SELECT
  CAST("d"."ReportDate" AS TIMESTAMP) AS "ReportDate",
  COALESCE(@BranchId, CAST(0 AS BIGINT)) AS "BranchId",
  CASE
    WHEN @BranchId IS NULL
    THEN 'همه شعبه‌ها'
    ELSE COALESCE(
      (
        SELECT
          "Name"
        FROM "public"."Branches"
        WHERE
          "TenantId" = @TenantId AND "Id" = @BranchId
        LIMIT 1
      ),
      'شعبه'
    )
  END AS "BranchName",
  @CurrencyId AS "CurrencyId",
  CASE
    WHEN @CurrencyId IS NULL
    THEN NULL
    ELSE (
      SELECT
        "Code"
      FROM "public"."Currencies"
      WHERE
        "TenantId" = @TenantId AND "Id" = @CurrencyId
      LIMIT 1
    )
  END AS "CurrencyCode",
  CAST(COALESCE("ac"."TotalTransactions", 0) AS INT) AS "TotalTransactions",
  CAST(COALESCE("ta"."TotalSendAmount", 0) + COALESCE("ha"."TotalSendAmount", 0) AS DECIMAL(18, 4)) AS "TotalSendAmount",
  CAST(COALESCE("ta"."TotalReceiveAmount", 0) + COALESCE("ha"."TotalReceiveAmount", 0) AS DECIMAL(18, 4)) AS "TotalReceiveAmount",
  CAST(COALESCE("ta"."TotalCommission", 0) + COALESCE("ha"."TotalCommission", 0) + COALESCE("pc"."TotalCommission", 0) AS DECIMAL(18, 4)) AS "TotalCommission",
  CAST(COALESCE("ta"."TotalAgentCommission", 0) + COALESCE("ha"."TotalAgentCommission", 0) AS DECIMAL(18, 4)) AS "TotalAgentCommission",
  CAST(COALESCE("ea"."TotalExpenses", 0) AS DECIMAL(18, 4)) AS "TotalExpenses",
  CAST(COALESCE("ta"."TotalCommission", 0) + COALESCE("ha"."TotalCommission", 0) + COALESCE("pc"."TotalCommission", 0) - COALESCE("ta"."TotalAgentCommission", 0) - COALESCE("ha"."TotalAgentCommission", 0) - COALESCE("ea"."TotalExpenses", 0) AS DECIMAL(18, 4)) AS "NetIncome"
FROM "Dates" AS "d"
LEFT JOIN "ActivityCounts" AS "ac"
  ON "ac"."ReportDate" = "d"."ReportDate"
LEFT JOIN "TransactionAmounts" AS "ta"
  ON "ta"."ReportDate" = "d"."ReportDate"
LEFT JOIN "HawalaAmounts" AS "ha"
  ON "ha"."ReportDate" = "d"."ReportDate"
LEFT JOIN "PeriodicCommissionAmounts" AS "pc"
  ON "pc"."ReportDate" = "d"."ReportDate"
LEFT JOIN "ExpenseAmounts" AS "ea"
  ON "ea"."ReportDate" = "d"."ReportDate"
ORDER BY
  "d"."ReportDate" NULLS FIRST;
