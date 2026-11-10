CREATE OR REPLACE FUNCTION hawala.append_validation(current_value text,reason text) RETURNS text LANGUAGE sql IMMUTABLE AS $body$
    SELECT CASE WHEN nullif(current_value,'') IS NULL THEN reason ELSE current_value||' | '||reason END;
$body$;

CREATE OR REPLACE FUNCTION hawala.validate_import(p_tenant bigint,p_batch bigint) RETURNS void LANGUAGE plpgsql AS $body$
DECLARE b public."HawalaImportBatches"%ROWTYPE; period_start timestamp;
BEGIN
    SELECT * INTO b FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'پیش‌نمایش آپلود پیدا نشد.'; END IF;
    IF b."Status"<>'Preview' THEN RAISE EXCEPTION 'این پیش‌نمایش دیگر قابل اعتبارسنجی نیست.'; END IF;
    IF (SELECT count(*) FROM public."HawalaImportRows" WHERE "TenantId"=p_tenant AND "BatchId"=p_batch)<>b."RowCount" THEN RAISE EXCEPTION 'تعداد ردیف‌های جدول آماده‌سازی با فایل برابر نیست.'; END IF;
    SELECT coalesce(max("PeriodTo"),timestamp '0001-01-01') INTO period_start FROM public."CorrespondentAccountPeriods"
        WHERE "TenantId"=p_tenant AND "CorrespondentId"=b."CorrespondentId";
    UPDATE public."HawalaImportRows" r SET "CurrencyId"=c."Id","CurrencyCode"=c."Code" FROM public."Currencies" c
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND c."TenantId"=r."TenantId" AND c."IsActive" AND upper(btrim(c."Code"))=upper(btrim(r."CurrencyCode"));
    WITH resolved AS (
        SELECT r."Id",coalesce(cc."Id",r."CurrencyId") AS currency_id,coalesce(cc."Code",c."Code") AS currency_code
        FROM public."HawalaImportRows" r LEFT JOIN public."Currencies" cc ON cc."TenantId"=r."TenantId" AND cc."IsActive"
            AND upper(btrim(cc."Code"))=upper(btrim(r."AgentCommissionCurrencyCode"))
        LEFT JOIN public."Currencies" c ON c."TenantId"=r."TenantId" AND c."Id"=r."CurrencyId" AND c."IsActive"
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND r."AgentCommissionAmount" IS NOT NULL
    ) UPDATE public."HawalaImportRows" r SET "AgentCommissionCurrencyId"=x.currency_id,"AgentCommissionCurrencyCode"=x.currency_code FROM resolved x WHERE r."Id"=x."Id" AND r."TenantId"=p_tenant;
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'شماره حواله در همین فایل تکراری است.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND r."HawalaNumber" IS NOT NULL AND EXISTS(
            SELECT 1 FROM public."HawalaImportRows" d WHERE d."TenantId"=r."TenantId" AND d."BatchId"=r."BatchId" AND d."HawalaNumber"=r."HawalaNumber" AND d."Id"<>r."Id");
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'رفرنس در همین فایل تکراری است.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND nullif(r."ReferenceNumber",'') IS NOT NULL AND EXISTS(
            SELECT 1 FROM public."HawalaImportRows" d WHERE d."TenantId"=r."TenantId" AND d."BatchId"=r."BatchId" AND d."ReferenceNumber"=r."ReferenceNumber" AND d."Id"<>r."Id");
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'شماره حواله قبلاً برای این نمایندگی ثبت شده است.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND EXISTS(SELECT 1 FROM public."Hawalas" h WHERE h."TenantId"=p_tenant AND h."CorrespondentId"=b."CorrespondentId"
            AND h."HawalaType"='HawalaReceive' AND h."Number"=r."HawalaNumber" AND h."CreatedAt">=period_start);
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'رفرنس قبلاً برای این نمایندگی ثبت شده است.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND nullif(r."ReferenceNumber",'') IS NOT NULL AND EXISTS(SELECT 1 FROM public."Hawalas" h WHERE h."TenantId"=p_tenant AND h."CorrespondentId"=b."CorrespondentId"
            AND h."HawalaType"='HawalaReceive' AND h."ReferenceNumber"=r."ReferenceNumber" AND h."CreatedAt">=period_start);
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'ارز تعریف یا فعال نیست.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND (r."ValidationErrors" IS NULL OR r."ValidationErrors" NOT LIKE '%ارز «%')
        AND NOT EXISTS(SELECT 1 FROM public."Currencies" c WHERE c."TenantId"=p_tenant AND c."Id"=r."CurrencyId" AND c."IsActive");
    UPDATE public."HawalaImportRows" r SET "ValidationErrors"=hawala.append_validation(r."ValidationErrors",'ارز کمیشن تعریف یا فعال نیست.')
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND r."AgentCommissionAmount" IS NOT NULL AND (r."ValidationErrors" IS NULL OR r."ValidationErrors" NOT LIKE '%ارز کمیشن%')
        AND NOT EXISTS(SELECT 1 FROM public."Currencies" c WHERE c."TenantId"=p_tenant AND c."Id"=r."AgentCommissionCurrencyId" AND c."IsActive");
END $body$;

CREATE OR REPLACE FUNCTION hawala.prepare_import(p_tenant bigint,p_batch bigint,p_own bigint,p_mappings jsonb) RETURNS void LANGUAGE plpgsql AS $body$
DECLARE expected integer;
BEGIN
    SELECT "RowCount" INTO expected FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch AND "Status"='Preview' FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Import preview is not available.'; END IF;
    IF p_mappings IS NULL OR jsonb_typeof(p_mappings)<>'array' OR jsonb_array_length(p_mappings)<>(SELECT count(*) FROM public."HawalaImportRows" WHERE "TenantId"=p_tenant AND "BatchId"=p_batch)
        OR (SELECT count(DISTINCT (m->>'RowId')::bigint) FROM jsonb_array_elements(p_mappings) m)<>jsonb_array_length(p_mappings)
        OR EXISTS(SELECT 1 FROM jsonb_array_elements(p_mappings) m WHERE NOT EXISTS(SELECT 1 FROM public."HawalaImportRows" r WHERE r."Id"=(m->>'RowId')::bigint AND r."TenantId"=p_tenant AND r."BatchId"=p_batch)) THEN RAISE EXCEPTION 'Import preparation row mismatch.'; END IF;
    UPDATE public."HawalaImportRows" r SET "PaymentLocationId"=(m->>'PaymentLocationId')::bigint,"DestinationCorrespondentId"=(m->>'DestinationCorrespondentId')::bigint
        FROM jsonb_array_elements(p_mappings) m WHERE r."Id"=(m->>'RowId')::bigint AND r."TenantId"=p_tenant AND r."BatchId"=p_batch;
    UPDATE public."HawalaImportBatches" SET "OwnPaymentLocationId"=p_own,"LocationsPreparedAt"=CURRENT_TIMESTAMP AT TIME ZONE 'UTC' WHERE "TenantId"=p_tenant AND "Id"=p_batch;
END $body$;

CREATE OR REPLACE FUNCTION hawala.finalize_import(p_tenant bigint,p_batch bigint,p_actor bigint,p_mappings jsonb) RETURNS void LANGUAGE plpgsql AS $body$
DECLARE b public."HawalaImportBatches"%ROWTYPE;
BEGIN
    SELECT * INTO b FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'پیش‌نمایش آپلود پیدا نشد.'; END IF;
    IF b."Status"<>'Preview' THEN RAISE EXCEPTION 'این پیش‌نمایش قبلاً ثبت شده یا دیگر قابل استفاده نیست.'; END IF;
    IF p_mappings IS NULL OR jsonb_typeof(p_mappings)<>'array' OR jsonb_array_length(p_mappings)<>b."RowCount" OR
        (SELECT count(DISTINCT (m->>'RowId')::bigint) FROM jsonb_array_elements(p_mappings) m)<>b."RowCount" THEN RAISE EXCEPTION 'نتیجه ثبت با تعداد ردیف‌های پیش‌نمایش برابر نیست.'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p_mappings) m WHERE NOT EXISTS(SELECT 1 FROM public."HawalaImportRows" r
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND r."Id"=(m->>'RowId')::bigint)) THEN RAISE EXCEPTION 'یکی از ردیف‌های نتیجه متعلق به این پیش‌نمایش نیست.'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(p_mappings) m LEFT JOIN public."Hawalas" h
        ON h."TenantId"=p_tenant AND h."Id"=(m->>'HawalaId')::bigint AND h."HawalaType"='HawalaReceive'
        LEFT JOIN public."Hawalas" g ON g."TenantId"=p_tenant AND g."Id"=(m->>'GeneratedSendHawalaId')::bigint AND g."HawalaType"='HawalaSend' AND g."SourceHawalaId"=(m->>'HawalaId')::bigint
        WHERE h."Id" IS NULL OR(m->>'GeneratedSendHawalaId' IS NOT NULL AND g."Id" IS NULL)) THEN RAISE EXCEPTION 'ارتباط حواله‌های ثبت‌شده با پیش‌نمایش معتبر نیست.'; END IF;
    UPDATE public."HawalaImportRows" r SET "HawalaId"=(m->>'HawalaId')::bigint,"GeneratedSendHawalaId"=(m->>'GeneratedSendHawalaId')::bigint,
        "PaymentLocationId"=(m->>'PaymentLocationId')::bigint,"DestinationCorrespondentId"=(m->>'DestinationCorrespondentId')::bigint,
        "AgentCommissionAmount"=(m->>'AgentCommissionAmount')::numeric,"AgentCommissionCurrencyId"=(m->>'AgentCommissionCurrencyId')::bigint,
        "AgentCommissionCurrencyCode"=(SELECT c."Code" FROM public."Currencies" c WHERE c."TenantId"=p_tenant AND c."Id"=(m->>'AgentCommissionCurrencyId')::bigint)
    FROM jsonb_array_elements(p_mappings) m WHERE r."Id"=(m->>'RowId')::bigint AND r."TenantId"=p_tenant AND r."BatchId"=p_batch;
    UPDATE public."HawalaImportBatches" SET "Status"='Posted',"ConfirmedAt"=CURRENT_TIMESTAMP AT TIME ZONE 'UTC',"ConfirmedBy"=p_actor WHERE "TenantId"=p_tenant AND "Id"=p_batch;
END $body$;
