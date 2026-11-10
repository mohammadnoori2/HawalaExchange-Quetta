CREATE OR REPLACE FUNCTION hawala.capture_settlement(p_tenant bigint,p_conversion bigint,p_actor bigint,p_rate_date date DEFAULT NULL,p_offset integer DEFAULT 0,p_confirm boolean DEFAULT false,p_backfill boolean DEFAULT false)
RETURNS void LANGUAGE plpgsql AS $body$
DECLARE owner bigint; target bigint; mode text; target_code text; target_priority integer; usd bigint; snapshot text;
BEGIN
    SELECT c."CorrespondentId",c."TargetCurrencyId",c."SourceMode",cur."Code",cur."QuotationPriority" INTO owner,target,mode,target_code,target_priority
        FROM public."CorrespondentSettlementConversions" c JOIN public."Currencies" cur ON cur."TenantId"=c."TenantId" AND cur."Id"=c."TargetCurrencyId"
        WHERE c."TenantId"=p_tenant AND c."Id"=p_conversion;
    IF NOT FOUND THEN RAISE EXCEPTION 'تبدیل نمایندگی یافت نشد.'; END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('CorrespondentCommissionPeriod:'||p_tenant||':'||owner,0));
    DROP TABLE IF EXISTS pg_temp."CaptureRates";
    CREATE TEMP TABLE "CaptureRates"(rate_date date,source_currency bigint,exchange_rate numeric(18,8),unit_to_target numeric(28,12)) ON COMMIT DROP;
    INSERT INTO "CaptureRates" SELECT(h."CreatedAt"+p_offset*INTERVAL '1 minute')::date,i."SourceCurrencyId",i."ExchangeRate",
        CASE WHEN cur."QuotationPriority"<target_priority OR(cur."QuotationPriority"=target_priority AND cur."Code"<target_code) THEN i."ExchangeRate" ELSE 1::numeric(28,12)/i."ExchangeRate" END
        FROM public."CorrespondentSettlementConversionHawalaItems" i JOIN public."Hawalas" h ON h."TenantId"=i."TenantId" AND h."Id"=i."HawalaId"
        JOIN public."Currencies" cur ON cur."TenantId"=i."TenantId" AND cur."Id"=i."SourceCurrencyId" WHERE i."TenantId"=p_tenant AND i."ConversionId"=p_conversion;
    IF mode='Account' THEN INSERT INTO "CaptureRates" SELECT coalesce(p_rate_date,(c."CreatedAt"+p_offset*INTERVAL '1 minute')::date),i."SourceCurrencyId",i."ExchangeRate",
        CASE WHEN cur."QuotationPriority"<target_priority OR(cur."QuotationPriority"=target_priority AND cur."Code"<target_code) THEN i."ExchangeRate" ELSE 1::numeric(28,12)/i."ExchangeRate" END
        FROM public."CorrespondentSettlementConversionItems" i JOIN public."CorrespondentSettlementConversions" c ON c."TenantId"=i."TenantId" AND c."Id"=i."ConversionId"
        JOIN public."Currencies" cur ON cur."TenantId"=i."TenantId" AND cur."Id"=i."SourceCurrencyId" WHERE i."TenantId"=p_tenant AND i."ConversionId"=p_conversion; END IF;
    IF NOT p_backfill AND EXISTS(SELECT 1 FROM "CaptureRates" GROUP BY rate_date,source_currency HAVING min(exchange_rate)<>max(exchange_rate)) THEN RAISE EXCEPTION 'برای هر روز و ارز یک نرخ یکسان وارد کنید؛ هر روز را جدا ثبت کنید.'; END IF;
    IF NOT p_confirm AND EXISTS(SELECT 1 FROM "CaptureRates" r JOIN public."CorrespondentSettlementDailyRates" d ON d."TenantId"=p_tenant AND d."CorrespondentId"=owner AND d."RateDate"=r.rate_date
        AND d."SourceCurrencyId"=r.source_currency AND d."TargetCurrencyId"=target WHERE d."ExchangeRate"<>r.exchange_rate) THEN RAISE EXCEPTION 'نرخ این روز و ارز قبلاً متفاوت ثبت شده است؛ گزینه تأیید نرخ متفاوت را بررسی کنید.'; END IF;
    INSERT INTO public."CorrespondentSettlementDailyRates"("TenantId","CorrespondentId","RateDate","SourceCurrencyId","TargetCurrencyId","ExchangeRate","UnitToTarget","ModifiedAt","ModifiedBy")
        SELECT p_tenant,owner,rate_date,source_currency,target,max(exchange_rate),max(unit_to_target),CURRENT_TIMESTAMP AT TIME ZONE 'UTC',p_actor FROM "CaptureRates" GROUP BY rate_date,source_currency
        ON CONFLICT("TenantId","CorrespondentId","RateDate","SourceCurrencyId","TargetCurrencyId") DO UPDATE SET "ExchangeRate"=EXCLUDED."ExchangeRate","UnitToTarget"=EXCLUDED."UnitToTarget","ModifiedAt"=EXCLUDED."ModifiedAt","ModifiedBy"=EXCLUDED."ModifiedBy";
    SELECT "Id" INTO usd FROM public."Currencies" WHERE "TenantId"=p_tenant AND "Code"='USD';
    DELETE FROM public."CorrespondentSettlementValuations" WHERE "TenantId"=p_tenant AND "ConversionId"=p_conversion;
    INSERT INTO public."CorrespondentSettlementValuations"("TenantId","HawalaId","ConversionId","CorrespondentId","RateDate","SourceCurrencyId","TargetCurrencyId","SourceAmount","ExchangeRate","UnitToTarget","TargetAmount","UsdAmount")
    SELECT p_tenant,h."Id",p_conversion,owner,(h."CreatedAt"+p_offset*INTERVAL '1 minute')::date,i."SourceCurrencyId",target,a.source_amount,i."ExchangeRate",u.unit_to_target,t.amount,
        CASE WHEN i."SourceCurrencyId"=usd THEN a.source_amount WHEN target=usd THEN t.amount WHEN bridge."UnitToTarget">0 THEN round(t.amount/bridge."UnitToTarget",8) ELSE NULL END
    FROM public."CorrespondentSettlementConversionHawalaItems" i JOIN public."Hawalas" h ON h."TenantId"=i."TenantId" AND h."Id"=i."HawalaId"
    JOIN public."Currencies" cur ON cur."TenantId"=i."TenantId" AND cur."Id"=i."SourceCurrencyId"
    CROSS JOIN LATERAL(SELECT CASE WHEN h."HawalaType"='HawalaSend' THEN coalesce(h."ToAmount",h."FromAmount") ELSE h."FromAmount" END AS source_amount,
        CASE WHEN h."HawalaType"='HawalaSend' THEN h."ToCurrencyId" ELSE h."FromCurrencyId" END AS principal_currency)a
    CROSS JOIN LATERAL(SELECT(CASE WHEN cur."QuotationPriority"<target_priority OR(cur."QuotationPriority"=target_priority AND cur."Code"<target_code)
        THEN i."ExchangeRate" ELSE 1::numeric(28,12)/i."ExchangeRate" END)::numeric(28,12) AS unit_to_target)u
    CROSS JOIN LATERAL(SELECT round(CASE WHEN cur."QuotationPriority"<target_priority OR(cur."QuotationPriority"=target_priority AND cur."Code"<target_code)
        THEN a.source_amount*i."ExchangeRate" ELSE a.source_amount/i."ExchangeRate" END,8)::numeric(28,8) AS amount)t
    LEFT JOIN public."CorrespondentSettlementDailyRates" bridge ON bridge."TenantId"=p_tenant AND bridge."CorrespondentId"=owner AND bridge."TargetCurrencyId"=target
        AND bridge."SourceCurrencyId"=usd AND bridge."RateDate"=(h."CreatedAt"+p_offset*INTERVAL '1 minute')::date
    WHERE i."TenantId"=p_tenant AND i."ConversionId"=p_conversion AND a.principal_currency=i."SourceCurrencyId";
    UPDATE public."CorrespondentSettlementValuations" v SET "UsdAmount"=round(v."TargetAmount"/bridge."UnitToTarget",8)
        FROM public."CorrespondentSettlementDailyRates" bridge WHERE bridge."TenantId"=v."TenantId" AND bridge."CorrespondentId"=v."CorrespondentId"
        AND bridge."RateDate"=v."RateDate" AND bridge."TargetCurrencyId"=v."TargetCurrencyId" AND bridge."SourceCurrencyId"=usd
        AND v."TenantId"=p_tenant AND v."CorrespondentId"=owner AND v."UsdAmount" IS NULL;
    IF mode='Account' THEN
        SELECT coalesce(json_agg(json_strip_nulls(row_to_json(q))),'[]'::json)::text INTO snapshot FROM(
            SELECT coalesce(p_rate_date,(c."CreatedAt"+p_offset*INTERVAL '1 minute')::date) AS "RateDate",i."SourceCurrencyId",cur."Code" AS "SourceCurrencyCode",
                abs(i."SourceTalabKar"-i."SourceBadehKar") AS "SourceAmount",i."ExchangeRate",abs(i."TargetTalabKar"-i."TargetBadehKar") AS "TargetAmount"
            FROM public."CorrespondentSettlementConversionItems" i JOIN public."CorrespondentSettlementConversions" c ON c."TenantId"=i."TenantId" AND c."Id"=i."ConversionId"
            JOIN public."Currencies" cur ON cur."TenantId"=i."TenantId" AND cur."Id"=i."SourceCurrencyId" WHERE i."TenantId"=p_tenant AND i."ConversionId"=p_conversion)q;
    ELSE
        SELECT coalesce(json_agg(json_strip_nulls(row_to_json(q))),'[]'::json)::text INTO snapshot FROM(
            SELECT i."HawalaId",h."Number" AS "HawalaNumber",coalesce(h."ReferenceNumber",'') AS "ReferenceNumber",coalesce(h."SenderName",'') AS "SenderName",coalesce(h."ReceiverName",'') AS "ReceiverName",coalesce(loc."Name",'') AS "PaymentLocationName",
                (h."CreatedAt"+p_offset*INTERVAL '1 minute')::date AS "RateDate",i."SourceCurrencyId",cur."Code" AS "SourceCurrencyCode",abs(i."SourceTalabKar"-i."SourceBadehKar") AS "SourceAmount",i."ExchangeRate",
                abs(i."TargetTalabKar"-i."TargetBadehKar") AS "TargetAmount",v."TargetAmount" AS "PrincipalTargetAmount",v."UsdAmount"
            FROM public."CorrespondentSettlementConversionHawalaItems" i JOIN public."Hawalas" h ON h."TenantId"=i."TenantId" AND h."Id"=i."HawalaId"
            JOIN public."Currencies" cur ON cur."TenantId"=i."TenantId" AND cur."Id"=i."SourceCurrencyId" LEFT JOIN public."PaymentLocations" loc ON loc."TenantId"=h."TenantId" AND loc."Id"=h."PaymentLocationId"
            LEFT JOIN public."CorrespondentSettlementValuations" v ON v."TenantId"=i."TenantId" AND v."HawalaId"=i."HawalaId" WHERE i."TenantId"=p_tenant AND i."ConversionId"=p_conversion ORDER BY h."CreatedAt",h."Number",cur."Code")q;
    END IF;
    UPDATE public."CorrespondentSettlementHistorySnapshots" SET "Status"='Superseded' WHERE "TenantId"=p_tenant AND "ConversionId"=p_conversion AND "Status"='Active';
    INSERT INTO public."CorrespondentSettlementHistorySnapshots"("TenantId","CorrespondentId","ConversionId","CreatedAt","CreatedBy","HawalaCount","SnapshotJson")
        VALUES(p_tenant,owner,p_conversion,CURRENT_TIMESTAMP AT TIME ZONE 'UTC',p_actor,(SELECT count(*) FROM public."CorrespondentSettlementConversionHawalas" WHERE "TenantId"=p_tenant AND "ConversionId"=p_conversion),snapshot);
END $body$;
