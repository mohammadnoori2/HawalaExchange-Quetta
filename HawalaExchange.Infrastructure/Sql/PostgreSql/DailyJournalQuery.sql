-- Generated SELECT; source SHA256 24a228b6f8ded9e31863037f09dfe4fd745560a05dbd737e579e76a80cb4ffe2
SELECT
  "le"."Id",
  "le"."CreatedAt",
  "le"."HawalaId",
  "le"."CapitalInvestmentId",
  "le"."ExpenseId",
  "le"."AccountMoneyOperationId",
  "le"."MoneyExchangeOperationId",
  "le"."TransferId",
  "le"."TransactionId",
  "le"."AccountId",
  "a"."AccountCode",
  "a"."AccountName",
  "a"."AccountType",
  "le"."CurrencyId",
  "c"."Code" AS "CurrencyCode",
  "le"."TalabKar",
  "le"."BadehKar",
  "le"."Description"
FROM "public"."LedgerEntries" AS "le"
INNER JOIN "public"."Accounts" AS "a"
  ON "a"."TenantId" = "le"."TenantId" AND "a"."Id" = "le"."AccountId"
INNER JOIN "public"."Currencies" AS "c"
  ON "c"."TenantId" = "le"."TenantId" AND "c"."Id" = "le"."CurrencyId"
WHERE
  "le"."TenantId" = @TenantId
  AND "le"."CreatedAt" >= @FromDate
  AND "le"."CreatedAt" < @ToDateExclusive
ORDER BY
  "le"."CreatedAt" DESC NULLS LAST,
  "le"."Id" DESC NULLS LAST;
