-- Generated SELECT; source SHA256 2cdf157dd2724b257b51716326efc53199d135499d9398652ac7d5bcb2da6e29
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
), "CommissionRows" AS (
  SELECT
    CAST("t"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE) AS "ReportDate",
    CASE
      WHEN @CurrencyId IS NULL OR "d"."CommissionCurrencyId" = @CurrencyId
      THEN COALESCE("d"."CommissionAmount", 0)
      ELSE 0
    END AS "CommissionAmount",
    CASE
      WHEN @CurrencyId IS NULL OR "d"."AgentCommissionCurrencyId" = @CurrencyId
      THEN COALESCE("d"."AgentCommissionAmount", 0)
      ELSE 0
    END AS "AgentCommissionAmount"
  FROM "public"."Transactions" AS "t"
  INNER JOIN "public"."TransactionDetails" AS "d"
    ON "d"."TenantId" = @TenantId AND "d"."TransactionId" = "t"."Id"
  WHERE
    "t"."TenantId" = @TenantId
    AND "t"."CreatedAt" >= @FromUtc
    AND "t"."CreatedAt" < @ToUtcExclusive
    AND (
      @BranchId IS NULL OR "t"."BranchId" = @BranchId
    )
    AND "t"."Status" <> 'Cancel'
  UNION ALL
  SELECT
    CAST("h"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE),
    CASE
      WHEN @CurrencyId IS NULL OR "h"."CommissionCurrencyId" = @CurrencyId
      THEN COALESCE("h"."CommissionAmount", 0)
      ELSE 0
    END,
    CASE
      WHEN @CurrencyId IS NULL OR "h"."AgentCommissionCurrencyId" = @CurrencyId
      THEN COALESCE("h"."AgentCommissionAmount", 0)
      ELSE 0
    END
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
  UNION ALL
  SELECT
    CAST("b"."CreatedAt" + (
      INTERVAL '1 MINUTE' * @UtcOffsetMinutes
    ) AS DATE),
    "b"."TotalCommissionUsd",
    CAST(0 AS DECIMAL(18, 4))
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
), "CommissionAmounts" AS (
  SELECT
    "ReportDate",
    SUM("CommissionAmount") AS "TotalCommission",
    SUM("AgentCommissionAmount") AS "TotalAgentCommission"
  FROM "CommissionRows"
  GROUP BY
    "ReportDate"
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
  CAST(COALESCE("ca"."TotalCommission", 0) AS DECIMAL(18, 4)) AS "TotalCommission",
  CAST(COALESCE("ca"."TotalAgentCommission", 0) AS DECIMAL(18, 4)) AS "TotalAgentCommission",
  CAST(COALESCE("ca"."TotalCommission", 0) - COALESCE("ca"."TotalAgentCommission", 0) AS DECIMAL(18, 4)) AS "NetCommission"
FROM "Dates" AS "d"
LEFT JOIN "CommissionAmounts" AS "ca"
  ON "ca"."ReportDate" = "d"."ReportDate"
ORDER BY
  "d"."ReportDate" NULLS FIRST;
