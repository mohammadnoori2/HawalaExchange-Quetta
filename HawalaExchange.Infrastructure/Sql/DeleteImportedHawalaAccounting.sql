CREATE OR ALTER PROCEDURE dbo.usp_DeleteImportedHawalaAccounting_v1
    @TenantId bigint,@UserId bigint,@BatchId bigint,@HawalaIds nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 THROW 51100,N'حذف حسابداری باید در تراکنش آپلود انجام شود.',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.HawalaImportBatches WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND Id=@BatchId AND Status=N'Posted')
        THROW 51101,N'فایل ثبت‌شده صرافی جاری یافت نشد.',1;
    CREATE TABLE #Ids(Id bigint PRIMARY KEY);
    INSERT #Ids SELECT DISTINCT CONVERT(bigint,[value]) FROM OPENJSON(@HawalaIds);
    IF EXISTS(SELECT 1 FROM #Ids i WHERE NOT EXISTS(SELECT 1 FROM dbo.Hawalas h WHERE h.TenantId=@TenantId AND h.Id=i.Id))
        THROW 51102,N'حواله صرافی جاری یافت نشد.',1;
    CREATE TABLE #Owned(Id bigint PRIMARY KEY);
    INSERT #Owned SELECT DISTINCT i.Id FROM #Ids i JOIN dbo.HawalaImportRows r ON r.TenantId=@TenantId AND r.BatchId=@BatchId AND (r.HawalaId=i.Id OR r.GeneratedSendHawalaId=i.Id);
    WHILE 1=1
    BEGIN
        INSERT #Owned SELECT DISTINCT h.Id FROM dbo.Hawalas h JOIN #Owned p ON p.Id=h.SourceHawalaId OR p.Id=h.CompensatesHawalaId
          WHERE h.TenantId=@TenantId AND NOT EXISTS(SELECT 1 FROM #Owned x WHERE x.Id=h.Id);
        IF @@ROWCOUNT=0 BREAK;
    END;
    IF EXISTS(SELECT Id FROM #Ids EXCEPT SELECT Id FROM #Owned) OR EXISTS(SELECT Id FROM #Owned EXCEPT SELECT Id FROM #Ids)
        THROW 51103,N'حواله‌های وابسته تغییر کرده‌اند؛ دوباره پیش‌نمایش بگیرید.',1;
    IF EXISTS(SELECT 1 FROM dbo.HawalaImportRows r JOIN #Ids i ON i.Id=r.HawalaId OR i.Id=r.GeneratedSendHawalaId WHERE r.TenantId=@TenantId AND r.BatchId<>@BatchId)
        THROW 51104,N'حواله با فایل دیگری مشترک است؛ حذف خودکار مجاز نیست.',1;
    SELECT s.Id,s.ConversionId,s.HawalaId INTO #Items FROM dbo.CorrespondentSettlementConversionHawalaItems s JOIN #Ids i ON i.Id=s.HawalaId WHERE s.TenantId=@TenantId;
    SELECT e.* INTO #Entries FROM dbo.LedgerEntries e WITH(UPDLOCK,HOLDLOCK)
      WHERE e.TenantId=@TenantId AND (EXISTS(SELECT 1 FROM #Ids i WHERE i.Id=e.HawalaId)
        OR EXISTS(SELECT 1 FROM #Items i WHERE i.Id=e.SettlementHawalaItemId)
        OR EXISTS(SELECT 1 FROM dbo.Transactions t JOIN #Ids i ON i.Id=t.HawalaId WHERE t.TenantId=@TenantId AND t.Id=e.TransactionId));
    DECLARE @Closed bit=0,@Now datetime2=SYSUTCDATETIME();
    IF EXISTS(SELECT 1 FROM dbo.CorrespondentAccountPeriodHawalas p JOIN #Ids i ON i.Id=p.HawalaId WHERE p.TenantId=@TenantId)
      OR EXISTS(SELECT 1 FROM #Entries e JOIN dbo.Accounts a ON a.TenantId=@TenantId AND a.Id=e.AccountId JOIN dbo.CorrespondentAccountPeriods p
        ON p.TenantId=@TenantId AND p.CorrespondentId=a.CorrespondentId AND p.PeriodTo>=e.CreatedAt)
        SET @Closed=1;
    IF @Closed=1
    BEGIN
        IF EXISTS(SELECT 1 FROM #Entries e JOIN dbo.Accounts a ON a.TenantId=@TenantId AND a.Id=e.AccountId JOIN dbo.CorrespondentAccountPeriods p
          ON p.TenantId=@TenantId AND p.CorrespondentId=a.CorrespondentId WHERE p.PeriodTo>=@Now)
            THROW 51105,N'دوره باز برای ثبت اصلاحیه موجود نیست.',1;
        DECLARE @Branch bigint=(SELECT TOP(1) Id FROM dbo.Branches WHERE TenantId=@TenantId ORDER BY Id),@Tx bigint;
        INSERT dbo.Transactions(TenantId,TransactionNo,TransactionType,Status,CreatedAt,CreatedBy,BranchId,Remarks)
          VALUES(@TenantId,N'IMPORT-DELETE-'+CONVERT(nvarchar(36),NEWID()),N'ImportDeletionAdjustment',N'Paid',@Now,@UserId,@Branch,N'اصلاح حذف آپلود گروهی در دوره باز؛ سوابق دوره بسته حفظ شدند.');
        SET @Tx=SCOPE_IDENTITY();
        INSERT dbo.LedgerEntries(TenantId,TransactionId,AccountId,CurrencyId,TalabKar,BadehKar,Description,CreatedAt)
          SELECT @TenantId,@Tx,AccountId,CurrencyId,
            CASE WHEN SUM(TalabKar-BadehKar)<0 THEN -SUM(TalabKar-BadehKar) ELSE 0 END,
            CASE WHEN SUM(TalabKar-BadehKar)>0 THEN SUM(TalabKar-BadehKar) ELSE 0 END,
            N'حذف آپلود گروهی '+CONVERT(nvarchar(20),@BatchId)+N'؛ برگشت اصل حواله و تبدیل مربوط',@Now
          FROM #Entries GROUP BY AccountId,CurrencyId HAVING SUM(TalabKar-BadehKar)<>0;
        -- Detach metadata only; historical amounts/dates remain unchanged.
        UPDATE e SET SettlementHawalaItemId=NULL FROM dbo.LedgerEntries e JOIN #Items i ON i.Id=e.SettlementHawalaItemId WHERE e.TenantId=@TenantId;
    END
    ELSE
        DELETE e FROM dbo.LedgerEntries e JOIN #Entries x ON x.Id=e.Id WHERE e.TenantId=@TenantId;
    UPDATE t SET Status=N'Cancel' FROM dbo.Transactions t JOIN #Ids i ON i.Id=t.HawalaId
      WHERE t.TenantId=@TenantId AND NOT EXISTS(SELECT 1 FROM dbo.LedgerEntries e WHERE e.TenantId=@TenantId AND e.TransactionId=t.Id);
    DELETE s FROM dbo.CorrespondentSettlementValuations s JOIN #Ids i ON i.Id=s.HawalaId WHERE s.TenantId=@TenantId;
    DELETE s FROM dbo.CorrespondentSettlementConversionHawalaItems s JOIN #Ids i ON i.Id=s.HawalaId WHERE s.TenantId=@TenantId;
    DELETE s FROM dbo.CorrespondentSettlementConversionHawalas s JOIN #Ids i ON i.Id=s.HawalaId WHERE s.TenantId=@TenantId;
    -- Keep immutable history and commission allocation references as an audit archive.
    UPDATE h SET IsDeleted=1,Status=N'Cancel',CancelledAt=@Now,CancelledBy=@UserId,
      CancelReason=N'حذف آپلود گروهی؛ اثر مالی حذف یا در دوره باز خنثی شد.'
      FROM dbo.Hawalas h JOIN #Ids i ON i.Id=h.Id WHERE h.TenantId=@TenantId;
END;
