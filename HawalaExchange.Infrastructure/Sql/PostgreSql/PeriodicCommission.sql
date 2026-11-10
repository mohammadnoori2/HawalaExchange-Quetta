CREATE OR REPLACE FUNCTION hawala.system_account(p_tenant bigint,p_code text,p_name text,p_type text) RETURNS bigint LANGUAGE plpgsql AS $body$
DECLARE account_id bigint; archived boolean; account_type text;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('SystemAccount:'||p_tenant||':'||p_code,0));
    SELECT "Id","IsArchived","AccountType" INTO account_id,archived,account_type FROM public."Accounts"
        WHERE "TenantId"=p_tenant AND "AccountCode"=p_code FOR UPDATE;
    IF NOT FOUND THEN
        INSERT INTO public."Accounts"("TenantId","AccountCode","AccountName","AccountType","IsArchived","CreatedAt")
            VALUES(p_tenant,p_code,p_name,p_type,false,CURRENT_TIMESTAMP AT TIME ZONE 'UTC') RETURNING "Id" INTO account_id;
    ELSIF archived OR account_type<>p_type THEN RAISE EXCEPTION 'حساب سیستمی % باید فعال و از نوع % باشد.',p_code,p_type;
    END IF;
    RETURN account_id;
END $body$;

CREATE OR REPLACE FUNCTION hawala.periodic_commission(p jsonb) RETURNS json LANGUAGE plpgsql AS $body$
DECLARE
    v_mode text:=p->>'Mode'; v_tenantid bigint:=(p->>'TenantId')::bigint; v_currentuserid bigint:=(p->>'CurrentUserId')::bigint;
    v_correspondentid bigint:=(p->>'CorrespondentId')::bigint; v_hawalatype text:=p->>'HawalaType';
    v_periodfrom date:=(p->>'PeriodFrom')::timestamp::date; v_periodto date:=(p->>'PeriodTo')::timestamp::date;
    v_fromutc timestamp:=(p->>'FromUtc')::timestamp; v_toutcexclusive timestamp:=(p->>'ToUtcExclusive')::timestamp;
    v_commissionperlakhafn numeric(18,4):=(p->>'CommissionPerLakhAfn')::numeric; v_usdtoafnrate numeric(18,8):=(p->>'UsdToAfnRate')::numeric;
    v_commissionscope text:=coalesce(p->>'CommissionScope','Standard'); v_utcoffsetminutes integer:=coalesce((p->>'UtcOffsetMinutes')::integer,0);
    v_itemscope text; v_ownpaymentlocationid bigint; v_afncurrencyid bigint; v_usdcurrencyid bigint;
    v_correspondentname text; v_hawalacount integer; v_totalbaseafn numeric(18,4); v_totalcommissionafn numeric(18,4); v_totalcommissionusd numeric(18,4);
    v_account bigint; v_income bigint; v_expense bigint; v_clearing bigint; v_branch bigint; v_transaction bigint; v_batch bigint;
    v_now timestamp:=CURRENT_TIMESTAMP AT TIME ZONE 'UTC'; v_prefix text; v_next integer; v_remarks text; v_description text; v_postingamount numeric(18,4);
    v_previous_timeout text; result json;
BEGIN
    IF v_mode IS NULL OR v_mode NOT IN ('Preview','Post') OR v_hawalatype IS NULL OR v_hawalatype NOT IN('HawalaReceive','HawalaSend') THEN RAISE EXCEPTION 'نوع عملیات محاسبه کمیشن معتبر نیست.'; END IF;
    IF v_commissionscope NOT IN('Standard','Destination','Origin','Incoming','Forwarding') OR(v_commissionscope IN('Origin','Incoming') AND v_hawalatype<>'HawalaReceive')
        OR(v_commissionscope IN('Destination','Forwarding') AND v_hawalatype<>'HawalaSend') THEN RAISE EXCEPTION 'نوع محاسبه کمیشن معتبر نیست.'; END IF;
    IF v_periodto<v_periodfrom OR coalesce(v_commissionperlakhafn,0)<=0 THEN RAISE EXCEPTION 'بازه تاریخ یا نرخ کمیشن معتبر نیست.'; END IF;
    SELECT "Name" INTO v_correspondentname FROM public."Correspondents" WHERE "TenantId"=v_tenantid AND "Id"=v_correspondentid AND NOT "IsArchived"
        AND(v_commissionscope<>'Standard' OR "CommissionMethod"='PeriodicPerLakh');
    IF NOT FOUND THEN RAISE EXCEPTION 'نمایندگی فعال با روش کمیشن دوره‌ای یافت نشد.'; END IF;
    IF NOT EXISTS(SELECT 1 FROM public."Users" WHERE "TenantId"=v_tenantid AND "Id"=v_currentuserid AND "IsActive") THEN RAISE EXCEPTION 'کاربر جاری معتبر نیست.'; END IF;
    v_itemscope:=CASE WHEN v_commissionscope='Standard' AND v_hawalatype='HawalaSend' THEN 'Destination' ELSE v_commissionscope END;
    v_previous_timeout:=current_setting('lock_timeout'); PERFORM set_config('lock_timeout','5s',true);
    IF v_mode='Post' THEN PERFORM pg_advisory_xact_lock(hashtextextended('CorrespondentCommissionPeriod:'||v_tenantid||':'||v_correspondentid,0)); END IF;
    SELECT "OwnPaymentLocationId" INTO v_ownpaymentlocationid FROM public."CompanySettings" WHERE "TenantId"=v_tenantid ORDER BY "Id" LIMIT 1;
    IF v_commissionscope='Forwarding' AND v_ownpaymentlocationid IS NULL THEN RAISE EXCEPTION 'ابتدا محل پرداخت خود صرافی را در تنظیمات شرکت مشخص کنید.'; END IF;
    SELECT "Id" INTO v_afncurrencyid FROM public."Currencies" WHERE "TenantId"=v_tenantid AND "Code"='AFN' AND "IsActive" ORDER BY "Id" LIMIT 1;
    SELECT "Id" INTO v_usdcurrencyid FROM public."Currencies" WHERE "TenantId"=v_tenantid AND "Code"='USD' AND "IsActive" ORDER BY "Id" LIMIT 1;
    DROP TABLE IF EXISTS pg_temp."LocationRates",pg_temp."CurrencyRates",pg_temp."LocationCurrencyRates",pg_temp."Eligible";
    CREATE TEMP TABLE "LocationRates"("PaymentLocationId" bigint PRIMARY KEY,"PerLakhRate" numeric(18,4) NOT NULL) ON COMMIT DROP;
    CREATE TEMP TABLE "CurrencyRates"("CurrencyId" bigint PRIMARY KEY,"PerLakhRate" numeric(18,4) NOT NULL) ON COMMIT DROP;
    CREATE TEMP TABLE "LocationCurrencyRates"("PaymentLocationId" bigint,"CurrencyId" bigint,"PerLakhRate" numeric(18,4) NOT NULL,PRIMARY KEY("PaymentLocationId","CurrencyId")) ON COMMIT DROP;
    IF v_hawalatype='HawalaSend' OR v_commissionscope='Origin' THEN
        INSERT INTO "LocationRates" SELECT * FROM jsonb_to_recordset(coalesce(p->>'LocationRatesJson','[]')::jsonb) AS x("PaymentLocationId" bigint,"PerLakhRate" numeric(18,4));
    END IF;
    IF v_hawalatype='HawalaSend' AND v_commissionscope<>'Forwarding' THEN
        INSERT INTO "CurrencyRates" SELECT * FROM jsonb_to_recordset(coalesce(p->>'CurrencyRatesJson','[]')::jsonb) AS x("CurrencyId" bigint,"PerLakhRate" numeric(18,4));
    END IF;
    IF v_commissionscope='Destination' THEN
        INSERT INTO "LocationCurrencyRates" SELECT * FROM jsonb_to_recordset(coalesce(p->>'LocationCurrencyRatesJson','[]')::jsonb) AS x("PaymentLocationId" bigint,"CurrencyId" bigint,"PerLakhRate" numeric(18,4));
    END IF;
    IF EXISTS(SELECT 1 FROM "LocationRates" WHERE "PerLakhRate"<=0) OR EXISTS(SELECT 1 FROM "CurrencyRates" WHERE "PerLakhRate"<=0)
        OR EXISTS(SELECT 1 FROM "LocationCurrencyRates" WHERE "PerLakhRate"<=0 OR "PaymentLocationId"<=0 OR "CurrencyId"<=0) THEN RAISE EXCEPTION 'نرخ کمیشن باید معتبر و بزرگ‌تر از صفر باشد.'; END IF;
    CREATE TEMP TABLE "Eligible" ON COMMIT DROP AS
    SELECT
  "h"."Id" AS "HawalaId",
  "h"."Number" AS "HawalaNumber",
  "h"."CreatedAt" AS "HawalaDate",
  "valuation"."ValuationDate" AS "ValuationDate",
  "currencyData"."CurrencyId" AS "CurrencyId",
  "c"."Code" AS "CurrencyCode",
  CAST("currencyData"."SourceAmount" AS DECIMAL(18, 4)) AS "SourceAmount",
  CAST(CASE
    WHEN "currencyData"."CurrencyId" = v_usdcurrencyid
    THEN 1
    WHEN v_commissionscope = 'Destination'
    THEN 0
    WHEN v_commissionscope IN ('Incoming', 'Forwarding')
    THEN COALESCE("currencyData"."SourceAmount" / NULLIF("valuationAmount"."BaseUsd", 0), 0)
    ELSE "h"."CommissionUsdToAfnRate"
  END AS DECIMAL(18, 8)) AS "SourceToAfnRate",
  CAST(CASE
    WHEN v_commissionscope = 'Destination'
    THEN 0
    WHEN v_commissionscope IN ('Incoming', 'Forwarding')
    THEN COALESCE("valuationAmount"."BaseUsd", 0)
    WHEN v_hawalatype = 'HawalaReceive'
    THEN "h"."CommissionBaseUsdAmount"
    WHEN "currencyData"."CurrencyId" = v_usdcurrencyid
    THEN ROUND("currencyData"."SourceAmount" / 100000 * "locationRate"."PerLakhRate", 2)
    ELSE ROUND(
      ROUND("currencyData"."SourceAmount" / 100000 * "locationRate"."PerLakhRate", 2) / "h"."CommissionUsdToAfnRate",
      2
    )
  END AS DECIMAL(38, 8)) AS "AfnEquivalent",
  CAST(CASE
    WHEN v_commissionscope IN ('Incoming', 'Forwarding')
    THEN COALESCE("valuationAmount"."BaseUsd", 0) / 100000 * "locationRate"."PerLakhRate"
    WHEN v_commissionscope = 'Destination'
    THEN "currencyData"."SourceAmount" / 100000 * "locationRate"."PerLakhRate"
    WHEN v_commissionscope = 'Origin'
    THEN "h"."CommissionBaseUsdAmount" / 100000 * "locationRate"."PerLakhRate"
    WHEN v_hawalatype = 'HawalaReceive'
    THEN "h"."CommissionBaseUsdAmount" / 100000 * v_commissionperlakhafn
    ELSE ROUND("currencyData"."SourceAmount" / 100000 * "locationRate"."PerLakhRate", 2)
  END AS DECIMAL(38, 8)) AS "CommissionAfn",
  "sourceHawala"."CorrespondentId" AS "SourceCorrespondentId",
  "h"."PaymentLocationId" AS "PaymentLocationId",
  "paymentLocation"."Name" AS "PaymentLocationName",
  "locationRate"."PerLakhRate" AS "PerLakhRate"
FROM "public"."Hawalas" AS "h" INNER JOIN LATERAL (
  SELECT
    CASE
      WHEN v_hawalatype = 'HawalaSend'
      THEN "h"."ToCurrencyId"
      ELSE "h"."FromCurrencyId"
    END AS "CurrencyId",
    CASE
      WHEN v_hawalatype = 'HawalaSend'
      THEN COALESCE("h"."ToAmount", "h"."FromAmount")
      ELSE "h"."FromAmount"
    END AS "SourceAmount"
) AS "currencyData" ON TRUE
INNER JOIN "public"."Currencies" AS "c"
  ON "c"."TenantId" = v_tenantid AND "c"."Id" = "currencyData"."CurrencyId"
LEFT JOIN "public"."Hawalas" AS "sourceHawala"
  ON "sourceHawala"."TenantId" = v_tenantid
  AND "sourceHawala"."Id" = "h"."SourceHawalaId" INNER JOIN LATERAL (
  SELECT
    CAST(CASE
      WHEN v_commissionscope = 'Forwarding'
      THEN "sourceHawala"."CreatedAt"
      ELSE "h"."CreatedAt"
    END + (
      INTERVAL '1 MINUTE' * v_utcoffsetminutes
    ) AS DATE) AS "ValuationDate"
) AS "valuation" ON TRUE
LEFT JOIN "public"."CorrespondentSettlementValuations" AS "settled"
  ON "settled"."TenantId" = v_tenantid
  AND "settled"."CorrespondentId" = v_correspondentid
  AND "settled"."HawalaId" = CASE WHEN v_commissionscope = 'Forwarding' THEN "sourceHawala"."Id" ELSE "h"."Id" END
LEFT JOIN "public"."Correspondents" AS "senderOwner"
  ON "senderOwner"."TenantId" = v_tenantid AND "senderOwner"."Id" = v_correspondentid
LEFT JOIN "public"."CorrespondentSettlementDailyRates" AS "settlementRate"
  ON "settlementRate"."TenantId" = v_tenantid
  AND "settlementRate"."CorrespondentId" = v_correspondentid
  AND "settlementRate"."RateDate" = "valuation"."ValuationDate"
  AND "settlementRate"."SourceCurrencyId" = "currencyData"."CurrencyId"
  AND "settlementRate"."TargetCurrencyId" = "senderOwner"."SettlementCurrencyId"
LEFT JOIN "public"."CorrespondentSettlementDailyRates" AS "usdBridge"
  ON "usdBridge"."TenantId" = v_tenantid
  AND "usdBridge"."CorrespondentId" = v_correspondentid
  AND "usdBridge"."RateDate" = "valuation"."ValuationDate"
  AND "usdBridge"."SourceCurrencyId" = v_usdcurrencyid
  AND "usdBridge"."TargetCurrencyId" = "senderOwner"."SettlementCurrencyId" INNER JOIN LATERAL (
  SELECT
    CAST(CASE
      WHEN "currencyData"."CurrencyId" = v_usdcurrencyid
      THEN "currencyData"."SourceAmount"
      WHEN "settled"."SourceCurrencyId" = "currencyData"."CurrencyId"
      AND "settled"."SourceAmount" > 0
      THEN "currencyData"."SourceAmount" * "settled"."UsdAmount" / "settled"."SourceAmount"
      WHEN "currencyData"."CurrencyId" = "senderOwner"."SettlementCurrencyId"
      AND "usdBridge"."UnitToTarget" > 0
      THEN "currencyData"."SourceAmount" / "usdBridge"."UnitToTarget"
      WHEN v_commissionscope = 'Forwarding'
      AND "senderOwner"."SettlementCurrencyId" = v_usdcurrencyid
      THEN "currencyData"."SourceAmount" * "settlementRate"."UnitToTarget"
      WHEN v_commissionscope = 'Forwarding' AND "usdBridge"."UnitToTarget" > 0
      THEN "currencyData"."SourceAmount" * "settlementRate"."UnitToTarget" / "usdBridge"."UnitToTarget"
      ELSE NULL
    END AS DECIMAL(18, 8)) AS "BaseUsd"
) AS "valuationAmount" ON TRUE
LEFT JOIN "public"."PaymentLocations" AS "paymentLocation"
  ON "paymentLocation"."TenantId" = v_tenantid
  AND "paymentLocation"."Id" = "h"."PaymentLocationId"
LEFT JOIN "LocationRates" AS "suppliedLocationRate"
  ON "suppliedLocationRate"."PaymentLocationId" = "h"."PaymentLocationId"
LEFT JOIN "CurrencyRates" AS "suppliedCurrencyRate"
  ON "suppliedCurrencyRate"."CurrencyId" = "currencyData"."CurrencyId"
LEFT JOIN "LocationCurrencyRates" AS "suppliedLocationCurrencyRate"
  ON "suppliedLocationCurrencyRate"."PaymentLocationId" = "h"."PaymentLocationId"
  AND "suppliedLocationCurrencyRate"."CurrencyId" = "currencyData"."CurrencyId" INNER JOIN LATERAL (
  SELECT
    CASE
      WHEN v_commissionscope IN ('Origin', 'Forwarding')
      THEN COALESCE("suppliedLocationRate"."PerLakhRate", v_commissionperlakhafn)
      WHEN v_commissionscope = 'Incoming'
      THEN v_commissionperlakhafn
      WHEN v_commissionscope = 'Destination'
      THEN COALESCE("suppliedLocationCurrencyRate"."PerLakhRate", 0)
      ELSE COALESCE(
        "suppliedCurrencyRate"."PerLakhRate",
        "suppliedLocationRate"."PerLakhRate",
        v_commissionperlakhafn
      )
    END AS "PerLakhRate"
) AS "locationRate" ON TRUE
WHERE
  "h"."TenantId" = v_tenantid
  AND (
    (
      v_commissionscope = 'Forwarding'
      AND "sourceHawala"."CorrespondentId" = v_correspondentid
      AND "sourceHawala"."HawalaType" = 'HawalaReceive'
      AND "sourceHawala"."Status" <> 'Cancel'
      AND NOT "h"."CorrespondentId" IS NULL
      AND (
        "h"."PaymentLocationId" IS NULL
        OR "h"."PaymentLocationId" <> v_ownpaymentlocationid
      )
    )
    OR (
      v_commissionscope <> 'Forwarding' AND "h"."CorrespondentId" = v_correspondentid
    )
  )
  AND "h"."HawalaType" = v_hawalatype
  AND "h"."Status" <> 'Cancel'
  AND "h"."CreatedAt" >= v_fromutc
  AND "h"."CreatedAt" < v_toutcexclusive
  AND (
    v_commissionscope <> 'Origin'
    OR EXISTS(
      SELECT
        1
      FROM "public"."Hawalas" AS "generated"
      WHERE
        "generated"."TenantId" = v_tenantid
        AND "generated"."SourceHawalaId" = "h"."Id"
        AND "generated"."HawalaType" = 'HawalaSend'
        AND "generated"."Status" <> 'Cancel'
    )
  )
  AND (
    v_commissionscope = 'Forwarding'
    OR (
      v_hawalatype = 'HawalaReceive'
      AND (
        "h"."CommissionAmount" IS NULL OR "h"."CommissionAmount" = 0
      )
    )
    OR (
      v_hawalatype = 'HawalaSend'
      AND "h"."AgentCommissionAmount" IS NULL
      AND NOT "sourceHawala"."CorrespondentId" IS NULL
      AND (
        v_commissionscope <> 'Destination' OR "sourceHawala"."Status" <> 'Cancel'
      )
    )
  )
  AND (
    v_commissionscope IN ('Incoming', 'Forwarding', 'Destination')
    OR "currencyData"."CurrencyId" IN (v_afncurrencyid, v_usdcurrencyid)
  )
  AND (
    v_commissionscope IN ('Incoming', 'Forwarding', 'Destination')
    OR NOT "h"."CommissionBaseUsdAmount" IS NULL
  )
  AND NOT EXISTS(
    SELECT
      1
    FROM "public"."CorrespondentCommissionBatchItems" AS "bi"
    WHERE
      "bi"."TenantId" = v_tenantid
      AND "bi"."HawalaId" = "h"."Id"
      AND "bi"."IsActive" = TRUE
      AND (
        "bi"."CommissionScope" = v_itemscope
        OR (
          v_commissionscope = 'Incoming' AND "bi"."CommissionScope" = 'Standard'
        )
        OR (
          v_commissionscope = 'Standard'
          AND v_hawalatype = 'HawalaReceive'
          AND "bi"."CommissionScope" = 'Incoming'
        )
      )
  );
    ALTER TABLE "Eligible" ADD PRIMARY KEY("HawalaId");
    IF v_commissionscope='Forwarding' THEN DELETE FROM "Eligible" e USING public."Hawalas" sent WHERE sent."TenantId"=v_tenantid AND sent."Id"=e."HawalaId" AND EXISTS(
        SELECT 1 FROM public."CorrespondentCommissionBatchItems" old_item WHERE old_item."TenantId"=v_tenantid AND old_item."HawalaId"=sent."SourceHawalaId" AND old_item."CommissionScope"='Origin' AND old_item."IsActive"); END IF;
    IF v_commissionscope IN('Incoming','Forwarding') AND EXISTS(SELECT 1 FROM "Eligible" WHERE "CurrencyId"<>v_usdcurrencyid AND "SourceToAfnRate"<=0) THEN RAISE EXCEPTION 'ابتدا تبدیل به ارز توافقی و اتصال آن به USD را برای روزهای این دوره ثبت کنید.'; END IF;
    IF(v_hawalatype='HawalaSend' OR v_commissionscope='Origin') AND EXISTS(SELECT 1 FROM "Eligible" WHERE "PaymentLocationId" IS NULL OR "PaymentLocationName" IS NULL) THEN RAISE EXCEPTION 'برای یک یا چند حواله ارسالی محل پرداخت معتبر ثبت نشده است.'; END IF;
    IF v_mode='Post' AND v_commissionscope='Destination' AND EXISTS(SELECT 1 FROM "Eligible" e WHERE NOT EXISTS(SELECT 1 FROM "LocationCurrencyRates" r WHERE r."PaymentLocationId"=e."PaymentLocationId" AND r."CurrencyId"=e."CurrencyId")) THEN RAISE EXCEPTION 'نرخ کمیشن هر محل پرداخت و ارز این دوره را وارد کنید.'; END IF;
    IF v_mode='Post' AND v_commissionscope IN('Origin','Forwarding') AND EXISTS(SELECT 1 FROM "Eligible" e WHERE NOT EXISTS(SELECT 1 FROM "LocationRates" r WHERE r."PaymentLocationId"=e."PaymentLocationId")) THEN RAISE EXCEPTION 'نرخ کمیشن همه محل‌های پرداخت این دوره را وارد کنید.'; END IF;
    IF v_commissionscope NOT IN('Destination','Incoming','Forwarding') AND(v_hawalatype='HawalaSend' OR v_commissionscope='Origin') AND EXISTS(SELECT 1 FROM "Eligible" WHERE "SourceToAfnRate"<=0) THEN RAISE EXCEPTION 'نرخ پایان روز برای نمایندگی مبدأ ثبت نشده است.'; END IF;
    IF v_commissionscope IN('Incoming','Destination','Forwarding') THEN
        WITH shares AS(SELECT "HawalaId","CommissionAfn",CASE WHEN v_commissionscope='Forwarding' THEN ("HawalaDate"+v_utcoffsetminutes*INTERVAL '1 minute')::date ELSE date '1900-01-01' END AS group_date,
            CASE WHEN v_commissionscope='Destination' THEN "CurrencyId" ELSE 0 END AS group_currency,floor("CommissionAfn") AS units FROM "Eligible"),
        allocations AS(SELECT "HawalaId",units,round(sum("CommissionAfn") OVER(PARTITION BY group_date,group_currency),0) AS group_total,
            sum(units) OVER(PARTITION BY group_date,group_currency) AS floor_total,row_number() OVER(PARTITION BY group_date,group_currency ORDER BY "CommissionAfn"-units DESC,"HawalaId") AS unit_order FROM shares)
        UPDATE "Eligible" e SET "CommissionAfn"=a.units+CASE WHEN a.unit_order<=a.group_total-a.floor_total THEN 1 ELSE 0 END FROM allocations a WHERE a."HawalaId"=e."HawalaId";
    END IF;
    SELECT count(*)::integer,coalesce(sum("AfnEquivalent"),0)::numeric(18,4) INTO v_hawalacount,v_totalbaseafn FROM "Eligible";
    SELECT CASE WHEN v_hawalatype='HawalaSend' AND v_commissionscope<>'Forwarding' THEN coalesce(sum("CommissionAfn") FILTER(WHERE "CurrencyId"=v_afncurrencyid),0) ELSE 0 END::numeric(18,4),
        CASE WHEN v_commissionscope IN('Incoming','Forwarding','Origin') THEN coalesce(sum("CommissionAfn"),0) WHEN v_hawalatype='HawalaReceive' THEN round(v_totalbaseafn/100000*v_commissionperlakhafn,0)
            ELSE coalesce(sum("CommissionAfn") FILTER(WHERE "CurrencyId"=v_usdcurrencyid),0) END::numeric(18,4) INTO v_totalcommissionafn,v_totalcommissionusd FROM "Eligible";
    IF v_mode='Preview' THEN
        SELECT json_build_array(
            (SELECT json_agg(q) FROM(SELECT v_correspondentid AS "CorrespondentId",v_correspondentname AS "CorrespondentName",v_periodfrom::timestamp AS "PeriodFrom",v_periodto::timestamp AS "PeriodTo",v_hawalacount AS "HawalaCount",v_totalbaseafn AS "TotalBaseAfn",v_totalcommissionafn AS "TotalCommissionAfn",v_totalcommissionusd AS "TotalCommissionUsd")q),
            (SELECT coalesce(json_agg(q),'[]'::json) FROM(SELECT "CurrencyId","CurrencyCode",sum("SourceAmount")::numeric(18,4) AS "TotalAmount",max("SourceToAfnRate") AS "SourceToAfnRate" FROM "Eligible" GROUP BY "CurrencyId","CurrencyCode" ORDER BY "CurrencyCode")q),
            (SELECT coalesce(json_agg(q),'[]'::json) FROM(SELECT "HawalaId","HawalaNumber","HawalaDate","CurrencyId","CurrencyCode","SourceAmount","SourceToAfnRate","AfnEquivalent"::numeric(18,4) AS "AfnEquivalent","CommissionAfn"::numeric(18,4) AS "CommissionAfn","PaymentLocationId","PaymentLocationName","PerLakhRate","ValuationDate" FROM "Eligible" ORDER BY "HawalaDate","HawalaNumber")q),
            (SELECT coalesce(json_agg(q),'[]'::json) FROM(SELECT "PaymentLocationId","PaymentLocationName",max("PerLakhRate") AS "PerLakhRate",count(*)::integer AS "HawalaCount",sum("SourceAmount")::numeric(18,4) AS "TotalAmount",
                sum(CASE WHEN v_commissionscope NOT IN('Origin','Forwarding') AND "CurrencyId"=v_afncurrencyid THEN "CommissionAfn" ELSE 0 END)::numeric(18,4) AS "TotalCommissionAfn",
                sum(CASE WHEN v_commissionscope IN('Origin','Forwarding') OR "CurrencyId"=v_usdcurrencyid THEN "CommissionAfn" ELSE 0 END)::numeric(18,4) AS "TotalCommissionUsd",sum("AfnEquivalent")::numeric(18,4) AS "TotalDebitUsd"
                FROM "Eligible" WHERE v_hawalatype='HawalaSend' OR v_commissionscope='Origin' GROUP BY "PaymentLocationId","PaymentLocationName" ORDER BY "PaymentLocationName")q)) INTO result;
    ELSE
        IF v_hawalacount=0 THEN RAISE EXCEPTION 'حواله محاسبه‌نشده‌ای در این دوره وجود ندارد.'; END IF;
        IF(v_commissionscope='Destination' AND NOT EXISTS(SELECT 1 FROM "Eligible" WHERE "CommissionAfn">0)) OR(v_commissionscope='Forwarding' AND v_totalcommissionusd<=0)
            OR(v_hawalatype='HawalaReceive' AND v_totalcommissionusd<=0) OR(v_hawalatype='HawalaSend' AND v_commissionscope NOT IN('Destination','Forwarding') AND v_totalbaseafn<=0) THEN RAISE EXCEPTION 'کمیشن نهایی پس از گردکردن قابل ثبت نیست.'; END IF;
        IF v_usdcurrencyid IS NULL THEN RAISE EXCEPTION 'ارز فعال USD در سیستم یافت نشد.'; END IF;
        SELECT "Id" INTO v_account FROM public."Accounts" WHERE "TenantId"=v_tenantid AND "CorrespondentId"=v_correspondentid AND NOT "IsArchived" ORDER BY "Id" LIMIT 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'حساب فعال نمایندگی یافت نشد.'; END IF;
        IF v_commissionscope<>'Destination' THEN v_income:=hawala.system_account(v_tenantid,CASE v_commissionscope WHEN 'Incoming' THEN 'SYS-COMMISSION-INCOMING' WHEN 'Forwarding' THEN 'SYS-COMMISSION-FORWARDING' ELSE '3001' END,
            CASE v_commissionscope WHEN 'Incoming' THEN 'درآمد کمیشن حواله‌های دریافتی' WHEN 'Forwarding' THEN 'درآمد کمیشن ارسالی به نمایندگی‌ها' ELSE 'کارمزد حواله' END,'Income'); END IF;
        IF v_commissionscope='Destination' THEN v_expense:=hawala.system_account(v_tenantid,'5002','هزینه کمیشن پرداختی به نمایندگی‌ها','Expense');
        ELSIF v_hawalatype='HawalaSend' AND v_commissionscope<>'Forwarding' THEN
            v_clearing:=hawala.system_account(v_tenantid,'SYS-SETTLEMENT-CLEARING','حساب واسط تبدیل ارز نمایندگی‌ها','CurrencyConversionClearing');
            IF EXISTS(SELECT 1 FROM "Eligible" e WHERE e."SourceCorrespondentId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public."Accounts" a WHERE a."TenantId"=v_tenantid AND a."CorrespondentId"=e."SourceCorrespondentId" AND NOT a."IsArchived")) THEN RAISE EXCEPTION 'حساب فعال نمایندگی فرستنده حواله یافت نشد.'; END IF;
            IF EXISTS(SELECT 1 FROM "Eligible" WHERE "SourceCorrespondentId" IS NULL) THEN v_expense:=hawala.system_account(v_tenantid,'5002','هزینه کمیشن حواله‌های ارسالی','Expense'); END IF;
        END IF;
        SELECT "Id" INTO v_branch FROM public."Branches" WHERE "TenantId"=v_tenantid ORDER BY ("Code"='HQ') DESC,"Id" LIMIT 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('PeriodicCommissionNumber:'||v_tenantid,0));
        v_prefix:='PC-'||to_char(v_now,'YYYYMMDD')||'-';
        SELECT coalesce(max(CASE WHEN suffix COLLATE "C" ~ '^[0-9]{1,10}$' THEN CASE WHEN suffix::numeric<=2147483646 THEN suffix::integer END END),0)+1 INTO v_next
            FROM(SELECT substring("TransactionNo",length(v_prefix)+1) AS suffix FROM public."Transactions" WHERE "TenantId"=v_tenantid AND left("TransactionNo",length(v_prefix))=v_prefix)n;
        v_remarks:=CASE WHEN v_commissionscope='Forwarding' THEN 'کمیشن ارسالی به نمایندگی‌ها، فرستنده ' WHEN v_commissionscope='Destination' THEN 'کمیشن پرداختی به نمایندگی '
            WHEN v_commissionscope='Origin' THEN 'کمیشن ارسال حواله‌های مبدأ، نمایندگی ' WHEN v_hawalatype='HawalaSend' THEN 'کمیشن دوره‌ای حواله‌های ارسالی نمایندگی ' ELSE 'کمیشن دوره‌ای حواله‌های دریافتی نمایندگی ' END||v_correspondentname||' از '||v_periodfrom||' تا '||v_periodto;
        INSERT INTO public."Transactions"("TenantId","TransactionNo","TransactionType","BranchId","Status","Remarks","CreatedBy","CreatedAt")
            VALUES(v_tenantid,v_prefix||lpad(v_next::text,greatest(4,length(v_next::text)),'0'),CASE WHEN v_commissionscope='Forwarding' THEN 'PeriodicForwardingCommission' WHEN v_hawalatype='HawalaSend' THEN 'PeriodicOutgoingCommission' ELSE 'PeriodicCorrespondentCommission' END,v_branch,'Paid',v_remarks,v_currentuserid,v_now) RETURNING "Id" INTO v_transaction;
        INSERT INTO public."CorrespondentCommissionBatches"("TenantId","CorrespondentId","PeriodFrom","PeriodTo","CommissionPerLakhAfn","UsdToAfnRate","TotalBaseAfn","TotalCommissionAfn","TotalCommissionUsd","Status","PostingTransactionId","CreatedBy","CreatedAt","CommissionScope","AccountingVersion")
            VALUES(v_tenantid,v_correspondentid,v_periodfrom,v_periodto,v_commissionperlakhafn,v_usdtoafnrate,v_totalbaseafn,v_totalcommissionafn,v_totalcommissionusd,'Posted',v_transaction,v_currentuserid,v_now,v_commissionscope,CASE WHEN v_commissionscope IN('Incoming','Destination','Forwarding') THEN 3 ELSE 1 END) RETURNING "Id" INTO v_batch;
        INSERT INTO public."CorrespondentCommissionBatchItems"("TenantId","BatchId","HawalaId","SourceCurrencyId","SourceAmount","SourceToAfnRate","AfnEquivalent","CommissionAfn","IsActive","PaymentLocationId","PaymentLocationName","PerLakhRate","CommissionScope","ValuationDate")
            SELECT v_tenantid,v_batch,"HawalaId","CurrencyId","SourceAmount","SourceToAfnRate","AfnEquivalent"::numeric(18,4),"CommissionAfn"::numeric(18,4),true,"PaymentLocationId","PaymentLocationName","PerLakhRate",v_itemscope,"ValuationDate" FROM "Eligible";
        v_description:=CASE WHEN v_commissionscope='Forwarding' THEN 'کمیشن ارسالی به نمایندگی‌ها، فرستنده ' WHEN v_commissionscope='Destination' THEN 'کمیشن پرداختی به نمایندگی ' WHEN v_commissionscope='Origin' THEN 'کمیشن مبدأ حواله‌های ارسالی نمایندگی ' WHEN v_hawalatype='HawalaSend' THEN 'کمیشن حواله‌های ارسالی نمایندگی ' ELSE 'کمیشن حواله‌های دریافتی نمایندگی ' END||v_correspondentname||'، '||v_hawalacount||' حواله';
        v_postingamount:=CASE WHEN v_commissionscope='Forwarding' THEN v_totalcommissionusd WHEN v_hawalatype='HawalaSend' THEN v_totalbaseafn ELSE v_totalcommissionusd END;
        IF v_hawalatype='HawalaReceive' OR v_commissionscope='Forwarding' THEN
            INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt") VALUES
                (v_tenantid,v_transaction,v_account,v_usdcurrencyid,0,v_totalcommissionusd,v_description,v_now),(v_tenantid,v_transaction,v_income,v_usdcurrencyid,v_totalcommissionusd,0,v_description,v_now);
        ELSIF v_commissionscope='Destination' THEN
            INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
            SELECT v_tenantid,v_transaction,s.account_id,e."CurrencyId",CASE WHEN s.expense=0 THEN sum(e."CommissionAfn") ELSE 0 END,CASE WHEN s.expense=1 THEN sum(e."CommissionAfn") ELSE 0 END,v_description,v_now
                FROM "Eligible" e CROSS JOIN(VALUES(v_account,0),(v_expense,1))s(account_id,expense) GROUP BY s.account_id,s.expense,e."CurrencyId" HAVING sum(e."CommissionAfn")>0;
        ELSE
            IF v_totalcommissionusd>0 THEN INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt") VALUES(v_tenantid,v_transaction,v_account,v_usdcurrencyid,v_totalcommissionusd,0,v_description,v_now); END IF;
            IF v_totalcommissionafn>0 THEN
                INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt") VALUES(v_tenantid,v_transaction,v_account,v_afncurrencyid,v_totalcommissionafn,0,v_description,v_now),(v_tenantid,v_transaction,v_clearing,v_afncurrencyid,0,v_totalcommissionafn,v_description,v_now);
                INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt") SELECT v_tenantid,v_transaction,v_clearing,v_usdcurrencyid,sum("AfnEquivalent")::numeric(18,4),0,v_description,v_now FROM "Eligible" WHERE "CurrencyId"=v_afncurrencyid;
            END IF;
            INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
                SELECT v_tenantid,v_transaction,a."Id",v_usdcurrencyid,0,sum(e."AfnEquivalent")::numeric(18,4),v_description,v_now FROM "Eligible" e
                CROSS JOIN LATERAL(SELECT "Id" FROM public."Accounts" WHERE "TenantId"=v_tenantid AND "CorrespondentId"=e."SourceCorrespondentId" AND NOT "IsArchived" ORDER BY "Id" LIMIT 1)a
                WHERE e."SourceCorrespondentId" IS NOT NULL GROUP BY a."Id";
            IF v_expense IS NOT NULL THEN INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
                SELECT v_tenantid,v_transaction,v_expense,v_usdcurrencyid,0,sum(e."AfnEquivalent")::numeric(18,4),'هزینه کمیشن حواله‌های ارسالی از صرافی خود ما',v_now FROM "Eligible" e WHERE e."SourceCorrespondentId" IS NULL; END IF;
        END IF;
        INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
            VALUES(v_tenantid,v_currentuserid,gen_random_uuid(),'POST_PERIODIC_COMMISSION','CorrespondentCommissionBatches',v_batch,'کمیشن '||v_hawalacount||' حواله به مبلغ '||CASE WHEN v_commissionscope='Destination' THEN v_totalcommissionafn||' AFN / '||v_totalcommissionusd||' USD' ELSE v_postingamount||' USD' END||' ثبت شد.',v_now);
        SELECT json_build_array((SELECT json_agg(q) FROM(SELECT v_batch AS "Id",v_correspondentid AS "CorrespondentId",v_correspondentname AS "CorrespondentName",v_periodfrom::timestamp AS "PeriodFrom",v_periodto::timestamp AS "PeriodTo",v_hawalacount AS "HawalaCount",v_commissionperlakhafn AS "CommissionPerLakhAfn",v_usdtoafnrate AS "UsdToAfnRate",v_totalbaseafn AS "TotalBaseAfn",v_totalcommissionafn AS "TotalCommissionAfn",v_totalcommissionusd AS "TotalCommissionUsd",'Posted' AS "Status",v_now AS "CreatedAt")q)) INTO result;
    END IF;
    PERFORM set_config('lock_timeout',v_previous_timeout,true);
    RETURN result;
END $body$;
