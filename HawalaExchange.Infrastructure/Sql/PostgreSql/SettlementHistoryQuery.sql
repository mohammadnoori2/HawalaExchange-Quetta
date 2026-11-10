SELECT c."Id" AS "ConversionId",c."CreatedAt",u."FullName" AS "UserName",c."SourceMode",c."Note",t."Code" AS "TargetCurrencyCode",latest."Status",
    CASE WHEN @id IS NOT NULL THEN latest."SnapshotJson" ELSE(
        SELECT coalesce(json_agg(json_strip_nulls(row_to_json(q))),'[]'::json)::text FROM(
            SELECT j."RateDate",j."SourceCurrencyId",j."SourceCurrencyCode",j."ExchangeRate",sum(j."SourceAmount") AS "SourceAmount",
                sum(j."TargetAmount") AS "TargetAmount",sum(j."PrincipalTargetAmount") AS "PrincipalTargetAmount",sum(j."UsdAmount") AS "UsdAmount"
            FROM jsonb_to_recordset(latest."SnapshotJson"::jsonb) AS j("RateDate" date,"SourceCurrencyId" bigint,"SourceCurrencyCode" text,"ExchangeRate" numeric(18,8),"SourceAmount" numeric(18,4),"TargetAmount" numeric(18,4),"PrincipalTargetAmount" numeric(28,8),"UsdAmount" numeric(28,8))
            GROUP BY j."RateDate",j."SourceCurrencyId",j."SourceCurrencyCode",j."ExchangeRate" ORDER BY j."RateDate",j."SourceCurrencyCode")q)
    END AS "SnapshotJson",latest."HawalaCount"
FROM public."CorrespondentSettlementConversions" c JOIN public."Currencies" t ON t."TenantId"=c."TenantId" AND t."Id"=c."TargetCurrencyId"
JOIN public."Users" u ON u."TenantId"=c."TenantId" AND u."Id"=c."CreatedBy"
CROSS JOIN LATERAL(SELECT s."Status",s."SnapshotJson",s."HawalaCount" FROM public."CorrespondentSettlementHistorySnapshots" s
    WHERE s."TenantId"=c."TenantId" AND s."ConversionId"=c."Id" ORDER BY s."Id" DESC LIMIT 1)latest
WHERE c."TenantId"=@tenant AND c."CorrespondentId"=@owner AND(@id IS NULL OR c."Id"=@id) AND(@status IS NULL OR latest."Status"=@status)
    AND EXISTS(SELECT 1 FROM jsonb_to_recordset(latest."SnapshotJson"::jsonb) AS j("RateDate" date,"SourceCurrencyId" bigint)
        WHERE(@from IS NULL OR j."RateDate">=@from) AND(@to IS NULL OR j."RateDate"<=@to) AND(@currency IS NULL OR j."SourceCurrencyId"=@currency))
ORDER BY c."CreatedAt" DESC,c."Id" DESC LIMIT @size OFFSET @skip;
