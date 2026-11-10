-- Applied only to an explicitly selected PostgreSQL copy, in a transaction.
CREATE SCHEMA IF NOT EXISTS hawala;
CREATE TABLE IF NOT EXISTS hawala.schema_versions
    (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE OR REPLACE FUNCTION hawala.append_result(results json,result_set json) RETURNS json LANGUAGE sql IMMUTABLE AS $append$
    SELECT json_agg(x.value ORDER BY x.position) FROM(
        SELECT value,ordinality AS position FROM json_array_elements(results) WITH ORDINALITY
        UNION ALL SELECT result_set,json_array_length(results)+1)x;
$append$;
CREATE SEQUENCE IF NOT EXISTS hawala.rowversion_sequence AS bigint;

CREATE OR REPLACE FUNCTION hawala.set_rowversion() RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    NEW."RowVersion" := int8send(nextval('hawala.rowversion_sequence'));
    RETURN NEW;
END $body$;

DO $body$
DECLARE item record; maximum bigint := 0; current_max bigint; sequence_value bigint;
BEGIN
    FOR item IN SELECT table_schema,table_name FROM information_schema.columns
        WHERE column_name='RowVersion' AND data_type='bytea' AND table_schema='public'
    LOOP
        EXECUTE format('SELECT max((''x''||encode("RowVersion",''hex''))::bit(64)::bigint) FROM %I.%I',item.table_schema,item.table_name) INTO current_max;
        maximum := greatest(maximum,coalesce(current_max,0));
        IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='hawala_rowversion' AND tgrelid=format('%I.%I',item.table_schema,item.table_name)::regclass) THEN
            EXECUTE format('CREATE TRIGGER hawala_rowversion BEFORE INSERT OR UPDATE ON %I.%I FOR EACH ROW EXECUTE FUNCTION hawala.set_rowversion()',item.table_schema,item.table_name);
        END IF;
    END LOOP;
    SELECT last_value INTO sequence_value FROM hawala.rowversion_sequence;
    PERFORM setval('hawala.rowversion_sequence',greatest(maximum,sequence_value,1),true);
END $body$;

CREATE OR REPLACE FUNCTION hawala.apply_balance_delta(p_tenant bigint,p_account bigint,p_currency bigint,p_delta numeric)
RETURNS void LANGUAGE plpgsql AS $body$
BEGIN
    IF p_delta = 0 THEN RETURN; END IF;
    INSERT INTO public."AccountCurrencyBalances"("TenantId","AccountId","CurrencyId","Balance","UpdatedAt")
    VALUES(p_tenant,p_account,p_currency,p_delta,CURRENT_TIMESTAMP AT TIME ZONE 'UTC')
    ON CONFLICT("TenantId","AccountId","CurrencyId") DO UPDATE
        SET "Balance"=public."AccountCurrencyBalances"."Balance"+EXCLUDED."Balance",
            "UpdatedAt"=EXCLUDED."UpdatedAt";
    DELETE FROM public."AccountCurrencyBalances"
        WHERE "TenantId"=p_tenant AND "AccountId"=p_account AND "CurrencyId"=p_currency AND "Balance"=0;
END $body$;

CREATE OR REPLACE FUNCTION hawala.ledger_balances() RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    IF TG_OP='UPDATE' AND OLD."TenantId"=NEW."TenantId" AND OLD."AccountId"=NEW."AccountId" AND OLD."CurrencyId"=NEW."CurrencyId" THEN
        PERFORM hawala.apply_balance_delta(NEW."TenantId",NEW."AccountId",NEW."CurrencyId",
            (NEW."TalabKar"-NEW."BadehKar")-(OLD."TalabKar"-OLD."BadehKar"));
    ELSE
        IF TG_OP IN ('UPDATE','DELETE') THEN
            PERFORM hawala.apply_balance_delta(OLD."TenantId",OLD."AccountId",OLD."CurrencyId",OLD."BadehKar"-OLD."TalabKar");
        END IF;
        IF TG_OP IN ('UPDATE','INSERT') THEN
            PERFORM hawala.apply_balance_delta(NEW."TenantId",NEW."AccountId",NEW."CurrencyId",NEW."TalabKar"-NEW."BadehKar");
        END IF;
    END IF;
    RETURN NULL;
END $body$;
DO $body$ BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_trigger WHERE tgname='hawala_ledger_balances' AND tgrelid='public."LedgerEntries"'::regclass) THEN
        CREATE TRIGGER hawala_ledger_balances AFTER INSERT OR UPDATE OR DELETE ON public."LedgerEntries"
            FOR EACH ROW EXECUTE FUNCTION hawala.ledger_balances();
    END IF;
END $body$;

CREATE OR REPLACE FUNCTION public.ufn_pendingcorrespondentcommissions_v1(p_tenant bigint,p_account bigint,p_asof timestamp)
RETURNS TABLE("AccountId" bigint,"CurrencyId" bigint,"PendingCommissionDebit" numeric,"PendingCommissionCredit" numeric)
LANGUAGE sql STABLE AS $body$
    SELECT "AccountId","CurrencyId",
        sum(CASE WHEN "Net"<0 THEN -"Net" ELSE 0 END)::numeric(18,4),
        sum(CASE WHEN "Net">0 THEN "Net" ELSE 0 END)::numeric(18,4)
    FROM (
        SELECT e."AccountId",e."CurrencyId",coalesce(t."ReversedTransactionId",t."Id") AS "CommissionDocumentId",
            sum(e."TalabKar"-e."BadehKar") AS "Net"
        FROM public."Transactions" t JOIN public."LedgerEntries" e ON e."TenantId"=t."TenantId" AND e."TransactionId"=t."Id"
        JOIN public."Accounts" a ON a."TenantId"=e."TenantId" AND a."Id"=e."AccountId"
        WHERE t."TenantId"=p_tenant AND a."CorrespondentId" IS NOT NULL
          AND t."TransactionType" IN ('PeriodicCorrespondentCommission','PeriodicOutgoingCommission','PeriodicForwardingCommission','PeriodicCorrespondentCommissionReversal')
          AND (p_account IS NULL OR e."AccountId"=p_account) AND (p_asof IS NULL OR e."CreatedAt"<=p_asof)
          AND NOT EXISTS(SELECT 1 FROM public."CorrespondentCommissionRecognitions" r
            JOIN public."CorrespondentAccountPeriods" p ON p."TenantId"=r."TenantId" AND p."Id"=r."PeriodId"
            WHERE r."TenantId"=e."TenantId" AND r."AccountId"=e."AccountId" AND r."TransactionId"=e."TransactionId"
              AND (p_asof IS NULL OR p."PeriodTo"<=p_asof))
        GROUP BY e."AccountId",e."CurrencyId",coalesce(t."ReversedTransactionId",t."Id")
    ) pending GROUP BY "AccountId","CurrencyId";
$body$;

CREATE OR REPLACE FUNCTION public.ufn_accountoperationalbalances_v1(p_tenant bigint,p_account bigint,p_asof timestamp)
RETURNS TABLE("AccountId" bigint,"CurrencyId" bigint,"Balance" numeric,"PendingCommissionDebit" numeric,"PendingCommissionCredit" numeric,"TotalIncludingCommission" numeric)
LANGUAGE sql STABLE AS $body$
    WITH raw_balances AS (
        SELECT "AccountId","CurrencyId","Balance" FROM public."AccountCurrencyBalances"
        WHERE p_asof IS NULL AND "TenantId"=p_tenant AND (p_account IS NULL OR "AccountId"=p_account)
        UNION ALL
        SELECT "AccountId","CurrencyId",sum("TalabKar"-"BadehKar")::numeric(18,4) FROM public."LedgerEntries"
        WHERE p_asof IS NOT NULL AND "TenantId"=p_tenant AND "CreatedAt"<=p_asof AND (p_account IS NULL OR "AccountId"=p_account)
        GROUP BY "AccountId","CurrencyId"
    )
    SELECT coalesce(raw."AccountId",pending."AccountId"),coalesce(raw."CurrencyId",pending."CurrencyId"),
        (coalesce(raw."Balance",0)-coalesce(pending."PendingCommissionCredit",0)+coalesce(pending."PendingCommissionDebit",0))::numeric(18,4),
        coalesce(pending."PendingCommissionDebit",0),coalesce(pending."PendingCommissionCredit",0),coalesce(raw."Balance",0)
    FROM raw_balances raw FULL JOIN public.ufn_pendingcorrespondentcommissions_v1(p_tenant,p_account,p_asof) pending
        ON pending."AccountId"=raw."AccountId" AND pending."CurrencyId"=raw."CurrencyId";
$body$;

CREATE OR REPLACE FUNCTION public.ufn_hawalaledgerpostings_v1(p_tenant bigint,p_ids text)
RETURNS TABLE("LedgerId" bigint,"HawalaId" bigint,"IsCorrected" integer,"IsReversal" integer,"PostingId" bigint,"PostingStage" text)
LANGUAGE sql STABLE AS $body$
    WITH flags AS (
        SELECT e."Id" AS "LedgerId",e."HawalaId",e."CreatedAt",
            CASE WHEN e."Description" LIKE 'اصلاح حواله آپلود گروهی — برگشت مانده ثبت قبلی%' THEN 1 ELSE 0 END AS "IsReversal"
        FROM public."LedgerEntries" e WHERE e."TenantId"=p_tenant AND e."HawalaId" IS NOT NULL
          AND (p_ids IS NULL OR e."HawalaId" IN(SELECT value::bigint FROM json_array_elements_text(p_ids::json)))
    ), previous AS (
        SELECT *,lag("IsReversal",1,-1) OVER(PARTITION BY "HawalaId" ORDER BY "LedgerId") AS "PreviousReversal",
            max("IsReversal") OVER(PARTITION BY "HawalaId") AS "IsCorrected" FROM flags
    ), runs AS (
        SELECT *,sum(CASE WHEN "IsReversal"<>"PreviousReversal" THEN 1 ELSE 0 END)
            OVER(PARTITION BY "HawalaId" ORDER BY "LedgerId" ROWS UNBOUNDED PRECEDING) AS "RunNumber" FROM previous
    )
    SELECT "LedgerId","HawalaId","IsCorrected","IsReversal",min("LedgerId") OVER(PARTITION BY "HawalaId","RunNumber"),
        CASE WHEN "IsReversal"=1 THEN 'Reversal' WHEN "RunNumber"=1 THEN 'Original' ELSE 'Replacement' END FROM runs;
$body$;

INSERT INTO hawala.schema_versions(version) VALUES(1) ON CONFLICT DO NOTHING;
