CREATE OR ALTER FUNCTION dbo.ufn_CashExecutedHawalas_v1(@TenantId bigint)
RETURNS TABLE AS RETURN
(
    WITH DirectLedger AS (
        SELECT e.HawalaId,e.AccountId,e.CurrencyId,
            SUM(e.TalabKar-e.BadehKar) Net,
            SUM(CASE WHEN e.TalabKar>0 THEN e.TalabKar ELSE 0 END) Credits,
            SUM(CASE WHEN e.Description=CONCAT(N'حواله دریافتی ',h.Id,N': پرداخت حواله از حساب انتخاب‌شده') THEN e.TalabKar ELSE 0 END) OriginalPrincipal,
            SUM(CASE WHEN e.Description=CONCAT(N'حواله دریافتی ',h.Id,N': کمیشن قابل پرداخت به عامل پرداخت') THEN e.TalabKar ELSE 0 END) OriginalCommission,
            MAX(CASE WHEN e.TalabKar>0 THEN e.CreatedAt END) CreditAt,
            MAX(CASE WHEN e.BadehKar>0 THEN e.CreatedAt END) DebitAt
        FROM dbo.LedgerEntries e
        JOIN dbo.Accounts a ON a.TenantId=e.TenantId AND a.Id=e.AccountId AND a.AccountType=N'Cash'
        JOIN dbo.Hawalas h ON h.TenantId=e.TenantId AND h.Id=e.HawalaId AND h.HawalaType=N'HawalaReceive'
        WHERE e.TenantId=@TenantId
        GROUP BY e.HawalaId,e.AccountId,e.CurrencyId
    ), DirectParts AS (
        SELECT d.*,h.Number,h.ReferenceNumber,h.ReceiverName,h.SenderName,h.CorrespondentId,h.PaymentLocationId,h.Status,
            COALESCE(h.PaidAt,d.CreditAt) PaidAt,h.PaidBy,
            CASE WHEN d.Net>0 AND d.CurrencyId=h.ToCurrencyId THEN CASE WHEN d.Net<COALESCE(h.ToAmount,h.FromAmount) THEN d.Net ELSE COALESCE(h.ToAmount,h.FromAmount) END
                 WHEN d.Net<=0 THEN d.OriginalPrincipal ELSE 0 END Principal,
            CASE WHEN d.Net>0 AND d.CurrencyId=COALESCE(h.AgentCommissionCurrencyId,h.ToCurrencyId)
                THEN CASE WHEN d.Net-(CASE WHEN d.CurrencyId=h.ToCurrencyId THEN COALESCE(h.ToAmount,h.FromAmount) ELSE 0 END)<COALESCE(h.AgentCommissionAmount,0)
                    THEN CASE WHEN d.Net-(CASE WHEN d.CurrencyId=h.ToCurrencyId THEN COALESCE(h.ToAmount,h.FromAmount) ELSE 0 END)>0 THEN d.Net-(CASE WHEN d.CurrencyId=h.ToCurrencyId THEN COALESCE(h.ToAmount,h.FromAmount) ELSE 0 END) ELSE 0 END
                    ELSE COALESCE(h.AgentCommissionAmount,0) END
                 WHEN d.Net<=0 THEN d.OriginalCommission ELSE 0 END Commission
        FROM DirectLedger d JOIN dbo.Hawalas h ON h.TenantId=@TenantId AND h.Id=d.HawalaId
        WHERE d.Credits>0 AND (d.OriginalPrincipal+d.OriginalCommission>0 OR (h.PaidFromAccountId=d.AccountId AND h.PaidAt IS NOT NULL))
          AND EXISTS(SELECT 1 FROM DirectLedger principal WHERE principal.HawalaId=d.HawalaId AND principal.AccountId=d.AccountId
              AND (principal.OriginalPrincipal>0 OR (principal.CurrencyId=h.ToCurrencyId AND h.PaidFromAccountId=d.AccountId AND h.PaidAt IS NOT NULL)))
    ), BillSnapshots AS (
        SELECT b.*,CASE WHEN ISJSON(b.SnapshotJson)=1 THEN b.SnapshotJson ELSE N'{}' END SafeSnapshot
        FROM dbo.AgentPaymentBills b WHERE b.TenantId=@TenantId AND b.PaidAt IS NOT NULL AND b.Status IN(N'Paid',N'Cancelled')
    ), BillItems AS (
        SELECT b.Id BillId,b.CorrespondentId,b.PaidAt,b.PaidBy,b.CancelledAt,b.Status,b.SafeSnapshot,
            item.HawalaId,item.Number,item.ReferenceNumber,item.ReceiverName,item.SenderName,item.PaymentLocation,
            item.CurrencyId,item.Amount,item.CommissionCurrencyId,COALESCE(item.AgentCommission,0) AgentCommission
        FROM BillSnapshots b
        CROSS APPLY OPENJSON(b.SafeSnapshot,N'$.Items') WITH (
            HawalaId bigint,Number bigint,ReferenceNumber nvarchar(100),ReceiverName nvarchar(200),SenderName nvarchar(200),PaymentLocation nvarchar(200),
            CurrencyId bigint,Amount decimal(18,2),CommissionCurrencyId bigint,AgentCommission decimal(18,2)) item
        JOIN dbo.AgentPaymentBillItems stored ON stored.TenantId=@TenantId AND stored.BillId=b.Id AND stored.HawalaId=item.HawalaId
        WHERE COALESCE(JSON_VALUE(b.SafeSnapshot,N'$.IsHistoricalReceipt'),N'false')<>N'true'
    ), BillParts AS (
        SELECT b.*,part.CurrencyId PartCurrencyId,part.Principal,part.Commission,paid.AccountId
        FROM BillItems b
        CROSS APPLY (VALUES(b.CurrencyId,b.Amount,CONVERT(decimal(18,2),0)),
                           (b.CommissionCurrencyId,CONVERT(decimal(18,2),0),b.AgentCommission)) part(CurrencyId,Principal,Commission)
        CROSS APPLY OPENJSON(b.SafeSnapshot,N'$.PaymentAccounts') WITH(CurrencyId bigint,AccountId bigint) paid
        JOIN dbo.Accounts a ON a.TenantId=@TenantId AND a.Id=paid.AccountId AND a.AccountType=N'Cash'
        WHERE paid.CurrencyId=part.CurrencyId AND part.Principal+part.Commission>0
          AND EXISTS(SELECT 1 FROM OPENJSON(b.SafeSnapshot,N'$.PaymentAccounts') WITH(CurrencyId bigint,AccountId bigint) principal
              JOIN dbo.Accounts ca ON ca.TenantId=@TenantId AND ca.Id=principal.AccountId AND ca.AccountType=N'Cash'
              WHERE principal.CurrencyId=b.CurrencyId)
          AND (SELECT COALESCE(SUM(e.TalabKar),0) FROM dbo.LedgerEntries e WHERE e.TenantId=@TenantId AND e.HawalaId=b.HawalaId
                AND e.AccountId=paid.AccountId AND e.CurrencyId=part.CurrencyId AND e.TalabKar>0
                AND (EXISTS(SELECT 1 FROM OPENJSON(b.SafeSnapshot,N'$.PaymentLedgerEntryIds') entry WHERE TRY_CONVERT(bigint,entry.value)=e.Id)
                    OR (COALESCE(JSON_QUERY(b.SafeSnapshot,N'$.PaymentLedgerEntryIds'),N'[]')=N'[]' AND e.CreatedAt=b.PaidAt
                        AND e.Description=CONCAT(N'پرداخت بیل عامل #',b.BillId,N' به ',JSON_VALUE(b.SafeSnapshot,N'$.RecipientName')))))>=(CASE WHEN part.CurrencyId=b.CurrencyId THEN b.Amount ELSE 0 END + CASE WHEN part.CurrencyId=b.CommissionCurrencyId THEN b.AgentCommission ELSE 0 END)
          AND (b.Status=N'Paid' OR (SELECT COALESCE(SUM(e.BadehKar),0) FROM dbo.LedgerEntries e WHERE e.TenantId=@TenantId AND e.HawalaId=b.HawalaId
                AND e.AccountId=paid.AccountId AND e.CurrencyId=part.CurrencyId AND e.Description=CONCAT(N'برگشت پرداخت بیل عامل #',b.BillId))>=(CASE WHEN part.CurrencyId=b.CurrencyId THEN b.Amount ELSE 0 END + CASE WHEN part.CurrencyId=b.CommissionCurrencyId THEN b.AgentCommission ELSE 0 END))
    )
    SELECT CONCAT(N'D:',d.HawalaId,N':',d.AccountId) EventKey,d.HawalaId,d.Number,COALESCE(d.ReferenceNumber,N'') Reference,
        COALESCE(d.ReceiverName,N'') Receiver,COALESCE(d.SenderName,N'') Sender,d.CorrespondentId,COALESCE(co.Name,N'صرافی') Correspondent,
        d.PaymentLocationId LocationId,COALESCE(loc.Name,N'نامشخص') Location,d.AccountId,a.AccountName Account,d.CurrencyId,c.Code Currency,
        CONVERT(decimal(18,2),d.Principal) Principal,CONVERT(decimal(18,2),d.Commission) Commission,d.PaidAt,
        CASE WHEN d.Net<=0 THEN d.DebitAt END ReversedAt,d.PaidBy ExecutedBy,COALESCE(u.FullName,N'نامشخص') ExecutedByName,
        CONVERT(nvarchar(10),N'Direct') Mode,CONVERT(bit,CASE WHEN d.Net<=0 THEN 1 ELSE 0 END) IsReversed,CONVERT(bigint,NULL) BillId,d.Status HawalaStatus
    FROM DirectParts d JOIN dbo.Accounts a ON a.TenantId=@TenantId AND a.Id=d.AccountId
    JOIN dbo.Currencies c ON c.TenantId=@TenantId AND c.Id=d.CurrencyId
    LEFT JOIN dbo.Correspondents co ON co.TenantId=@TenantId AND co.Id=d.CorrespondentId
    LEFT JOIN dbo.PaymentLocations loc ON loc.TenantId=@TenantId AND loc.Id=d.PaymentLocationId
    LEFT JOIN dbo.Users u ON u.TenantId=@TenantId AND u.Id=d.PaidBy
    WHERE d.PaidAt IS NOT NULL AND d.Principal+d.Commission>0
    UNION ALL
    SELECT CONCAT(N'B:',b.BillId,N':',b.HawalaId),b.HawalaId,b.Number,COALESCE(b.ReferenceNumber,N''),COALESCE(b.ReceiverName,N''),COALESCE(b.SenderName,N''),
        b.CorrespondentId,co.Name,loc.Id,COALESCE(b.PaymentLocation,N'نامشخص'),b.AccountId,a.AccountName,b.PartCurrencyId,c.Code,
        CONVERT(decimal(18,2),SUM(b.Principal)),CONVERT(decimal(18,2),SUM(b.Commission)),b.PaidAt,b.CancelledAt,b.PaidBy,COALESCE(u.FullName,N'نامشخص'),
        CONVERT(nvarchar(10),N'Bill'),CONVERT(bit,CASE WHEN b.Status=N'Cancelled' THEN 1 ELSE 0 END),b.BillId,h.Status
    FROM BillParts b JOIN dbo.Accounts a ON a.TenantId=@TenantId AND a.Id=b.AccountId
    JOIN dbo.Currencies c ON c.TenantId=@TenantId AND c.Id=b.PartCurrencyId
    JOIN dbo.Correspondents co ON co.TenantId=@TenantId AND co.Id=b.CorrespondentId
    JOIN dbo.Hawalas h ON h.TenantId=@TenantId AND h.Id=b.HawalaId
    OUTER APPLY(SELECT TOP(1) p.Id FROM dbo.PaymentLocations p WHERE p.TenantId=@TenantId AND p.Name=b.PaymentLocation ORDER BY p.Id) loc
    LEFT JOIN dbo.Users u ON u.TenantId=@TenantId AND u.Id=b.PaidBy
    GROUP BY b.BillId,b.HawalaId,b.Number,b.ReferenceNumber,b.ReceiverName,b.SenderName,b.CorrespondentId,co.Name,loc.Id,b.PaymentLocation,
        b.AccountId,a.AccountName,b.PartCurrencyId,c.Code,b.PaidAt,b.CancelledAt,b.PaidBy,u.FullName,b.Status,h.Status
);
