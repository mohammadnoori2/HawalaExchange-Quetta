WITH activities AS (
    SELECT "CreatedAt"::date AS "ActivityDate","CreatedAt" AS "ActivityAt",1 AS "HawalaCount",0 AS "ExchangeCount",0 AS "AccountOperationCount",0 AS "ExpenseCount",0 AS "CapitalInvestmentCount"
    FROM public."Hawalas" WHERE "TenantId"=@TenantId AND "CreatedAt">=@FromDate AND "CreatedAt"<@ToDateExclusive AND "Status"<>'Cancel'
    UNION ALL SELECT "ExchangeDate"::date,"ExchangeDate",0,1,0,0,0 FROM public."MoneyExchangeOperations"
    WHERE "TenantId"=@TenantId AND "ExchangeDate">=@FromDate AND "ExchangeDate"<@ToDateExclusive AND NOT "IsDeleted"
    UNION ALL SELECT "OperationDate"::date,"OperationDate",0,0,1,0,0 FROM public."AccountMoneyOperations"
    WHERE "TenantId"=@TenantId AND "OperationDate">=@FromDate AND "OperationDate"<@ToDateExclusive AND NOT "IsDeleted"
    UNION ALL SELECT "ExpenseDate"::date,"ExpenseDate",0,0,0,1,0 FROM public."Expenses"
    WHERE "TenantId"=@TenantId AND "ExpenseDate">=@FromDate AND "ExpenseDate"<@ToDateExclusive AND NOT "IsDeleted"
    UNION ALL SELECT "InvestmentDate"::date,"InvestmentDate",0,0,0,0,1 FROM public."CapitalInvestments"
    WHERE "TenantId"=@TenantId AND "InvestmentDate">=@FromDate AND "InvestmentDate"<@ToDateExclusive AND NOT "IsDeleted"
)
SELECT "ActivityDate"::timestamp,max("ActivityAt") AS "LastActivityAt",sum("HawalaCount")::integer AS "HawalaCount",sum("ExchangeCount")::integer AS "ExchangeCount",
    sum("AccountOperationCount")::integer AS "AccountOperationCount",sum("ExpenseCount")::integer AS "ExpenseCount",sum("CapitalInvestmentCount")::integer AS "CapitalInvestmentCount"
FROM activities GROUP BY "ActivityDate" ORDER BY "ActivityDate";

SELECT le."AccountId",a."AccountCode",a."AccountName",a."AccountType",le."CurrencyId",c."Code" AS "CurrencyCode",sum(le."TalabKar") AS "TalabKar",sum(le."BadehKar") AS "BadehKar"
FROM public."LedgerEntries" le JOIN public."Accounts" a ON a."TenantId"=le."TenantId" AND a."Id"=le."AccountId"
JOIN public."Currencies" c ON c."TenantId"=le."TenantId" AND c."Id"=le."CurrencyId"
WHERE le."TenantId"=@TenantId AND le."CreatedAt"<@ToDateExclusive GROUP BY le."AccountId",a."AccountCode",a."AccountName",a."AccountType",le."CurrencyId",c."Code";

SELECT le."CreatedAt"::date::timestamp AS "EntryDate",le."AccountId",a."AccountCode",a."AccountName",a."AccountType",le."CurrencyId",c."Code" AS "CurrencyCode",sum(le."TalabKar") AS "TalabKar",sum(le."BadehKar") AS "BadehKar"
FROM public."LedgerEntries" le JOIN public."Accounts" a ON a."TenantId"=le."TenantId" AND a."Id"=le."AccountId"
JOIN public."Currencies" c ON c."TenantId"=le."TenantId" AND c."Id"=le."CurrencyId"
WHERE le."TenantId"=@TenantId AND le."CreatedAt">=@FromDate AND le."CreatedAt"<@ToDateExclusive
GROUP BY le."CreatedAt"::date,le."AccountId",a."AccountCode",a."AccountName",a."AccountType",le."CurrencyId",c."Code" ORDER BY "EntryDate";
