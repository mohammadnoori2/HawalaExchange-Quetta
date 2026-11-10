CREATE OR REPLACE FUNCTION hawala.bulk_import_locations(p_tenant bigint,p_actor bigint,p_names jsonb)
RETURNS TABLE("NormalizedName" text,"Id" bigint) LANGUAGE plpgsql AS $body$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('HawalaImportPrepare:'||p_tenant,0));
    IF EXISTS(SELECT 1 FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) JOIN public."PaymentLocations" loc ON loc."TenantId"=p_tenant AND loc."NormalizedName"=n."NormalizedName" WHERE NOT loc."IsActive")
        OR EXISTS(SELECT 1 FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) JOIN public."PaymentLocationAliases" a ON a."TenantId"=p_tenant AND a."NormalizedName"=n."NormalizedName"
            JOIN public."PaymentLocations" loc ON loc."TenantId"=p_tenant AND loc."Id"=a."PaymentLocationId" WHERE NOT loc."IsActive") THEN RAISE EXCEPTION 'نام محل پرداخت با یک محل غیرفعال تکراری است.'; END IF;
    WITH created AS(INSERT INTO public."PaymentLocations" AS new_location("TenantId","Name","NormalizedName","Address","IsActive","CreatedAt","CreatedBy")
        SELECT p_tenant,n."Name",n."NormalizedName",'',true,CURRENT_TIMESTAMP AT TIME ZONE 'UTC',p_actor FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text)
        WHERE NOT EXISTS(SELECT 1 FROM public."PaymentLocations" loc WHERE loc."TenantId"=p_tenant AND loc."NormalizedName"=n."NormalizedName")
            AND NOT EXISTS(SELECT 1 FROM public."PaymentLocationAliases" a WHERE a."TenantId"=p_tenant AND a."NormalizedName"=n."NormalizedName") RETURNING new_location."Id",new_location."Name")
    INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
        SELECT p_tenant,p_actor,gen_random_uuid(),'CREATE','PaymentLocations',c."Id",json_build_object('Name',c."Name",'__Description','ایجاد محل پرداخت در آماده‌سازی گروهی')::text,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM created c;
    RETURN QUERY SELECT n."NormalizedName",loc."Id" FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) JOIN public."PaymentLocations" loc
        ON loc."TenantId"=p_tenant AND loc."IsActive" AND loc."NormalizedName"=n."NormalizedName"
        UNION ALL SELECT n."NormalizedName",loc."Id" FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) JOIN public."PaymentLocationAliases" a ON a."TenantId"=p_tenant AND a."NormalizedName"=n."NormalizedName"
        JOIN public."PaymentLocations" loc ON loc."TenantId"=p_tenant AND loc."Id"=a."PaymentLocationId" AND loc."IsActive"
        WHERE NOT EXISTS(SELECT 1 FROM public."PaymentLocations" direct WHERE direct."TenantId"=p_tenant AND direct."NormalizedName"=n."NormalizedName");
END $body$;

CREATE OR REPLACE FUNCTION hawala.bulk_import_correspondents(p_tenant bigint,p_actor bigint,p_names jsonb)
RETURNS TABLE("NormalizedName" text,"Id" bigint) LANGUAGE plpgsql AS $body$
DECLARE base_number bigint;account_base bigint;candidate_count integer;requested integer;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('HawalaImportPrepare:'||p_tenant,0));
    IF EXISTS(SELECT 1 FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) JOIN public."Correspondents" c ON c."TenantId"=p_tenant AND c."Name"=n."Name" AND NOT c."IsArchived") THEN RAISE EXCEPTION 'نمایندگی از قبل وجود دارد؛ آن را از فهرست موجود انتخاب کنید.'; END IF;
    SELECT coalesce(max(CASE WHEN suffix COLLATE "C" ~ '^[0-9]{1,19}$' THEN CASE WHEN suffix::numeric<=9223372036854775807 THEN suffix::bigint END END),0)
        INTO base_number FROM(SELECT substring("Code",4) AS suffix FROM public."Correspondents" WHERE "TenantId"=p_tenant AND "Code" LIKE 'AG-%')n;
    SELECT greatest(coalesce(max(CASE WHEN code COLLATE "C" ~ '^[0-9]{1,19}$' THEN CASE WHEN code::numeric<=9223372036854775807 THEN code::bigint END END),1999),1999)
        INTO account_base FROM(SELECT "AccountCode" AS code FROM public."Accounts" WHERE "TenantId"=p_tenant AND("AccountCode" LIKE '2%' OR "AccountType"='Correspondent'))n;
    requested:=jsonb_array_length(p_names);SELECT requested+count(*)::integer INTO candidate_count FROM public."Accounts" WHERE "TenantId"=p_tenant;
    DROP TABLE IF EXISTS pg_temp."ImportCreatedAgents",pg_temp."ImportAccountCodes";
    CREATE TEMP TABLE "ImportAccountCodes" ON COMMIT DROP AS SELECT row_number() OVER(ORDER BY number) AS row_number,number::text AS code FROM(
        SELECT account_base+g AS number FROM generate_series(1,candidate_count)g WHERE NOT EXISTS(SELECT 1 FROM public."Accounts" a WHERE a."TenantId"=p_tenant AND a."AccountCode"=(account_base+g)::text) ORDER BY number LIMIT requested)n;
    IF(SELECT count(*) FROM "ImportAccountCodes")<>requested THEN RAISE EXCEPTION 'Could not allocate account codes.'; END IF;
    CREATE TEMP TABLE "ImportCreatedAgents"("Id" bigint,"Name" text,"Code" text,"NormalizedName" text) ON COMMIT DROP;
    WITH numbered AS(SELECT n.*,row_number() OVER(ORDER BY n."NormalizedName") AS row_number FROM jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text)),
    created AS(INSERT INTO public."Correspondents" AS new_agent("TenantId","Code","Name","City","IsArchived","CommissionMethod","CreatedAt")
        SELECT p_tenant,'AG-'||CASE WHEN base_number+n.row_number<1000 THEN lpad((base_number+n.row_number)::text,3,'0') ELSE(base_number+n.row_number)::text END,
            n."Name",left(n."Name",100),false,'PerTransaction',CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM numbered n RETURNING new_agent."Id",new_agent."Name",new_agent."Code")
    INSERT INTO "ImportCreatedAgents" SELECT c."Id",c."Name",c."Code",n."NormalizedName" FROM created c JOIN jsonb_to_recordset(p_names)n("NormalizedName" text,"Name" text) ON n."Name"=c."Name";
    WITH numbered AS(SELECT c.*,row_number() OVER(ORDER BY c."NormalizedName") AS row_number FROM "ImportCreatedAgents" c),
    accounts AS(INSERT INTO public."Accounts" AS new_account("TenantId","AccountCode","AccountName","AccountType","CorrespondentId","IsArchived","CreatedAt")
        SELECT p_tenant,codes.code,left('نماینده: '||c."Name",200),'Correspondent',c."Id",false,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM numbered c JOIN "ImportAccountCodes" codes ON codes.row_number=c.row_number RETURNING new_account."Id",new_account."AccountName",new_account."AccountCode",new_account."CorrespondentId")
    INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
        SELECT p_tenant,p_actor,gen_random_uuid(),'CREATE','Accounts',a."Id",json_build_object('AccountName',a."AccountName",'AccountCode',a."AccountCode",'CorrespondentId',a."CorrespondentId",'__Description','ایجاد حساب نمایندگی در آماده‌سازی گروهی')::text,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM accounts a;
    INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
        SELECT p_tenant,p_actor,gen_random_uuid(),'CREATE','Correspondents',c."Id",json_build_object('Name',c."Name",'Code',c."Code",'__Description','ایجاد نمایندگی در آماده‌سازی گروهی')::text,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM "ImportCreatedAgents" c;
    RETURN QUERY SELECT c."NormalizedName",c."Id" FROM "ImportCreatedAgents" c;
END $body$;

CREATE OR REPLACE FUNCTION hawala.bulk_import_assignments(p_tenant bigint,p_actor bigint,p_date date,p_assignments jsonb) RETURNS void LANGUAGE plpgsql AS $body$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('HawalaImportPrepare:'||p_tenant,0));
    IF EXISTS(SELECT 1 FROM jsonb_to_recordset(p_assignments)a("PaymentLocationId" bigint,"CorrespondentId" bigint)
        LEFT JOIN public."PaymentLocations" loc ON loc."TenantId"=p_tenant AND loc."Id"=a."PaymentLocationId" AND loc."IsActive"
        LEFT JOIN public."Correspondents" c ON c."TenantId"=p_tenant AND c."Id"=a."CorrespondentId" AND NOT c."IsArchived" WHERE loc."Id" IS NULL OR c."Id" IS NULL) THEN RAISE EXCEPTION 'محل پرداخت یا نمایندگی مسئول فعال یا متعلق به صرافی جاری نیست.'; END IF;
    IF EXISTS(SELECT 1 FROM jsonb_to_recordset(p_assignments)a("PaymentLocationId" bigint,"CorrespondentId" bigint) JOIN public."PaymentLocationCorrespondentAssignments" h
        ON h."TenantId"=p_tenant AND h."PaymentLocationId"=a."PaymentLocationId") THEN RAISE EXCEPTION 'برای محل پرداخت تاریخچه مسئولیت ثبت شده است؛ آن را دوباره بررسی کنید.'; END IF;
    WITH created AS(INSERT INTO public."PaymentLocationCorrespondentAssignments"("TenantId","PaymentLocationId","CorrespondentId","EffectiveFrom","CreatedBy","CreatedAt")
        SELECT p_tenant,a."PaymentLocationId",a."CorrespondentId",p_date,p_actor,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM jsonb_to_recordset(p_assignments)a("PaymentLocationId" bigint,"CorrespondentId" bigint) RETURNING "Id","PaymentLocationId","CorrespondentId")
    INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt") SELECT p_tenant,p_actor,gen_random_uuid(),'CREATE','PaymentLocationCorrespondentAssignments',c."Id",
        json_build_object('PaymentLocationId',c."PaymentLocationId",'CorrespondentId',c."CorrespondentId",'EffectiveFrom',p_date,'__Description','تعیین مسئول پرداخت در آماده‌سازی گروهی')::text,CURRENT_TIMESTAMP AT TIME ZONE 'UTC' FROM created c;
END $body$;
