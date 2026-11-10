CREATE OR REPLACE FUNCTION hawala.reverse_import_ledger(p_tenant bigint,p_actor bigint,p_ids jsonb) RETURNS void LANGUAGE plpgsql AS $body$
BEGIN
    IF p_ids IS NULL OR jsonb_typeof(p_ids)<>'array' THEN RAISE EXCEPTION 'Invalid import correction identifiers.'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements_text(p_ids)i WHERE NOT EXISTS(SELECT 1 FROM public."HawalaImportRows" r WHERE r."TenantId"=p_tenant AND(r."HawalaId"=i.value::bigint OR r."GeneratedSendHawalaId"=i.value::bigint))) THEN RAISE EXCEPTION 'حواله متعلق به آپلود صرافی جاری نیست.'; END IF;
    IF EXISTS(SELECT 1 FROM public."CorrespondentSettlementConversionHawalas" c WHERE c."TenantId"=p_tenant AND c."HawalaId" IN(SELECT value::bigint FROM jsonb_array_elements_text(p_ids))) THEN RAISE EXCEPTION 'ابتدا تبدیل ارز توافقی مرتبط با حواله را برگشت دهید.'; END IF;
    PERFORM 1 FROM public."Hawalas" WHERE "TenantId"=p_tenant AND "Id" IN(SELECT value::bigint FROM jsonb_array_elements_text(p_ids)) FOR UPDATE;
    INSERT INTO public."LedgerEntries"("TenantId","HawalaId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
        SELECT p_tenant,h."Id",e."AccountId",e."CurrencyId",greatest(-sum(e."TalabKar"-e."BadehKar"),0),greatest(sum(e."TalabKar"-e."BadehKar"),0),
            'اصلاح حواله آپلود گروهی — برگشت مانده ثبت قبلی',CURRENT_TIMESTAMP AT TIME ZONE 'UTC'
        FROM public."Hawalas" h JOIN public."LedgerEntries" e ON e."TenantId"=p_tenant AND e."HawalaId"=h."Id"
        WHERE h."TenantId"=p_tenant AND h."Id" IN(SELECT value::bigint FROM jsonb_array_elements_text(p_ids)) AND h."Status"<>'Cancel'
        GROUP BY h."Id",e."AccountId",e."CurrencyId" HAVING sum(e."TalabKar"-e."BadehKar")<>0;
END $body$;

CREATE OR REPLACE FUNCTION hawala.delete_import_accounting(p_tenant bigint,p_actor bigint,p_batch bigint,p_ids jsonb) RETURNS void LANGUAGE plpgsql AS $body$
DECLARE closed boolean;now_utc timestamp:=CURRENT_TIMESTAMP AT TIME ZONE 'UTC';branch bigint;posting bigint;
BEGIN
    PERFORM 1 FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch AND "Status"='Posted' FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'فایل ثبت‌شده صرافی جاری یافت نشد.'; END IF;
    IF p_ids IS NULL OR jsonb_typeof(p_ids)<>'array' THEN RAISE EXCEPTION 'Invalid import identifiers.'; END IF;
    DROP TABLE IF EXISTS pg_temp."DeletionIds",pg_temp."DeletionOwned",pg_temp."DeletionItems",pg_temp."DeletionEntries";
    CREATE TEMP TABLE "DeletionIds"("Id" bigint PRIMARY KEY) ON COMMIT DROP;INSERT INTO "DeletionIds" SELECT DISTINCT value::bigint FROM jsonb_array_elements_text(p_ids);
    IF EXISTS(SELECT 1 FROM "DeletionIds" i WHERE NOT EXISTS(SELECT 1 FROM public."Hawalas" h WHERE h."TenantId"=p_tenant AND h."Id"=i."Id")) THEN RAISE EXCEPTION 'حواله صرافی جاری یافت نشد.'; END IF;
    CREATE TEMP TABLE "DeletionOwned" ON COMMIT DROP AS WITH RECURSIVE owned("Id") AS(
        SELECT DISTINCT i."Id" FROM "DeletionIds" i JOIN public."HawalaImportRows" r ON r."TenantId"=p_tenant AND r."BatchId"=p_batch AND(r."HawalaId"=i."Id" OR r."GeneratedSendHawalaId"=i."Id")
        UNION SELECT h."Id" FROM public."Hawalas" h JOIN owned o ON o."Id"=h."SourceHawalaId" OR o."Id"=h."CompensatesHawalaId" WHERE h."TenantId"=p_tenant)SELECT * FROM owned;
    IF EXISTS(SELECT "Id" FROM "DeletionIds" EXCEPT SELECT "Id" FROM "DeletionOwned") OR EXISTS(SELECT "Id" FROM "DeletionOwned" EXCEPT SELECT "Id" FROM "DeletionIds") THEN RAISE EXCEPTION 'حواله‌های وابسته تغییر کرده‌اند؛ دوباره پیش‌نمایش بگیرید.'; END IF;
    IF EXISTS(SELECT 1 FROM public."HawalaImportRows" r JOIN "DeletionIds" i ON i."Id"=r."HawalaId" OR i."Id"=r."GeneratedSendHawalaId" WHERE r."TenantId"=p_tenant AND r."BatchId"<>p_batch) THEN RAISE EXCEPTION 'حواله با فایل دیگری مشترک است؛ حذف خودکار مجاز نیست.'; END IF;
    CREATE TEMP TABLE "DeletionItems" ON COMMIT DROP AS SELECT s."Id",s."ConversionId",s."HawalaId" FROM public."CorrespondentSettlementConversionHawalaItems" s JOIN "DeletionIds" i ON i."Id"=s."HawalaId" WHERE s."TenantId"=p_tenant;
    PERFORM 1 FROM public."LedgerEntries" e WHERE e."TenantId"=p_tenant AND(e."HawalaId" IN(SELECT "Id" FROM "DeletionIds") OR e."SettlementHawalaItemId" IN(SELECT "Id" FROM "DeletionItems")
        OR e."TransactionId" IN(SELECT t."Id" FROM public."Transactions" t JOIN "DeletionIds" i ON i."Id"=t."HawalaId" WHERE t."TenantId"=p_tenant)) FOR UPDATE;
    CREATE TEMP TABLE "DeletionEntries" ON COMMIT DROP AS SELECT e.* FROM public."LedgerEntries" e WHERE e."TenantId"=p_tenant AND(e."HawalaId" IN(SELECT "Id" FROM "DeletionIds") OR e."SettlementHawalaItemId" IN(SELECT "Id" FROM "DeletionItems")
        OR e."TransactionId" IN(SELECT t."Id" FROM public."Transactions" t JOIN "DeletionIds" i ON i."Id"=t."HawalaId" WHERE t."TenantId"=p_tenant));
    closed:=EXISTS(SELECT 1 FROM public."CorrespondentAccountPeriodHawalas" h JOIN "DeletionIds" i ON i."Id"=h."HawalaId" WHERE h."TenantId"=p_tenant)
        OR EXISTS(SELECT 1 FROM "DeletionEntries" e JOIN public."Accounts" a ON a."TenantId"=p_tenant AND a."Id"=e."AccountId"
            JOIN public."CorrespondentAccountPeriods" period ON period."TenantId"=p_tenant AND period."CorrespondentId"=a."CorrespondentId" AND period."PeriodTo">=e."CreatedAt");
    IF closed THEN
        IF EXISTS(SELECT 1 FROM "DeletionEntries" e JOIN public."Accounts" a ON a."TenantId"=p_tenant AND a."Id"=e."AccountId"
            JOIN public."CorrespondentAccountPeriods" period ON period."TenantId"=p_tenant AND period."CorrespondentId"=a."CorrespondentId" WHERE period."PeriodTo">=now_utc) THEN RAISE EXCEPTION 'دوره باز برای ثبت اصلاحیه موجود نیست.'; END IF;
        SELECT "Id" INTO branch FROM public."Branches" WHERE "TenantId"=p_tenant ORDER BY "Id" LIMIT 1;
        INSERT INTO public."Transactions"("TenantId","TransactionNo","TransactionType","Status","CreatedAt","CreatedBy","BranchId","Remarks")
            VALUES(p_tenant,'IMPORT-DELETE-'||gen_random_uuid(),'ImportDeletionAdjustment','Paid',now_utc,p_actor,branch,'اصلاح حذف آپلود گروهی در دوره باز؛ سوابق دوره بسته حفظ شدند.') RETURNING "Id" INTO posting;
        INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
            SELECT p_tenant,posting,"AccountId","CurrencyId",greatest(-sum("TalabKar"-"BadehKar"),0),greatest(sum("TalabKar"-"BadehKar"),0),
                'حذف آپلود گروهی '||p_batch||'؛ برگشت اصل حواله و تبدیل مربوط',now_utc FROM "DeletionEntries" GROUP BY "AccountId","CurrencyId" HAVING sum("TalabKar"-"BadehKar")<>0;
        UPDATE public."LedgerEntries" e SET "SettlementHawalaItemId"=NULL FROM "DeletionItems" i WHERE i."Id"=e."SettlementHawalaItemId" AND e."TenantId"=p_tenant;
    ELSE DELETE FROM public."LedgerEntries" e USING "DeletionEntries" x WHERE x."Id"=e."Id" AND e."TenantId"=p_tenant; END IF;
    UPDATE public."Transactions" t SET "Status"='Cancel' FROM "DeletionIds" i WHERE i."Id"=t."HawalaId" AND t."TenantId"=p_tenant
        AND NOT EXISTS(SELECT 1 FROM public."LedgerEntries" e WHERE e."TenantId"=p_tenant AND e."TransactionId"=t."Id");
    DELETE FROM public."CorrespondentSettlementValuations" v USING "DeletionIds" i WHERE i."Id"=v."HawalaId" AND v."TenantId"=p_tenant;
    DELETE FROM public."CorrespondentSettlementConversionHawalaItems" v USING "DeletionIds" i WHERE i."Id"=v."HawalaId" AND v."TenantId"=p_tenant;
    DELETE FROM public."CorrespondentSettlementConversionHawalas" v USING "DeletionIds" i WHERE i."Id"=v."HawalaId" AND v."TenantId"=p_tenant;
    UPDATE public."Hawalas" h SET "IsDeleted"=true,"Status"='Cancel',"CancelledAt"=now_utc,"CancelledBy"=p_actor,"CancelReason"='حذف آپلود گروهی؛ اثر مالی حذف یا در دوره باز خنثی شد.' FROM "DeletionIds" i WHERE i."Id"=h."Id" AND h."TenantId"=p_tenant;
END $body$;

CREATE OR REPLACE FUNCTION hawala.delete_preview_import(p_tenant bigint,p_batch bigint) RETURNS json LANGUAGE plpgsql AS $body$
DECLARE b public."HawalaImportBatches"%ROWTYPE;deleted_count integer;old_timeout text;
BEGIN
    old_timeout:=current_setting('lock_timeout');PERFORM set_config('lock_timeout','20s',true);
    SELECT * INTO b FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'فایل آپلود گروهی یافت نشد.'; END IF;
    IF b."Status"<>'Preview' THEN RAISE EXCEPTION 'وضعیت فایل تغییر کرده است؛ تاریخچه را تازه‌سازی کنید.'; END IF;
    IF EXISTS(SELECT 1 FROM public."HawalaImportJobs" WHERE "TenantId"=p_tenant AND "BatchId"=p_batch AND "Status" IN('Queued','Running')) THEN RAISE EXCEPTION 'فایل در صف یا در حال ثبت است و قابل حذف نیست.'; END IF;
    DROP TABLE IF EXISTS pg_temp."PreviewDeletionIds";CREATE TEMP TABLE "PreviewDeletionIds"("Id" bigint PRIMARY KEY) ON COMMIT DROP;
    INSERT INTO "PreviewDeletionIds" SELECT DISTINCT h.id FROM public."HawalaImportRows" r CROSS JOIN LATERAL(VALUES(r."HawalaId"),(r."GeneratedSendHawalaId"))h(id)
        WHERE r."TenantId"=p_tenant AND r."BatchId"=p_batch AND h.id IS NOT NULL;
    SELECT count(*)::integer INTO deleted_count FROM "PreviewDeletionIds";
    IF EXISTS(SELECT 1 FROM public."CorrespondentCommissionBatchItems" i JOIN "PreviewDeletionIds" d ON d."Id"=i."HawalaId" WHERE i."TenantId"=p_tenant)
        OR EXISTS(SELECT 1 FROM public."CorrespondentSettlementConversionHawalas" i JOIN "PreviewDeletionIds" d ON d."Id"=i."HawalaId" WHERE i."TenantId"=p_tenant)
        OR EXISTS(SELECT 1 FROM public."CorrespondentAccountPeriodHawalas" i JOIN "PreviewDeletionIds" d ON d."Id"=i."HawalaId" WHERE i."TenantId"=p_tenant) THEN RAISE EXCEPTION 'یکی از حواله‌های فایل سابقه مالی دارد و قابل حذف مستقیم نیست.'; END IF;
    DELETE FROM public."LedgerEntries" e USING "PreviewDeletionIds" d WHERE d."Id"=e."HawalaId" AND e."TenantId"=p_tenant;
    DELETE FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch;
    DELETE FROM public."Hawalas" h USING "PreviewDeletionIds" d WHERE d."Id"=h."Id" AND h."TenantId"=p_tenant AND h."SourceHawalaId" IS NOT NULL;
    DELETE FROM public."Hawalas" h USING "PreviewDeletionIds" d WHERE d."Id"=h."Id" AND h."TenantId"=p_tenant;
    PERFORM set_config('lock_timeout',old_timeout,true);RETURN json_build_object('FileName',b."FileName",'RowCount',b."RowCount",'DeletedHawalaCount',deleted_count);
END $body$;

CREATE OR REPLACE FUNCTION hawala.save_import_draft(p_tenant bigint,p_batch bigint,p_rows jsonb) RETURNS void LANGUAGE plpgsql AS $body$
BEGIN
    PERFORM 1 FROM public."HawalaImportBatches" WHERE "TenantId"=p_tenant AND "Id"=p_batch AND "Status"='Preview' AND "LocationsPreparedAt" IS NOT NULL FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'ابتدا محل‌های پرداخت را آماده کنید؛ فقط پیش‌نویس قابل ذخیره است.'; END IF;
    IF EXISTS(SELECT 1 FROM public."HawalaImportJobs" WHERE "TenantId"=p_tenant AND "BatchId"=p_batch AND "Status" IN('Queued','Running')) THEN RAISE EXCEPTION 'فایل در صف ثبت است و قابل تغییر نیست.'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_to_recordset(p_rows)v("RowId" bigint,"Amount" numeric(18,4),"CurrencyId" bigint)
        LEFT JOIN public."HawalaImportRows" r ON r."TenantId"=p_tenant AND r."BatchId"=p_batch AND r."Id"=v."RowId"
        LEFT JOIN public."Currencies" c ON c."TenantId"=p_tenant AND c."Id"=v."CurrencyId" AND c."IsActive" WHERE r."Id" IS NULL OR v."Amount"<=0 OR(v."Amount" IS NOT NULL AND c."Id" IS NULL)) THEN RAISE EXCEPTION 'ردیف، مبلغ یا ارز کمیشن پیش‌نویس معتبر نیست.'; END IF;
    UPDATE public."HawalaImportRows" r SET "AgentCommissionAmount"=v."Amount","AgentCommissionCurrencyId"=CASE WHEN v."Amount" IS NULL THEN NULL ELSE v."CurrencyId" END
        FROM jsonb_to_recordset(p_rows)v("RowId" bigint,"Amount" numeric(18,4),"CurrencyId" bigint) WHERE r."Id"=v."RowId" AND r."TenantId"=p_tenant AND r."BatchId"=p_batch;
END $body$;
