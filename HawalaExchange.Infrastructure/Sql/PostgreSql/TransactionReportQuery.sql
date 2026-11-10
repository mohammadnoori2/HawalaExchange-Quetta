-- Generated SELECT; source SHA256 cf01c87a0e87dbbe7fe6715f3e4ddbdefeeeaf45d9ee4eab1ac98888a4fc5e12
WITH "ReportRows" AS (
  SELECT
    "t"."TransactionNo",
    "t"."TransactionType",
    "t"."CreatedAt",
    COALESCE(NULLIF("t"."CustomerFullName", ''), 'N/A') AS "CustomerName",
    COALESCE(NULLIF("d"."SenderName", ''), 'N/A') AS "SenderName",
    COALESCE(NULLIF("d"."ReceiverName", ''), 'N/A') AS "ReceiverName",
    COALESCE("fc"."Code", 'N/A') AS "FromCurrency",
    CAST(COALESCE("d"."FromAmount", 0) AS DECIMAL(18, 4)) AS "FromAmount",
    COALESCE("tc"."Code", 'N/A') AS "ToCurrency",
    CAST(COALESCE("d"."ToAmount", 0) AS DECIMAL(18, 4)) AS "ToAmount",
    CAST(COALESCE("d"."CommissionAmount", 0) AS DECIMAL(18, 4)) AS "Commission",
    COALESCE("cc"."Code", 'N/A') AS "CommissionCurrency",
    "t"."Status",
    "t"."Id" AS "RowId"
  FROM "public"."Transactions" AS "t" LEFT JOIN LATERAL (
    SELECT
      "td".*
    FROM "public"."TransactionDetails" AS "td"
    WHERE
      "td"."TenantId" = @TenantId
      AND "td"."TransactionId" = "t"."Id"
      AND (
        @CorrespondentId IS NULL OR "td"."CorrespondentId" = @CorrespondentId
      )
    ORDER BY
      "td"."Id" NULLS FIRST
    LIMIT 1
  ) AS "d" ON TRUE
  LEFT JOIN "public"."Currencies" AS "fc"
    ON "fc"."TenantId" = @TenantId AND "fc"."Id" = "d"."FromCurrencyId"
  LEFT JOIN "public"."Currencies" AS "tc"
    ON "tc"."TenantId" = @TenantId AND "tc"."Id" = "d"."ToCurrencyId"
  LEFT JOIN "public"."Currencies" AS "cc"
    ON "cc"."TenantId" = @TenantId AND "cc"."Id" = "d"."CommissionCurrencyId"
  WHERE
    "t"."TenantId" = @TenantId
    AND "t"."CreatedAt" >= @FromUtc
    AND "t"."CreatedAt" < @ToUtcExclusive
    AND (
      @BranchId IS NULL OR "t"."BranchId" = @BranchId
    )
    AND (
      @Status IS NULL OR "t"."Status" = @Status
    )
    AND (
      @CustomerId IS NULL OR "t"."CustomerId" = @CustomerId
    )
    AND (
      @CorrespondentId IS NULL OR NOT "d"."Id" IS NULL
    )
    AND (
      @CurrencyId IS NULL
      OR "d"."FromCurrencyId" = @CurrencyId
      OR "d"."ToCurrencyId" = @CurrencyId
      OR "d"."CommissionCurrencyId" = @CurrencyId
      OR "d"."AgentCommissionCurrencyId" = @CurrencyId
    )
  UNION ALL
  SELECT
    CAST("h"."Number" AS VARCHAR(50)),
    "h"."HawalaType",
    "h"."CreatedAt",
    'N/A',
    COALESCE(NULLIF("h"."SenderName", ''), 'N/A'),
    COALESCE(NULLIF("h"."ReceiverName", ''), 'N/A'),
    COALESCE("hfc"."Code", 'N/A'),
    CAST("h"."FromAmount" AS DECIMAL(18, 4)),
    COALESCE("htc"."Code", 'N/A'),
    CAST(COALESCE("h"."ToAmount", "h"."FromAmount") AS DECIMAL(18, 4)),
    CAST(COALESCE("h"."CommissionAmount", 0) AS DECIMAL(18, 4)),
    COALESCE("hcc"."Code", 'N/A'),
    "h"."Status",
    "h"."Id"
  FROM "public"."Hawalas" AS "h"
  INNER JOIN "public"."Users" AS "u"
    ON "u"."TenantId" = @TenantId AND "u"."Id" = "h"."CreatedBy"
  LEFT JOIN "public"."Currencies" AS "hfc"
    ON "hfc"."TenantId" = @TenantId AND "hfc"."Id" = "h"."FromCurrencyId"
  LEFT JOIN "public"."Currencies" AS "htc"
    ON "htc"."TenantId" = @TenantId AND "htc"."Id" = "h"."ToCurrencyId"
  LEFT JOIN "public"."Currencies" AS "hcc"
    ON "hcc"."TenantId" = @TenantId AND "hcc"."Id" = "h"."CommissionCurrencyId"
  WHERE
    "h"."TenantId" = @TenantId
    AND "h"."CreatedAt" >= @FromUtc
    AND "h"."CreatedAt" < @ToUtcExclusive
    AND (
      @BranchId IS NULL OR "u"."BranchId" = @BranchId
    )
    AND (
      @Status IS NULL OR "h"."Status" = @Status
    )
    AND @CustomerId IS NULL
    AND (
      @CorrespondentId IS NULL OR "h"."CorrespondentId" = @CorrespondentId
    )
    AND (
      @CurrencyId IS NULL
      OR "h"."FromCurrencyId" = @CurrencyId
      OR "h"."ToCurrencyId" = @CurrencyId
      OR "h"."CommissionCurrencyId" = @CurrencyId
      OR "h"."AgentCommissionCurrencyId" = @CurrencyId
    )
)
SELECT
  "TransactionNo",
  "TransactionType",
  "CreatedAt",
  "CustomerName",
  "SenderName",
  "ReceiverName",
  "FromCurrency",
  "FromAmount",
  "ToCurrency",
  "ToAmount",
  "Commission",
  "CommissionCurrency",
  "Status"
FROM "ReportRows"
ORDER BY
  "CreatedAt" DESC NULLS LAST,
  "RowId" DESC NULLS LAST;
