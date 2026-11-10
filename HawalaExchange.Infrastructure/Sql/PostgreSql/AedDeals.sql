CREATE OR REPLACE FUNCTION hawala.aed_transaction_number(p_tenant bigint,p_prefix text) RETURNS text
LANGUAGE plpgsql AS $body$
DECLARE prefix text; next_number integer; zone text;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('AedTransactionNumber:'||p_tenant||':'||p_prefix,0));
    SELECT "TimeZoneId" INTO zone FROM public."TenantOperationalControls" WHERE "TenantId"=p_tenant;
    prefix := p_prefix||'-'||to_char(CURRENT_TIMESTAMP AT TIME ZONE coalesce(zone,'Asia/Kabul'),'YYYYMMDD')||'-';
    SELECT coalesce(max(CASE WHEN suffix COLLATE "C" ~ '^[0-9]{1,10}$' THEN CASE WHEN suffix::numeric<=2147483646 THEN suffix::integer END END),0)+1
    INTO next_number FROM (SELECT substring("TransactionNo",length(prefix)+1) AS suffix FROM public."Transactions"
        WHERE "TenantId"=p_tenant AND left("TransactionNo",length(prefix))=prefix) n;
    RETURN prefix||lpad(next_number::text,greatest(4,length(next_number::text)),'0');
END $body$;

CREATE OR REPLACE FUNCTION hawala.process_aed(p jsonb) RETURNS bigint LANGUAGE plpgsql AS $body$
DECLARE
    operation text:=p->>'Operation'; tenant bigint:=(p->>'TenantId')::bigint; actor bigint:=(p->>'CurrentUserId')::bigint;
    deal_id bigint:=coalesce((p->>'DealId')::bigint,0); conversion_id bigint:=coalesce((p->>'ConversionId')::bigint,0);
    deal_number text:=nullif(btrim(p->>'DealNumber'),''); source_id bigint:=(p->>'SourceCorrespondentId')::bigint;
    dubai_id bigint:=(p->>'DubaiCorrespondentId')::bigint; currency_id bigint:=(p->>'SourceCurrencyId')::bigint;
    amount numeric(18,4):=coalesce((p->>'Amount')::numeric,0); places integer:=coalesce((p->>'RoundingDecimalPlaces')::integer,0);
    actual_marker numeric(18,4):=coalesce((p->>'ActualMarker')::numeric,0); declared_marker numeric(18,4):=coalesce((p->>'DeclaredMarker')::numeric,0);
    note text:=nullif(btrim(p->>'Note'),''); reason text:=nullif(btrim(p->>'Reason'),'');
    now_utc timestamp:=CURRENT_TIMESTAMP AT TIME ZONE 'UTC'; branch bigint; posting bigint; reversal bigint;
    source_account bigint; dubai_account bigint; source_name text; dubai_name text; currency_code text; usd bigint;
    profit_account bigint; profit_code text; profit_type text; profit_name text;
    actual_adjustment numeric(18,4); declared_adjustment numeric(18,4); actual_adjusted numeric(38,8); declared_adjusted numeric(38,8);
    final_usd numeric(18,4); declared_usd numeric(18,4); profit numeric(18,4); description text;
    d public."AedDeals"%ROWTYPE; c public."AedDealConversions"%ROWTYPE; previous_timeout text;
BEGIN
    IF operation IS NULL OR operation NOT IN ('Create','Convert','ReverseConversion','Cancel') THEN RAISE EXCEPTION 'نوع عملیات معامله درهم معتبر نیست.'; END IF;
    IF NOT EXISTS(SELECT 1 FROM public."Users" WHERE "TenantId"=tenant AND "Id"=actor AND "IsActive") THEN RAISE EXCEPTION 'کاربر جاری معتبر نیست.'; END IF;
    SELECT "Id" INTO branch FROM public."Branches" WHERE "TenantId"=tenant ORDER BY ("Code"='HQ') DESC,"Id" LIMIT 1;
    IF branch IS NULL THEN RAISE EXCEPTION 'برای صرافی جاری هیچ شعبه‌ای تعریف نشده است.'; END IF;
    previous_timeout:=current_setting('lock_timeout'); PERFORM set_config('lock_timeout','30s',true);

    IF operation='Create' THEN
        IF deal_number IS NULL THEN RAISE EXCEPTION 'نمبر معامله الزامی است.'; END IF;
        IF coalesce(source_id,0)<=0 OR coalesce(dubai_id,0)<=0 OR source_id=dubai_id THEN RAISE EXCEPTION 'طرف کویته و طرف دبی باید معتبر و متفاوت باشند.'; END IF;
        IF amount<=0 OR places<0 OR places>4 THEN RAISE EXCEPTION 'مبلغ معامله یا تعداد اعشار معتبر نیست.'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('AedDealNumber:'||tenant||':'||lower(deal_number),0));
        IF EXISTS(SELECT 1 FROM public."AedDeals" WHERE "TenantId"=tenant AND lower("DealNumber")=lower(deal_number)) THEN RAISE EXCEPTION 'نمبر معامله قبلاً ثبت شده است.'; END IF;
        SELECT a."Id",r."Name" INTO source_account,source_name FROM public."Correspondents" r JOIN public."Accounts" a
            ON a."TenantId"=tenant AND a."CorrespondentId"=r."Id" AND NOT a."IsArchived"
            WHERE r."TenantId"=tenant AND r."Id"=source_id AND NOT r."IsArchived";
        SELECT a."Id",r."Name" INTO dubai_account,dubai_name FROM public."Correspondents" r JOIN public."Accounts" a
            ON a."TenantId"=tenant AND a."CorrespondentId"=r."Id" AND NOT a."IsArchived"
            WHERE r."TenantId"=tenant AND r."Id"=dubai_id AND NOT r."IsArchived";
        IF source_account IS NULL OR dubai_account IS NULL THEN RAISE EXCEPTION 'نمایندگی فعال یا حساب نمایندگی یافت نشد.'; END IF;
        SELECT "Code" INTO currency_code FROM public."Currencies" WHERE "TenantId"=tenant AND "Id"=currency_id AND "IsActive";
        IF currency_code IS NULL OR currency_code NOT IN ('AED','USD') THEN RAISE EXCEPTION 'ارز معامله فقط می‌تواند AED یا USD باشد.'; END IF;
        INSERT INTO public."Transactions"("TenantId","TransactionNo","TransactionType","BranchId","Status","Remarks","CreatedBy","CreatedAt")
        VALUES(tenant,hawala.aed_transaction_number(tenant,'AEDH'),'AedDealHolding',branch,'Paid',
            'ثبت معامله درهم '||deal_number||': '||source_name||' به '||dubai_name,actor,now_utc) RETURNING "Id" INTO posting;
        description:='نگهداری معامله '||deal_number||' نزد '||dubai_name;
        INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
        VALUES(tenant,posting,dubai_account,currency_id,0,amount,description,now_utc),(tenant,posting,source_account,currency_id,amount,0,description,now_utc);
        INSERT INTO public."AedDeals"("TenantId","DealNumber","SourceCorrespondentId","DubaiCorrespondentId","SourceCurrencyId","OriginalAmount",
            "ConvertedAmount","TotalFinalUsd","TotalProfitUsd","AedPerUsdRate","RoundingDecimalPlaces","Status","HoldingTransactionId","Note","CreatedAt","CreatedBy")
        VALUES(tenant,deal_number,source_id,dubai_id,currency_id,amount,0,0,0,3.67,places,'Held',posting,note,now_utc,actor) RETURNING "Id" INTO deal_id;
        INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
        VALUES(tenant,actor,gen_random_uuid(),'CREATE_AED_DEAL','AedDeals',deal_id,'معامله '||deal_number||' به مبلغ '||amount||' '||currency_code||' ثبت شد.',now_utc);
    ELSE
        IF operation='ReverseConversion' THEN
            IF conversion_id<=0 OR reason IS NULL THEN RAISE EXCEPTION 'تبدیل و دلیل برگشت معتبر الزامی است.'; END IF;
            SELECT "AedDealId" INTO deal_id FROM public."AedDealConversions" WHERE "TenantId"=tenant AND "Id"=conversion_id;
        END IF;
        IF coalesce(deal_id,0)<=0 THEN RAISE EXCEPTION 'معامله درهم یافت نشد.'; END IF;
        PERFORM pg_advisory_xact_lock(hashtextextended('AedDeal:'||tenant||':'||deal_id,0));
        SELECT * INTO d FROM public."AedDeals" WHERE "TenantId"=tenant AND "Id"=deal_id FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'معامله درهم یافت نشد.'; END IF;
        IF operation='Convert' THEN
            IF amount<=0 OR amount>d."OriginalAmount"-d."ConvertedAmount" OR d."Status"='Cancelled' THEN RAISE EXCEPTION 'مبلغ تبدیل یا وضعیت معامله معتبر نیست.'; END IF;
            SELECT "Code" INTO currency_code FROM public."Currencies" WHERE "TenantId"=tenant AND "Id"=d."SourceCurrencyId";
            actual_adjustment:=round(amount/100000*actual_marker,4); declared_adjustment:=round(amount/100000*declared_marker,4);
            actual_adjusted:=amount+amount/100000*actual_marker; declared_adjusted:=amount+amount/100000*declared_marker;
            IF actual_adjusted<=0 OR declared_adjusted<=0 THEN RAISE EXCEPTION 'حاصل مشخصه معامله نباید منفی یا صفر شود.'; END IF;
            final_usd:=round(CASE WHEN currency_code='AED' THEN actual_adjusted/d."AedPerUsdRate" ELSE actual_adjusted END,d."RoundingDecimalPlaces");
            declared_usd:=round(CASE WHEN currency_code='AED' THEN declared_adjusted/d."AedPerUsdRate" ELSE declared_adjusted END,d."RoundingDecimalPlaces");
            IF final_usd<=0 OR declared_usd<=0 THEN RAISE EXCEPTION 'حاصل تبدیل پس از گردکردن باید بزرگ‌تر از صفر باشد.'; END IF;
            profit:=final_usd-declared_usd;
            SELECT "Id" INTO source_account FROM public."Accounts" WHERE "TenantId"=tenant AND "CorrespondentId"=d."SourceCorrespondentId" AND NOT "IsArchived" ORDER BY "Id" LIMIT 1;
            SELECT "Id" INTO dubai_account FROM public."Accounts" WHERE "TenantId"=tenant AND "CorrespondentId"=d."DubaiCorrespondentId" AND NOT "IsArchived" ORDER BY "Id" LIMIT 1;
            SELECT "Id" INTO usd FROM public."Currencies" WHERE "TenantId"=tenant AND "Code"='USD' AND "IsActive" ORDER BY "Id" LIMIT 1;
            IF source_account IS NULL OR dubai_account IS NULL OR usd IS NULL THEN RAISE EXCEPTION 'حساب فعال نمایندگی یا ارز USD یافت نشد.'; END IF;
            IF profit<>0 THEN
                profit_code:=CASE WHEN profit>0 THEN '3003' ELSE '4003' END; profit_type:=CASE WHEN profit>0 THEN 'Income' ELSE 'Expense' END;
                profit_name:=CASE WHEN profit>0 THEN 'مفاد معاملات درهم' ELSE 'زیان معاملات درهم' END;
                PERFORM pg_advisory_xact_lock(hashtextextended('SystemAccount:'||tenant||':'||profit_code,0));
                SELECT "Id" INTO profit_account FROM public."Accounts" WHERE "TenantId"=tenant AND "AccountCode"=profit_code FOR UPDATE;
                IF profit_account IS NULL THEN
                    INSERT INTO public."Accounts"("TenantId","AccountCode","AccountName","AccountType","IsArchived","CreatedAt")
                    VALUES(tenant,profit_code,profit_name,profit_type,false,now_utc) RETURNING "Id" INTO profit_account;
                ELSIF EXISTS(SELECT 1 FROM public."Accounts" WHERE "TenantId"=tenant AND "Id"=profit_account AND ("IsArchived" OR "AccountType"<>profit_type)) THEN RAISE EXCEPTION 'حساب مفاد یا زیان معاملات درهم معتبر نیست.'; END IF;
            END IF;
            INSERT INTO public."Transactions"("TenantId","TransactionNo","TransactionType","BranchId","Status","Remarks","CreatedBy","CreatedAt")
            VALUES(tenant,hawala.aed_transaction_number(tenant,'AEDC'),'AedDealConversion',branch,'Paid',
                'تبدیل '||amount||' '||currency_code||' از معامله '||d."DealNumber"||' به USD',actor,now_utc) RETURNING "Id" INTO posting;
            INSERT INTO public."AedDealConversions"("TenantId","AedDealId","SourceAmount","AedPerUsdRate","ActualMarker","DeclaredMarker","ActualAdjustmentSource",
                "DeclaredAdjustmentSource","FinalUsdAmount","DeclaredUsdAmount","ProfitUsd","PostingTransactionId","Status","Note","CreatedAt","CreatedBy")
            VALUES(tenant,deal_id,amount,d."AedPerUsdRate",actual_marker,declared_marker,actual_adjustment,declared_adjustment,final_usd,declared_usd,profit,posting,'Posted',note,now_utc,actor) RETURNING "Id" INTO conversion_id;
            UPDATE public."AedDeals" SET "ConvertedAmount"="ConvertedAmount"+amount,"TotalFinalUsd"="TotalFinalUsd"+final_usd,"TotalProfitUsd"="TotalProfitUsd"+profit,
                "Status"=CASE WHEN "ConvertedAmount"+amount="OriginalAmount" THEN 'Converted' ELSE 'PartiallyConverted' END WHERE "TenantId"=tenant AND "Id"=deal_id;
            description:='تبدیل معامله '||d."DealNumber"||' به USD';
            INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt") VALUES
                (tenant,posting,source_account,d."SourceCurrencyId",0,amount,description,now_utc),(tenant,posting,dubai_account,d."SourceCurrencyId",amount,0,description,now_utc),
                (tenant,posting,dubai_account,usd,0,final_usd,description,now_utc),(tenant,posting,source_account,usd,declared_usd,0,description,now_utc);
            IF profit<>0 THEN INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
                VALUES(tenant,posting,profit_account,usd,greatest(profit,0),greatest(-profit,0),description,now_utc); END IF;
            INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
            VALUES(tenant,actor,gen_random_uuid(),'CONVERT_AED_DEAL','AedDealConversions',conversion_id,'معامله '||d."DealNumber"||': '||amount||' '||currency_code||' به '||final_usd||' USD تبدیل شد.',now_utc);
        ELSE
            IF reason IS NULL THEN RAISE EXCEPTION 'دلیل برگشت یا لغو الزامی است.'; END IF;
            IF operation='ReverseConversion' THEN
                SELECT * INTO c FROM public."AedDealConversions" WHERE "TenantId"=tenant AND "Id"=conversion_id FOR UPDATE;
                IF c."Status" IS DISTINCT FROM 'Posted' THEN RAISE EXCEPTION 'این تبدیل قبلاً برگشت داده شده است.'; END IF;
                posting:=c."PostingTransactionId";
                IF NOT EXISTS(SELECT 1 FROM public."LedgerEntries" WHERE "TenantId"=tenant AND "TransactionId"=posting) THEN RAISE EXCEPTION 'سند حسابداری تبدیل یافت نشد.'; END IF;
            ELSE
                IF d."Status"='Cancelled' THEN RAISE EXCEPTION 'معامله قبلاً لغو شده است.'; END IF;
                IF EXISTS(SELECT 1 FROM public."AedDealConversions" WHERE "TenantId"=tenant AND "AedDealId"=deal_id AND "Status"='Posted') THEN RAISE EXCEPTION 'ابتدا تمام تبدیل‌های فعال این معامله را برگشت دهید.'; END IF;
                posting:=d."HoldingTransactionId";
            END IF;
            INSERT INTO public."Transactions"("TenantId","TransactionNo","TransactionType","BranchId","Status","Remarks","CreatedBy","CreatedAt","ReversedTransactionId")
            VALUES(tenant,hawala.aed_transaction_number(tenant,CASE WHEN operation='ReverseConversion' THEN 'AEDCR' ELSE 'AEDR' END),'AedDealReversal',branch,'Paid',
                CASE WHEN operation='ReverseConversion' THEN 'برگشت تبدیل معامله ' ELSE 'لغو معامله ' END||d."DealNumber"||': '||reason,actor,now_utc,posting) RETURNING "Id" INTO reversal;
            INSERT INTO public."LedgerEntries"("TenantId","TransactionId","AccountId","CurrencyId","TalabKar","BadehKar","Description","CreatedAt")
            SELECT tenant,reversal,"AccountId","CurrencyId","BadehKar","TalabKar",
                CASE WHEN operation='ReverseConversion' THEN 'برگشت: ' ELSE 'لغو: ' END||coalesce("Description",''),now_utc
                FROM public."LedgerEntries" WHERE "TenantId"=tenant AND "TransactionId"=posting;
            IF operation='ReverseConversion' THEN
                UPDATE public."AedDealConversions" SET "Status"='Reversed',"ReversalTransactionId"=reversal,"ReversalReason"=reason,"ReversedAt"=now_utc,"ReversedBy"=actor WHERE "TenantId"=tenant AND "Id"=conversion_id;
                UPDATE public."AedDeals" SET "ConvertedAmount"="ConvertedAmount"-c."SourceAmount","TotalFinalUsd"="TotalFinalUsd"-c."FinalUsdAmount","TotalProfitUsd"="TotalProfitUsd"-c."ProfitUsd",
                    "Status"=CASE WHEN "ConvertedAmount"-c."SourceAmount"<=0 THEN 'Held' ELSE 'PartiallyConverted' END WHERE "TenantId"=tenant AND "Id"=deal_id;
            ELSE
                UPDATE public."AedDeals" SET "Status"='Cancelled',"ReversalTransactionId"=reversal,"CancelReason"=reason,"CancelledAt"=now_utc,"CancelledBy"=actor WHERE "TenantId"=tenant AND "Id"=deal_id;
            END IF;
            UPDATE public."Transactions" SET "Status"='Cancel',"CancelReason"=reason,"CancelledAt"=now_utc,"CancelledBy"=actor WHERE "TenantId"=tenant AND "Id"=posting;
            INSERT INTO public."AuditLogs"("TenantId","UserId","ProcessId","Action","TableName","RecordId","NewValue","CreatedAt")
            VALUES(tenant,actor,gen_random_uuid(),CASE WHEN operation='ReverseConversion' THEN 'REVERSE_AED_CONVERSION' ELSE 'CANCEL_AED_DEAL' END,
                CASE WHEN operation='ReverseConversion' THEN 'AedDealConversions' ELSE 'AedDeals' END,CASE WHEN operation='ReverseConversion' THEN conversion_id ELSE deal_id END,
                'معامله '||d."DealNumber"||CASE WHEN operation='ReverseConversion' THEN ' برگشت داده شد.' ELSE ' لغو شد.' END,now_utc);
        END IF;
    END IF;
    PERFORM set_config('lock_timeout',previous_timeout,true);
    RETURN deal_id;
END $body$;
