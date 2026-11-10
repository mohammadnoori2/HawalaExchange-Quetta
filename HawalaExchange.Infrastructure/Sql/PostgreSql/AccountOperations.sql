CREATE OR REPLACE FUNCTION hawala.account_operations_page(p jsonb) RETURNS json LANGUAGE plpgsql AS $body$
DECLARE
    v_tenantid bigint:=(p->>'TenantId')::bigint;v_accountid bigint:=(p->>'AccountId')::bigint;v_correspondentid bigint:=(p->>'CorrespondentId')::bigint;
    v_pagenumber integer:=coalesce((p->>'PageNumber')::integer,1);v_pagesize integer:=coalesce((p->>'PageSize')::integer,20);
    v_searchterm text:=nullif(btrim(p->>'SearchTerm'),'');v_sourcetype text:=nullif(btrim(p->>'SourceType'),'');v_currencycode text:=nullif(btrim(p->>'CurrencyCode'),'');
    v_fromdate timestamp:=(p->>'FromDate')::timestamp;v_todateexclusive timestamp:=(p->>'ToDateExclusive')::timestamp;
    v_minamount numeric(18,4):=(p->>'MinAmount')::numeric;v_maxamount numeric(18,4):=(p->>'MaxAmount')::numeric;v_direction text:=p->>'Direction';
    v_includestatusheader boolean:=coalesce((p->>'IncludeStatusHeader')::boolean,false);v_includebalances boolean:=coalesce((p->>'IncludeBalances')::boolean,false);
    v_postinghawalaids text;result json:='[]';part json;
BEGIN
    IF coalesce(v_tenantid,0)<=0 OR v_pagenumber<1 OR v_pagesize<1 OR v_pagesize>100 THEN RAISE EXCEPTION 'Invalid tenant or page bounds.'; END IF;
    IF v_includestatusheader THEN
        IF v_correspondentid IS NULL THEN RAISE EXCEPTION 'A correspondent identifier is required.'; END IF;
        SELECT "Id" INTO v_accountid FROM public."Accounts" WHERE "TenantId"=v_tenantid AND "CorrespondentId"=v_correspondentid AND NOT "IsArchived" ORDER BY "Id" LIMIT 1;
        SELECT coalesce(json_agg(q),'[]'::json) INTO part FROM(SELECT c."Id",c."Code",c."Name",c."Country",c."City",c."PhoneNumber",c."Address",c."IsArchived",c."Remarks",
            c."SettlementCurrencyId",cur."Code" AS "SettlementCurrencyCode",c."CommissionMethod",c."CreatedAt",v_accountid AS "AccountId"
            FROM public."Correspondents" c LEFT JOIN public."Currencies" cur ON cur."TenantId"=v_tenantid AND cur."Id"=c."SettlementCurrencyId" WHERE c."TenantId"=v_tenantid AND c."Id"=v_correspondentid)q;
        result:=json_build_array(part);
        IF json_array_length(part)=0 THEN RETURN result; END IF;
        IF v_accountid IS NULL THEN RETURN json_build_array(part,'[]'::json,json_build_array(json_build_object('TotalCount',0)),'[]'::json,'[]'::json); END IF;
    END IF;
    IF coalesce(v_accountid,0)<=0 OR NOT EXISTS(SELECT 1 FROM public."Accounts" WHERE "TenantId"=v_tenantid AND "Id"=v_accountid) THEN RAISE EXCEPTION 'The account does not belong to the current tenant.'; END IF;
    IF v_includebalances THEN
        SELECT coalesce(json_agg(q),'[]'::json) INTO part FROM(SELECT c."Id" AS "CurrencyId",c."Code" AS "CurrencyCode",b."Balance"::numeric(18,2) AS "Balance",
            b."PendingCommissionDebit"::numeric(18,2) AS "PendingCommissionDebit",b."PendingCommissionCredit"::numeric(18,2) AS "PendingCommissionCredit"
            FROM public.ufn_accountoperationalbalances_v1(v_tenantid,v_accountid,NULL)b JOIN public."Currencies" c ON c."TenantId"=v_tenantid AND c."Id"=b."CurrencyId"
            WHERE b."Balance"<>0 OR b."PendingCommissionDebit"<>0 OR b."PendingCommissionCredit"<>0 ORDER BY c."Code")q;
        result:=hawala.append_result(result,part);
    END IF;
    SELECT coalesce(json_agg(DISTINCT "HawalaId")::text,'[]') INTO v_postinghawalaids FROM public."LedgerEntries" WHERE "TenantId"=v_tenantid AND "AccountId"=v_accountid AND "HawalaId" IS NOT NULL;
    DROP TABLE IF EXISTS pg_temp."AccountEntries",pg_temp."Operations",pg_temp."FilteredOperations",pg_temp."PageOperations";
    CREATE TEMP TABLE "AccountEntries" ON COMMIT DROP AS SELECT
  "entry"."HawalaId",
  "posting"."PostingStage",
  "entry"."Id",
  "entry"."CreatedAt",
  "entry"."CurrencyId",
  "entry"."TalabKar",
  "entry"."BadehKar",
  "entry"."Description",
  CASE
    WHEN "posting"."IsCorrected" = 1
    THEN 'HawalaPosting'
    WHEN NOT "entry"."HawalaId" IS NULL
    THEN 'Hawala'
    WHEN NOT "entry"."CapitalInvestmentId" IS NULL
    THEN 'CapitalInvestment'
    WHEN NOT "entry"."ExpenseId" IS NULL
    THEN 'Expense'
    WHEN NOT "entry"."AccountMoneyOperationId" IS NULL
    THEN 'AccountMoneyOperation'
    WHEN NOT "entry"."MoneyExchangeOperationId" IS NULL
    THEN 'MoneyExchangeOperation'
    WHEN NOT "entry"."TransferId" IS NULL
    THEN 'Transfer'
    WHEN NOT "entry"."TransactionId" IS NULL
    THEN 'Transaction'
    ELSE 'Manual'
  END AS "SourceKind",
  COALESCE(
    CASE WHEN "posting"."IsCorrected" = 1 THEN "posting"."PostingId" END,
    "entry"."HawalaId",
    "entry"."CapitalInvestmentId",
    "entry"."ExpenseId",
    "entry"."AccountMoneyOperationId",
    "entry"."MoneyExchangeOperationId",
    "entry"."TransferId",
    "entry"."TransactionId",
    "entry"."Id"
  ) AS "OperationId"
FROM "public"."LedgerEntries" AS "entry"
LEFT JOIN "public".UFN_HAWALALEDGERPOSTINGS_V1(v_tenantid, v_postinghawalaids) AS "posting"
  ON "posting"."LedgerId" = "entry"."Id"
WHERE
  "entry"."TenantId" = v_tenantid AND "entry"."AccountId" = v_accountid;
    CREATE INDEX ON "AccountEntries"("SourceKind","OperationId","CurrencyId");
    CREATE TEMP TABLE "Operations" ON COMMIT DROP AS SELECT
  CONCAT("source"."SourceKind", ':', CAST("source"."OperationId" AS VARCHAR(30))) AS "OperationKey",
  "source"."SourceKind",
  "source"."OperationId",
  MIN("source"."HawalaId") AS "HawalaId",
  MAX("source"."PostingStage") AS "PostingStage",
  MIN("source"."CreatedAt") AS "CreatedAt",
  MAX(NULLIF(LTRIM(RTRIM("source"."Description")), '')) AS "Description",
  CAST("source"."OperationId" AS VARCHAR(100)) AS "DocumentNumber"
FROM "AccountEntries" AS "source"
GROUP BY
  "source"."SourceKind",
  "source"."OperationId";
    UPDATE "Operations" operation SET "DocumentNumber"=h."Number"::varchar(100) FROM public."Hawalas" h
        WHERE h."TenantId"=v_tenantid AND operation."SourceKind" IN('Hawala','HawalaPosting') AND h."Id"=operation."HawalaId";
    UPDATE "Operations" operation SET "DocumentNumber"=coalesce(nullif(t."ReferenceNumber",''),t."Id"::varchar(100)) FROM public."Transfers" t
        WHERE t."TenantId"=v_tenantid AND operation."SourceKind"='Transfer' AND t."Id"=operation."OperationId";
    UPDATE "Operations" operation SET "DocumentNumber"=t."TransactionNo" FROM public."Transactions" t
        WHERE t."TenantId"=v_tenantid AND operation."SourceKind"='Transaction' AND t."Id"=operation."OperationId";
    CREATE TEMP TABLE "FilteredOperations" ON COMMIT DROP AS SELECT
  "operation".*
FROM "Operations" AS "operation"
WHERE
  (
    v_sourcetype IS NULL
    OR (
      v_sourcetype = 'Other'
      AND NOT "operation"."SourceKind" IN ('MoneyExchangeOperation', 'Transfer', 'AccountMoneyOperation')
    )
    OR v_sourcetype = CASE "operation"."SourceKind"
      WHEN 'HawalaPosting'
      THEN 'حواله'
      WHEN 'Hawala'
      THEN 'حواله'
      WHEN 'CapitalInvestment'
      THEN 'ثبت سرمایه'
      WHEN 'Expense'
      THEN 'مصرف'
      WHEN 'AccountMoneyOperation'
      THEN 'واریز / برداشت / پرداخت'
      WHEN 'MoneyExchangeOperation'
      THEN 'تبدیل پول'
      WHEN 'Transfer'
      THEN 'انتقال'
      WHEN 'Transaction'
      THEN 'تراکنش'
      ELSE 'ثبت دستی'
    END
  )
  AND (
    v_fromdate IS NULL OR "operation"."CreatedAt" >= v_fromdate
  )
  AND (
    v_todateexclusive IS NULL OR "operation"."CreatedAt" < v_todateexclusive
  )
  AND (
    v_searchterm IS NULL
    OR "operation"."DocumentNumber" LIKE '%' || v_searchterm || '%'
    OR "operation"."Description" LIKE '%' || v_searchterm || '%'
    OR "operation"."OperationKey" LIKE '%' || v_searchterm || '%'
  )
  AND (
    v_currencycode IS NULL
    OR EXISTS(
      SELECT
        1
      FROM "AccountEntries" AS "accountEntry"
      INNER JOIN "public"."Currencies" AS "currency"
        ON "currency"."TenantId" = v_tenantid
        AND "currency"."Id" = "accountEntry"."CurrencyId"
      WHERE
        "accountEntry"."SourceKind" = "operation"."SourceKind"
        AND "accountEntry"."OperationId" = "operation"."OperationId"
        AND "currency"."Code" = v_currencycode
    )
  )
  AND (
    (
      v_minamount IS NULL AND v_maxamount IS NULL AND NULLIF(v_direction, '') IS NULL
    )
    OR EXISTS(
      SELECT
        1
      FROM "AccountEntries" AS "amountEntry"
      JOIN "public"."Currencies" AS "amountCurrency"
        ON "amountCurrency"."Id" = "amountEntry"."CurrencyId"
        AND "amountCurrency"."TenantId" = v_tenantid
      WHERE
        "amountEntry"."SourceKind" = "operation"."SourceKind"
        AND "amountEntry"."OperationId" = "operation"."OperationId"
        AND (
          v_currencycode IS NULL OR "amountCurrency"."Code" = v_currencycode
        )
      GROUP BY
        "amountEntry"."CurrencyId"
      HAVING
        (
          v_direction IS NULL
          OR v_direction = ''
          OR (
            v_direction = 'Credit' AND SUM("amountEntry"."TalabKar") > 0
          )
          OR (
            v_direction = 'Debit' AND SUM("amountEntry"."BadehKar") > 0
          )
        )
        AND (
          v_minamount IS NULL
          OR CASE
            WHEN SUM("amountEntry"."TalabKar") >= SUM("amountEntry"."BadehKar")
            THEN SUM("amountEntry"."TalabKar")
            ELSE SUM("amountEntry"."BadehKar")
          END >= v_minamount
        )
        AND (
          v_maxamount IS NULL
          OR CASE
            WHEN SUM("amountEntry"."TalabKar") >= SUM("amountEntry"."BadehKar")
            THEN SUM("amountEntry"."TalabKar")
            ELSE SUM("amountEntry"."BadehKar")
          END <= v_maxamount
        )
    )
  );
    SELECT json_build_array(json_build_object('TotalCount',count(*))) INTO part FROM "FilteredOperations";
    -- json preserves column order for the established positional ADO readers.
    result:=hawala.append_result(result,part);
    CREATE TEMP TABLE "PageOperations" ON COMMIT DROP AS WITH "Numbered" AS (
  SELECT
    "operation".*,
    ROW_NUMBER() OVER (
      ORDER BY "operation"."CreatedAt" DESC NULLS LAST, "operation"."OperationId" DESC NULLS LAST
    ) AS "RowNumber"
  FROM "FilteredOperations" AS "operation"
)
SELECT
  *
FROM "Numbered"
WHERE
  "RowNumber" BETWEEN (
    (
      v_pagenumber - 1
    ) * v_pagesize
  ) + 1 AND v_pagenumber * v_pagesize;
    SELECT coalesce(json_agg(q),'[]'::json) INTO part FROM(SELECT
  "operation"."OperationKey",
  "operation"."CreatedAt",
  CASE "operation"."SourceKind"
    WHEN 'HawalaPosting'
    THEN 'حواله'
    WHEN 'Hawala'
    THEN 'حواله'
    WHEN 'CapitalInvestment'
    THEN 'ثبت سرمایه'
    WHEN 'Expense'
    THEN 'مصرف'
    WHEN 'AccountMoneyOperation'
    THEN 'واریز / برداشت / پرداخت'
    WHEN 'MoneyExchangeOperation'
    THEN 'تبدیل پول'
    WHEN 'Transfer'
    THEN 'انتقال'
    WHEN 'Transaction'
    THEN 'تراکنش'
    ELSE 'ثبت دستی'
  END AS "SourceType",
  CASE
    WHEN "operation"."SourceKind" = 'HawalaPosting'
    THEN "operation"."HawalaId"
    WHEN "operation"."SourceKind" = 'Manual'
    THEN NULL
    ELSE "operation"."OperationId"
  END AS "SourceId",
  "operation"."DocumentNumber",
  COALESCE("operation"."Description", '-') AS "Description",
  "account"."AccountName",
  "currency"."Id" AS "CurrencyId",
  "currency"."Code" AS "CurrencyCode",
  CAST(SUM("entry"."TalabKar") AS DECIMAL(18, 2)) AS "TotalTalabKar",
  CAST(SUM("entry"."BadehKar") AS DECIMAL(18, 2)) AS "TotalBadehKar",
  CASE
    WHEN "operation"."SourceKind" = 'HawalaPosting'
    THEN "operation"."PostingStage"
  END AS "PostingStage"
FROM "PageOperations" AS "operation"
INNER JOIN "AccountEntries" AS "entry"
  ON "entry"."SourceKind" = "operation"."SourceKind"
  AND "entry"."OperationId" = "operation"."OperationId"
INNER JOIN "public"."Currencies" AS "currency"
  ON "currency"."TenantId" = v_tenantid AND "currency"."Id" = "entry"."CurrencyId"
INNER JOIN "public"."Accounts" AS "account"
  ON "account"."TenantId" = v_tenantid AND "account"."Id" = v_accountid
GROUP BY
  "operation"."OperationKey",
  "operation"."CreatedAt",
  "operation"."SourceKind",
  "operation"."OperationId",
  "operation"."DocumentNumber",
  "operation"."Description",
  "operation"."HawalaId",
  "operation"."PostingStage",
  "operation"."RowNumber",
  "account"."AccountName",
  "currency"."Id",
  "currency"."Code"
ORDER BY
  "operation"."RowNumber" NULLS FIRST,
  "currency"."Code" NULLS FIRST)q;
    result:=hawala.append_result(result,part);
    SELECT coalesce(json_agg(q),'[]'::json) INTO part FROM(SELECT DISTINCT c."Code" FROM "AccountEntries" e JOIN public."Currencies" c ON c."TenantId"=v_tenantid AND c."Id"=e."CurrencyId" ORDER BY c."Code")q;
    result:=hawala.append_result(result,part);
    RETURN result;
END $body$;
