CREATE OR REPLACE FUNCTION hawala.account_balances(p_tenant bigint,p_account bigint,p_customer bigint,p_correspondent bigint,p_owner text,p_type text,p_asof timestamp)
RETURNS TABLE("AccountId" bigint,"AccountName" text,"AccountType" text,"CustomerId" bigint,"CorrespondentId" bigint,"CurrencyId" bigint,
    "CurrencyCode" text,"Balance" numeric,"PendingCommissionDebit" numeric,"PendingCommissionCredit" numeric)
LANGUAGE plpgsql STABLE AS $body$
BEGIN
    IF p_tenant IS NULL OR p_tenant<=0 THEN RAISE EXCEPTION 'A valid TenantId is required.'; END IF;
    IF p_owner IS NOT NULL AND p_owner NOT IN ('Customer','Correspondent') THEN RAISE EXCEPTION 'OwnerType must be Customer or Correspondent.'; END IF;
    RETURN QUERY SELECT a."Id",a."AccountName",a."AccountType",a."CustomerId",a."CorrespondentId",b."CurrencyId",c."Code",
        b."Balance"::numeric(18,2),b."PendingCommissionDebit"::numeric(18,2),b."PendingCommissionCredit"::numeric(18,2)
    FROM public.ufn_accountoperationalbalances_v1(p_tenant,p_account,p_asof) b
    JOIN public."Accounts" a ON a."TenantId"=p_tenant AND a."Id"=b."AccountId"
    JOIN public."Currencies" c ON c."TenantId"=p_tenant AND c."Id"=b."CurrencyId"
    WHERE (p_customer IS NULL OR a."CustomerId"=p_customer) AND (p_correspondent IS NULL OR a."CorrespondentId"=p_correspondent)
      AND (p_type IS NULL OR a."AccountType"=p_type)
      AND (p_owner IS NULL OR(p_owner='Customer' AND a."CustomerId" IS NOT NULL) OR(p_owner='Correspondent' AND a."CorrespondentId" IS NOT NULL))
      AND (b."Balance"<>0 OR b."PendingCommissionDebit"<>0 OR b."PendingCommissionCredit"<>0)
    ORDER BY a."Id",c."Code";
END $body$;
