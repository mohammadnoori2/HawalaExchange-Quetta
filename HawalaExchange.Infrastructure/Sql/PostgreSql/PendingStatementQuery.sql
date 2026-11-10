SELECT
  "c"."Code" AS "Currency",
  "p"."PendingCommissionCredit" AS "Credit",
  "p"."PendingCommissionDebit" AS "Debit"
FROM "public".UFN_PENDINGCORRESPONDENTCOMMISSIONS_V1(@tenant, @account, @asof) AS "p"
JOIN "public"."Currencies" AS "c"
  ON "c"."Id" = "p"."CurrencyId" AND "c"."TenantId" = @tenant;
