CREATE TABLE dbo.CorrespondentSettlementDailyRates
(
    TenantId bigint NOT NULL, CorrespondentId bigint NOT NULL, RateDate date NOT NULL,
    SourceCurrencyId bigint NOT NULL, TargetCurrencyId bigint NOT NULL,
    ExchangeRate decimal(18,8) NOT NULL, UnitToTarget decimal(28,12) NOT NULL,
    ModifiedAt datetime2 NOT NULL, ModifiedBy bigint NOT NULL,
    CONSTRAINT PK_CorrespondentSettlementDailyRates PRIMARY KEY (TenantId,CorrespondentId,RateDate,SourceCurrencyId,TargetCurrencyId),
    CONSTRAINT CK_SettlementDailyRates_Positive CHECK (ExchangeRate>0 AND UnitToTarget>0)
);
CREATE TABLE dbo.CorrespondentSettlementValuations
(
    TenantId bigint NOT NULL,HawalaId bigint NOT NULL,ConversionId bigint NOT NULL,
    CorrespondentId bigint NOT NULL,RateDate date NOT NULL,SourceCurrencyId bigint NOT NULL,TargetCurrencyId bigint NOT NULL,
    SourceAmount decimal(18,4) NOT NULL,ExchangeRate decimal(18,8) NOT NULL,
    UnitToTarget decimal(28,12) NOT NULL,TargetAmount decimal(28,8) NOT NULL,UsdAmount decimal(28,8) NULL,
    CONSTRAINT PK_CorrespondentSettlementValuations PRIMARY KEY (TenantId,HawalaId)
);
CREATE INDEX IX_SettlementValuations_Conversion ON dbo.CorrespondentSettlementValuations(TenantId,ConversionId);
CREATE TABLE dbo.CorrespondentSettlementHistorySnapshots
(
    Id bigint IDENTITY PRIMARY KEY,TenantId bigint NOT NULL,CorrespondentId bigint NOT NULL,ConversionId bigint NOT NULL,
    Status nvarchar(20) NOT NULL DEFAULT N'Active',CreatedAt datetime2 NOT NULL,CreatedBy bigint NOT NULL,
    HawalaCount int NOT NULL DEFAULT 0,SnapshotJson nvarchar(max) NOT NULL,
    CONSTRAINT CK_SettlementHistory_Json CHECK (ISJSON(SnapshotJson)=1)
);
CREATE INDEX IX_SettlementHistory_Correspondent ON dbo.CorrespondentSettlementHistorySnapshots(TenantId,CorrespondentId,ConversionId,Id DESC);
GO
CREATE OR ALTER PROCEDURE dbo.usp_CaptureCorrespondentSettlement_v1
    @TenantId bigint,@ConversionId bigint,@UserId bigint,@RateDate date=NULL,@UtcOffsetMinutes int=0,
    @ConfirmDifferentDailyRate bit=0,@Backfill bit=0
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 THROW 50200,N'ثبت نرخ تبدیل باید همراه عملیات تبدیل انجام شود.',1;
    DECLARE @Owner bigint,@Target bigint,@Mode nvarchar(20),@TargetCode nvarchar(10),@TargetPriority int;
    SELECT @Owner=c.CorrespondentId,@Target=c.TargetCurrencyId,@Mode=c.SourceMode,@TargetCode=cur.Code,@TargetPriority=cur.QuotationPriority
        FROM dbo.CorrespondentSettlementConversions c JOIN dbo.Currencies cur ON cur.TenantId=c.TenantId AND cur.Id=c.TargetCurrencyId
        WHERE c.TenantId=@TenantId AND c.Id=@ConversionId;
    IF @Owner IS NULL THROW 50201,N'تبدیل نمایندگی یافت نشد.',1;
    CREATE TABLE #Rates(RateDate date,SourceCurrencyId bigint,ExchangeRate decimal(18,8),UnitToTarget decimal(28,12));
    INSERT #Rates
        SELECT CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,h.CreatedAt)),i.SourceCurrencyId,i.ExchangeRate,
            CASE WHEN cur.QuotationPriority<@TargetPriority OR (cur.QuotationPriority=@TargetPriority AND cur.Code<@TargetCode)
                 THEN i.ExchangeRate ELSE CAST(1 AS decimal(28,12))/i.ExchangeRate END
        FROM dbo.CorrespondentSettlementConversionHawalaItems i JOIN dbo.Hawalas h ON h.TenantId=i.TenantId AND h.Id=i.HawalaId
        JOIN dbo.Currencies cur ON cur.TenantId=i.TenantId AND cur.Id=i.SourceCurrencyId
        WHERE i.TenantId=@TenantId AND i.ConversionId=@ConversionId;
    IF @Mode=N'Account'
        INSERT #Rates SELECT COALESCE(@RateDate,CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,c.CreatedAt))),i.SourceCurrencyId,i.ExchangeRate,
            CASE WHEN cur.QuotationPriority<@TargetPriority OR (cur.QuotationPriority=@TargetPriority AND cur.Code<@TargetCode)
                 THEN i.ExchangeRate ELSE CAST(1 AS decimal(28,12))/i.ExchangeRate END
        FROM dbo.CorrespondentSettlementConversionItems i JOIN dbo.CorrespondentSettlementConversions c ON c.TenantId=i.TenantId AND c.Id=i.ConversionId
        JOIN dbo.Currencies cur ON cur.TenantId=i.TenantId AND cur.Id=i.SourceCurrencyId WHERE i.TenantId=@TenantId AND i.ConversionId=@ConversionId;
    IF @Backfill=0 AND EXISTS(SELECT 1 FROM #Rates GROUP BY RateDate,SourceCurrencyId HAVING MIN(ExchangeRate)<>MAX(ExchangeRate))
        THROW 50202,N'برای هر روز و ارز یک نرخ یکسان وارد کنید؛ هر روز را جدا ثبت کنید.',1;
    IF @ConfirmDifferentDailyRate=0 AND EXISTS(SELECT 1 FROM #Rates r JOIN dbo.CorrespondentSettlementDailyRates d WITH(UPDLOCK,HOLDLOCK)
        ON d.TenantId=@TenantId AND d.CorrespondentId=@Owner AND d.RateDate=r.RateDate AND d.SourceCurrencyId=r.SourceCurrencyId AND d.TargetCurrencyId=@Target
        WHERE d.ExchangeRate<>r.ExchangeRate)
        THROW 50203,N'نرخ این روز و ارز قبلاً متفاوت ثبت شده است؛ پس از بررسی، گزینه تأیید نرخ متفاوت را انتخاب کنید. تبدیل‌ها و کمیشن‌های قبلی تغییر نمی‌کنند.',1;
    UPDATE d SET ExchangeRate=r.ExchangeRate,UnitToTarget=r.UnitToTarget,ModifiedAt=SYSUTCDATETIME(),ModifiedBy=@UserId
        FROM dbo.CorrespondentSettlementDailyRates d JOIN (SELECT RateDate,SourceCurrencyId,MAX(ExchangeRate) ExchangeRate,MAX(UnitToTarget) UnitToTarget FROM #Rates GROUP BY RateDate,SourceCurrencyId) r
        ON d.TenantId=@TenantId AND d.CorrespondentId=@Owner AND d.RateDate=r.RateDate AND d.SourceCurrencyId=r.SourceCurrencyId AND d.TargetCurrencyId=@Target;
    INSERT dbo.CorrespondentSettlementDailyRates
        SELECT @TenantId,@Owner,r.RateDate,r.SourceCurrencyId,@Target,r.ExchangeRate,r.UnitToTarget,SYSUTCDATETIME(),@UserId
        FROM (SELECT RateDate,SourceCurrencyId,MAX(ExchangeRate) ExchangeRate,MAX(UnitToTarget) UnitToTarget FROM #Rates GROUP BY RateDate,SourceCurrencyId) r
        WHERE NOT EXISTS(SELECT 1 FROM dbo.CorrespondentSettlementDailyRates d WITH(UPDLOCK,HOLDLOCK) WHERE d.TenantId=@TenantId AND d.CorrespondentId=@Owner AND d.RateDate=r.RateDate AND d.SourceCurrencyId=r.SourceCurrencyId AND d.TargetCurrencyId=@Target);
    DECLARE @Usd bigint=(SELECT Id FROM dbo.Currencies WHERE TenantId=@TenantId AND Code=N'USD');
    -- Principal only: ledger net amounts may also contain per-transaction fees.
    DELETE FROM dbo.CorrespondentSettlementValuations WHERE TenantId=@TenantId AND ConversionId=@ConversionId;
    INSERT dbo.CorrespondentSettlementValuations
        SELECT @TenantId,h.Id,@ConversionId,@Owner,CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,h.CreatedAt)),i.SourceCurrencyId,@Target,
            amount.SourceAmount,i.ExchangeRate,unit.UnitToTarget,targetAmount.Amount,
            CASE WHEN i.SourceCurrencyId=@Usd THEN amount.SourceAmount WHEN @Target=@Usd THEN targetAmount.Amount
                 WHEN bridge.UnitToTarget>0 THEN ROUND(targetAmount.Amount/bridge.UnitToTarget,8) ELSE NULL END
        FROM dbo.CorrespondentSettlementConversionHawalaItems i JOIN dbo.Hawalas h ON h.TenantId=i.TenantId AND h.Id=i.HawalaId
        JOIN dbo.Currencies cur ON cur.TenantId=i.TenantId AND cur.Id=i.SourceCurrencyId
        CROSS APPLY(SELECT CASE WHEN h.HawalaType=N'HawalaSend' THEN COALESCE(h.ToAmount,h.FromAmount) ELSE h.FromAmount END SourceAmount,
            CASE WHEN h.HawalaType=N'HawalaSend' THEN h.ToCurrencyId ELSE h.FromCurrencyId END PrincipalCurrencyId) amount
        CROSS APPLY(SELECT CAST(CASE WHEN cur.QuotationPriority<@TargetPriority OR (cur.QuotationPriority=@TargetPriority AND cur.Code<@TargetCode)
                 THEN i.ExchangeRate ELSE CAST(1 AS decimal(28,12))/i.ExchangeRate END AS decimal(28,12)) UnitToTarget) unit
        CROSS APPLY(SELECT CAST(ROUND(CASE WHEN cur.QuotationPriority<@TargetPriority OR (cur.QuotationPriority=@TargetPriority AND cur.Code<@TargetCode)
            THEN amount.SourceAmount*i.ExchangeRate ELSE amount.SourceAmount/i.ExchangeRate END,8) AS decimal(28,8)) Amount) targetAmount
        LEFT JOIN dbo.CorrespondentSettlementDailyRates bridge ON bridge.TenantId=@TenantId AND bridge.CorrespondentId=@Owner AND bridge.TargetCurrencyId=@Target
            AND bridge.SourceCurrencyId=@Usd AND bridge.RateDate=CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,h.CreatedAt))
        WHERE i.TenantId=@TenantId AND i.ConversionId=@ConversionId AND amount.PrincipalCurrencyId=i.SourceCurrencyId;
    -- A USD bridge entered later completes, but never overwrites, existing snapshots.
    UPDATE v SET UsdAmount=ROUND(v.TargetAmount/bridge.UnitToTarget,8)
        FROM dbo.CorrespondentSettlementValuations v JOIN dbo.CorrespondentSettlementDailyRates bridge
        ON bridge.TenantId=v.TenantId AND bridge.CorrespondentId=v.CorrespondentId AND bridge.RateDate=v.RateDate AND bridge.TargetCurrencyId=v.TargetCurrencyId AND bridge.SourceCurrencyId=@Usd
        WHERE v.TenantId=@TenantId AND v.CorrespondentId=@Owner AND v.UsdAmount IS NULL;
    DECLARE @Json nvarchar(max)=(SELECT i.HawalaId,h.Number HawalaNumber,COALESCE(h.ReferenceNumber,N'') ReferenceNumber,
        COALESCE(h.SenderName,N'') SenderName,COALESCE(h.ReceiverName,N'') ReceiverName,COALESCE(p.Name,N'') PaymentLocationName,
        CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,h.CreatedAt)) RateDate,i.SourceCurrencyId,cur.Code SourceCurrencyCode,
        ABS(i.SourceTalabKar-i.SourceBadehKar) SourceAmount,i.ExchangeRate,ABS(i.TargetTalabKar-i.TargetBadehKar) TargetAmount,
        v.TargetAmount PrincipalTargetAmount,v.UsdAmount
        FROM dbo.CorrespondentSettlementConversionHawalaItems i JOIN dbo.Hawalas h ON h.TenantId=i.TenantId AND h.Id=i.HawalaId
        JOIN dbo.Currencies cur ON cur.TenantId=i.TenantId AND cur.Id=i.SourceCurrencyId
        LEFT JOIN dbo.PaymentLocations p ON p.TenantId=h.TenantId AND p.Id=h.PaymentLocationId
        LEFT JOIN dbo.CorrespondentSettlementValuations v ON v.TenantId=i.TenantId AND v.HawalaId=i.HawalaId
        WHERE i.TenantId=@TenantId AND i.ConversionId=@ConversionId ORDER BY h.CreatedAt,h.Number,cur.Code FOR JSON PATH);
    IF @Mode=N'Account'
        SET @Json=(SELECT COALESCE(@RateDate,CONVERT(date,DATEADD(minute,@UtcOffsetMinutes,c.CreatedAt))) RateDate,i.SourceCurrencyId,cur.Code SourceCurrencyCode,
            ABS(i.SourceTalabKar-i.SourceBadehKar) SourceAmount,i.ExchangeRate,ABS(i.TargetTalabKar-i.TargetBadehKar) TargetAmount
            FROM dbo.CorrespondentSettlementConversionItems i JOIN dbo.CorrespondentSettlementConversions c ON c.TenantId=i.TenantId AND c.Id=i.ConversionId
            JOIN dbo.Currencies cur ON cur.TenantId=i.TenantId AND cur.Id=i.SourceCurrencyId WHERE i.TenantId=@TenantId AND i.ConversionId=@ConversionId FOR JSON PATH);
    UPDATE dbo.CorrespondentSettlementHistorySnapshots SET Status=N'Superseded' WHERE TenantId=@TenantId AND ConversionId=@ConversionId AND Status=N'Active';
    INSERT dbo.CorrespondentSettlementHistorySnapshots(TenantId,CorrespondentId,ConversionId,CreatedAt,CreatedBy,HawalaCount,SnapshotJson)
        VALUES(@TenantId,@Owner,@ConversionId,SYSUTCDATETIME(),@UserId,
            (SELECT COUNT(*) FROM dbo.CorrespondentSettlementConversionHawalas WHERE TenantId=@TenantId AND ConversionId=@ConversionId),COALESCE(@Json,N'[]'));
END;
GO
