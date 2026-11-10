SELECT
  "e"."Id",
  "e"."HawalaId",
  "e"."AccountMoneyOperationId",
  CASE
    WHEN NOT "a"."CorrespondentId" IS NULL
    AND "t"."TransactionType" IN (
      'PeriodicCorrespondentCommission',
      'PeriodicOutgoingCommission',
      'PeriodicForwardingCommission',
      'PeriodicCorrespondentCommissionReversal'
    )
    AND "p"."PeriodTo" > "e"."CreatedAt"
    THEN "p"."PeriodTo"
    ELSE "e"."CreatedAt"
  END AS "Date",
  COALESCE(
    "t"."TransactionNo",
    CAST("h"."Number" AS VARCHAR(30)),
    CAST("e"."Id" AS VARCHAR(30))
  ) AS "Document",
  COALESCE("e"."Description", '') AS "Description",
  "c"."Code" AS "Currency",
  "e"."TalabKar" AS "Credit",
  "e"."BadehKar" AS "Debit",
  CAST(CASE
    WHEN NOT "a"."CorrespondentId" IS NULL
    AND "t"."TransactionType" IN (
      'PeriodicCorrespondentCommission',
      'PeriodicOutgoingCommission',
      'PeriodicForwardingCommission',
      'PeriodicCorrespondentCommissionReversal'
    )
    AND (
      "p"."Id" IS NULL OR "p"."PeriodTo" >= @end
    )
    THEN 1
    ELSE 0
  END AS BOOLEAN) AS "Pending",
  CAST(0 AS DECIMAL(18, 4)) AS "Balance"
FROM "public"."LedgerEntries" AS "e"
JOIN "public"."Accounts" AS "a"
  ON "a"."Id" = "e"."AccountId" AND "a"."TenantId" = "e"."TenantId"
JOIN "public"."Currencies" AS "c"
  ON "c"."Id" = "e"."CurrencyId" AND "c"."TenantId" = "e"."TenantId"
LEFT JOIN "public"."Transactions" AS "t"
  ON "t"."Id" = "e"."TransactionId" AND "t"."TenantId" = "e"."TenantId"
LEFT JOIN "public"."Hawalas" AS "h"
  ON "h"."Id" = "e"."HawalaId" AND "h"."TenantId" = "e"."TenantId"
LEFT JOIN "public"."CorrespondentCommissionRecognitions" AS "r"
  ON "r"."AccountId" = "e"."AccountId"
  AND "r"."TransactionId" = "e"."TransactionId"
  AND "r"."TenantId" = "e"."TenantId"
LEFT JOIN "public"."CorrespondentAccountPeriods" AS "p"
  ON "p"."Id" = "r"."PeriodId" AND "p"."TenantId" = "r"."TenantId"
WHERE
  "e"."TenantId" = @tenant AND "e"."AccountId" = @account AND "e"."CreatedAt" < @end;
