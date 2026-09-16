using System.Data;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Application.Services;
using HawalaExchange.Domain.Entities;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Infrastructure.Services;

public sealed class CorrespondentSettlementService(
    ApplicationDbContext context) : ICorrespondentSettlementService
{
    private const string ClearingAccountCode = "SYS-SETTLEMENT-CLEARING";

    public async Task<CorrespondentSettlementPreviewDto> GetPreviewAsync(
        long correspondentId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = PinCurrentTenant();
        var correspondent = await GetConfiguredCorrespondentAsync(correspondentId, cancellationToken);
        var accountId = await GetCorrespondentAccountIdAsync(correspondentId, cancellationToken);
        var balances = await GetNetBalancesAsync(accountId, correspondent.SettlementCurrencyId!.Value, cancellationToken);
        var pendingCount = await PendingHawalas(correspondentId, correspondent.SettlementCurrencyId.Value)
            .CountAsync(cancellationToken);

        return new CorrespondentSettlementPreviewDto
        {
            CorrespondentId = correspondent.Id,
            CorrespondentName = correspondent.Name,
            TargetCurrencyId = correspondent.SettlementCurrencyId.Value,
            TargetCurrencyCode = correspondent.SettlementCurrency!.Code,
            TargetQuotationPriority = correspondent.SettlementCurrency.QuotationPriority,
            TargetDecimalPlaces = correspondent.SettlementCurrency.DecimalPlaces,
            Balances = balances,
            PendingHawalaCount = pendingCount
        };
    }

    public async Task<CorrespondentSettlementResultDto> ConvertHawalasAsync(
        ConvertHawalasToSettlementDto dto,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = PinCurrentTenant();
        var ids = dto.HawalaIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            throw new InvalidOperationException("حداقل یک حواله را برای تبدیل انتخاب کنید.");
        ValidateConversionRequest(dto.CorrespondentId, dto.Note);
        return await ExecuteConversionAsync(
            "Hawalas", dto.CorrespondentId, dto.Note,
            CreateIdTable(ids),
            CreateHawalaRateTable(dto.HawalaRates),
            cancellationToken);
    }

    public async Task<IReadOnlyList<CorrespondentSettlementHawalaBalanceDto>> GetHawalaPreviewAsync(
        long correspondentId,
        IReadOnlyCollection<long> hawalaIds,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = PinCurrentTenant();
        var ids = hawalaIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        var correspondent = await GetConfiguredCorrespondentAsync(correspondentId, cancellationToken);
        var targetCurrencyId = correspondent.SettlementCurrencyId!.Value;
        var validCount = await PendingHawalas(correspondentId, targetCurrencyId)
            .CountAsync(x => ids.Contains(x.Id), cancellationToken);
        if (validCount != ids.Length)
            throw new InvalidOperationException("یک یا چند حواله معتبر نیست یا قبلاً تبدیل شده است.");

        var accountId = await GetCorrespondentAccountIdAsync(correspondentId, cancellationToken);
        return await GetHawalaBalancesAsync(accountId, ids, targetCurrencyId, cancellationToken);
    }

    public async Task<CorrespondentSettlementResultDto> ConvertBalanceAsync(
        ConvertCorrespondentBalanceDto dto,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = PinCurrentTenant();
        ValidateConversionRequest(dto.CorrespondentId, dto.Note);
        if (!dto.Rates.Any(x => x.SourceCurrencyId > 0 && x.Rate > 0))
            throw new InvalidOperationException("حداقل یک ارز را برای تبدیل انتخاب کنید.");
        return await ExecuteConversionAsync(
            "Account", dto.CorrespondentId, dto.Note,
            CreateIdTable([]),
            CreateAccountRateTable(dto.Rates),
            cancellationToken);
    }

    private async Task<CorrespondentSettlementResultDto> ExecuteConversionAsync(
        string sourceMode,
        long correspondentId,
        string? note,
        DataTable hawalaIds,
        DataTable rates,
        CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_ProcessCorrespondentSettlement_v1";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 180;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
            command.Parameters.Add(new SqlParameter("@SourceMode", SqlDbType.NVarChar, 20) { Value = sourceMode });
            command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.BigInt) { Value = context.CurrentTenantId });
            command.Parameters.Add(new SqlParameter("@CurrentUserId", SqlDbType.BigInt) { Value = context.RequireCurrentUserId() });
            command.Parameters.Add(new SqlParameter("@CorrespondentId", SqlDbType.BigInt) { Value = correspondentId });
            command.Parameters.Add(new SqlParameter("@Note", SqlDbType.NVarChar, 500)
                { Value = string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim() });
            command.Parameters.Add(new SqlParameter("@HawalaIds", SqlDbType.Structured)
                { TypeName = "dbo.IdTableType_v1", Value = hawalaIds });
            command.Parameters.Add(new SqlParameter("@Rates", SqlDbType.Structured)
                { TypeName = "dbo.SettlementRateTableType_v1", Value = rates });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("نتیجه تبدیل مانده از دیتابیس دریافت نشد.");
            var result = new CorrespondentSettlementResultDto
            {
                ConversionId = reader.GetInt64(0),
                TransactionId = reader.GetInt64(1),
                HawalaCount = reader.GetInt32(2),
                CurrencyCount = reader.GetInt32(3)
            };
            var items = new List<CorrespondentSettlementResultItemDto>();
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CorrespondentSettlementResultItemDto
                {
                    HawalaId = reader.IsDBNull(0) ? null : reader.GetInt64(0),
                    HawalaNumber = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    SourceCurrencyCode = reader.GetString(2),
                    SourceAmount = reader.GetDecimal(3),
                    BalanceDirection = reader.GetString(4),
                    ExchangeRate = reader.GetDecimal(5),
                    TargetCurrencyCode = reader.GetString(6),
                    TargetAmount = reader.GetDecimal(7)
                });
            result.Items = items;
            return result;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static DataTable CreateIdTable(IEnumerable<long> values)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        foreach (var id in values.Where(x => x > 0).Distinct())
            table.Rows.Add(id);
        return table;
    }

    private static DataTable CreateHawalaRateTable(IEnumerable<HawalaSettlementRateDto> rates)
    {
        var table = CreateRateTable();
        foreach (var rate in rates.Where(x => x.HawalaId > 0 && x.SourceCurrencyId > 0 && x.Rate > 0)
                     .GroupBy(x => new { x.HawalaId, x.SourceCurrencyId }).Select(x => x.Last()))
            table.Rows.Add(rate.HawalaId, rate.SourceCurrencyId, rate.Rate);
        return table;
    }

    private static DataTable CreateAccountRateTable(IEnumerable<SettlementRateDto> rates)
    {
        var table = CreateRateTable();
        foreach (var rate in rates.Where(x => x.SourceCurrencyId > 0 && x.Rate > 0)
                     .GroupBy(x => x.SourceCurrencyId).Select(x => x.Last()))
            table.Rows.Add(0L, rate.SourceCurrencyId, rate.Rate);
        return table;
    }

    private static DataTable CreateRateTable()
    {
        var table = new DataTable();
        table.Columns.Add("HawalaId", typeof(long));
        table.Columns.Add("SourceCurrencyId", typeof(long));
        table.Columns.Add("Rate", typeof(decimal));
        return table;
    }

    private static void ValidateConversionRequest(long correspondentId, string? note)
    {
        if (correspondentId <= 0)
            throw new InvalidOperationException("نمایندگی معتبر انتخاب نشده است.");
        if (note?.Trim().Length > 500)
            throw new InvalidOperationException("یادداشت نمی‌تواند بیشتر از ۵۰۰ حرف باشد.");
    }

    public async Task<HawalaSettlementRateResultDto> UpdateHawalaRateAsync(
        UpdateHawalaSettlementRateDto dto,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = PinCurrentTenant();
        context.ChangeTracker.Clear();
        if (dto.HawalaId <= 0 || dto.SourceCurrencyId <= 0 || dto.Rate <= 0)
            throw new InvalidOperationException("حواله، ارز و نرخ معتبر الزامی است.");

        await using var dbTransaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var item = await context.CorrespondentSettlementConversionHawalaItems
            .Include(x => x.Hawala)
            .Include(x => x.SourceCurrency)
            .Include(x => x.Conversion).ThenInclude(x => x.TargetCurrency)
            .Include(x => x.Conversion).ThenInclude(x => x.Correspondent)
            .Include(x => x.LedgerEntries)
            .SingleOrDefaultAsync(x => x.HawalaId == dto.HawalaId && x.SourceCurrencyId == dto.SourceCurrencyId, cancellationToken)
            ?? throw new InvalidOperationException("نرخ تبدیل این حواله پیدا نشد.");

        if (item.Conversion.SourceMode != "Hawalas")
            throw new InvalidOperationException("نرخ این حواله از تبدیل کلی حساب ایجاد شده و جداگانه قابل تغییر نیست.");

        var sourceAmount = Math.Abs(item.SourceTalabKar - item.SourceBadehKar);
        var previousRate = item.ExchangeRate;
        var targetAmount = ConvertAmount(item.SourceCurrency, item.Conversion.TargetCurrency, sourceAmount, dto.Rate);
        var accountId = await GetCorrespondentAccountIdAsync(item.Conversion.CorrespondentId, cancellationToken);
        var clearingAccountId = await GetOrCreateClearingAccountIdAsync(cancellationToken);

        context.LedgerEntries.RemoveRange(item.LedgerEntries);
        item.ExchangeRate = dto.Rate;
        item.TargetTalabKar = item.SourceTalabKar > item.SourceBadehKar ? targetAmount : 0;
        item.TargetBadehKar = item.SourceBadehKar > item.SourceTalabKar ? targetAmount : 0;
        AddBalancedLedgerEntries(
            item.Conversion.TransactionId,
            accountId,
            clearingAccountId,
            item.SourceCurrencyId,
            item.Conversion.TargetCurrencyId,
            sourceAmount,
            targetAmount,
            item.SourceTalabKar > item.SourceBadehKar,
            item.Conversion.Correspondent.Name,
            item.Id);

        context.AuditLogs.Add(new AuditLog
        {
            UserId = context.RequireCurrentUserId(),
            Action = "UPDATE_HAWALA_SETTLEMENT_RATE",
            TableName = "CorrespondentSettlementConversionHawalaItems",
            RecordId = item.Id,
            OldValue = $"نرخ {previousRate}",
            NewValue = $"نرخ تبدیل حواله شماره {item.Hawala.Number} از {previousRate} به {dto.Rate} تغییر کرد.",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return new HawalaSettlementRateResultDto
        {
            HawalaId = item.HawalaId,
            HawalaNumber = item.Hawala.Number,
            PreviousRate = previousRate,
            ExchangeRate = dto.Rate,
            SourceCurrencyCode = item.SourceCurrency.Code,
            SourceAmount = sourceAmount,
            TargetCurrencyCode = item.Conversion.TargetCurrency.Code,
            TargetAmount = targetAmount
        };
    }

    private void AddBalancedLedgerEntries(
        long transactionId,
        long correspondentAccountId,
        long clearingAccountId,
        long sourceCurrencyId,
        long targetCurrencyId,
        decimal sourceAmount,
        decimal targetAmount,
        bool correspondentWasCreditor,
        string correspondentName,
        long? settlementHawalaItemId = null)
    {
        var description = $"تبدیل مانده نمایندگی {correspondentName} به ارز توافقی";
        if (correspondentWasCreditor)
        {
            AddEntry(transactionId, correspondentAccountId, sourceCurrencyId, 0, sourceAmount, description, settlementHawalaItemId);
            AddEntry(transactionId, clearingAccountId, sourceCurrencyId, sourceAmount, 0, description, settlementHawalaItemId);
            AddEntry(transactionId, correspondentAccountId, targetCurrencyId, targetAmount, 0, description, settlementHawalaItemId);
            AddEntry(transactionId, clearingAccountId, targetCurrencyId, 0, targetAmount, description, settlementHawalaItemId);
        }
        else
        {
            AddEntry(transactionId, correspondentAccountId, sourceCurrencyId, sourceAmount, 0, description, settlementHawalaItemId);
            AddEntry(transactionId, clearingAccountId, sourceCurrencyId, 0, sourceAmount, description, settlementHawalaItemId);
            AddEntry(transactionId, correspondentAccountId, targetCurrencyId, 0, targetAmount, description, settlementHawalaItemId);
            AddEntry(transactionId, clearingAccountId, targetCurrencyId, targetAmount, 0, description, settlementHawalaItemId);
        }
    }

    private void AddEntry(
        long transactionId,
        long accountId,
        long currencyId,
        decimal talabKar,
        decimal badehKar,
        string description,
        long? settlementHawalaItemId = null) => context.LedgerEntries.Add(new LedgerEntry
        {
            TransactionId = transactionId,
            AccountId = accountId,
            CurrencyId = currencyId,
            TalabKar = talabKar,
            BadehKar = badehKar,
            SettlementHawalaItemId = settlementHawalaItemId,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });

    private IQueryable<Hawala> PendingHawalas(long correspondentId, long targetCurrencyId) =>
        context.Hawalas.Where(x =>
            x.CorrespondentId == correspondentId &&
            x.Status != "Cancel" &&
            (x.HawalaType == "HawalaSend" ? x.ToCurrencyId : x.FromCurrencyId) != targetCurrencyId &&
            !x.SettlementConversionLinks.Any());

    private static decimal ConvertAmount(Currency source, Currency target, decimal sourceAmount, decimal rate)
    {
        var converted = CurrencyQuotationCalculator.ConvertFromAmount(
            source.Id, source.Code, source.QuotationPriority, sourceAmount,
            target.Id, target.Code, target.QuotationPriority, rate);
        var targetAmount = decimal.Round(
            converted.ToAmount,
            Math.Clamp(target.DecimalPlaces, 0, 8),
            MidpointRounding.AwayFromZero);
        if (targetAmount <= 0)
            throw new InvalidOperationException($"حاصل تبدیل {source.Code} معتبر نیست.");
        return targetAmount;
    }

    private async Task<List<CorrespondentSettlementHawalaBalanceDto>> GetHawalaBalancesAsync(
        long accountId,
        IReadOnlyCollection<long> hawalaIds,
        long targetCurrencyId,
        CancellationToken cancellationToken) => await context.LedgerEntries
        .Where(x => x.AccountId == accountId && x.HawalaId.HasValue &&
                    hawalaIds.Contains(x.HawalaId.Value) && x.CurrencyId != targetCurrencyId)
        .GroupBy(x => new { HawalaId = x.HawalaId!.Value, x.Hawala!.Number, x.CurrencyId, x.Currency!.Code, x.Currency.QuotationPriority })
        .Select(x => new CorrespondentSettlementHawalaBalanceDto
        {
            HawalaId = x.Key.HawalaId,
            HawalaNumber = x.Key.Number,
            SourceCurrencyId = x.Key.CurrencyId,
            SourceCurrencyCode = x.Key.Code,
            SourceQuotationPriority = x.Key.QuotationPriority,
            TalabKar = x.Sum(y => y.TalabKar) > x.Sum(y => y.BadehKar)
                ? x.Sum(y => y.TalabKar) - x.Sum(y => y.BadehKar) : 0,
            BadehKar = x.Sum(y => y.BadehKar) > x.Sum(y => y.TalabKar)
                ? x.Sum(y => y.BadehKar) - x.Sum(y => y.TalabKar) : 0
        })
        .Where(x => x.TalabKar > 0 || x.BadehKar > 0)
        .OrderBy(x => x.HawalaNumber)
        .ThenBy(x => x.SourceCurrencyCode)
        .ToListAsync(cancellationToken);

    private async Task<List<CorrespondentSettlementBalanceDto>> GetNetBalancesAsync(
        long accountId,
        long targetCurrencyId,
        CancellationToken cancellationToken) => await context.LedgerEntries
        .Where(x => x.AccountId == accountId && x.CurrencyId != targetCurrencyId)
        .GroupBy(x => new { x.CurrencyId, x.Currency!.Code, x.Currency.QuotationPriority })
        .Select(x => new CorrespondentSettlementBalanceDto
        {
            SourceCurrencyId = x.Key.CurrencyId,
            SourceCurrencyCode = x.Key.Code,
            SourceQuotationPriority = x.Key.QuotationPriority,
            TalabKar = x.Sum(y => y.TalabKar) > x.Sum(y => y.BadehKar)
                ? x.Sum(y => y.TalabKar) - x.Sum(y => y.BadehKar) : 0,
            BadehKar = x.Sum(y => y.BadehKar) > x.Sum(y => y.TalabKar)
                ? x.Sum(y => y.BadehKar) - x.Sum(y => y.TalabKar) : 0
        })
        .Where(x => x.TalabKar > 0 || x.BadehKar > 0)
        .OrderBy(x => x.SourceCurrencyCode)
        .ToListAsync(cancellationToken);

    private async Task<Correspondent> GetConfiguredCorrespondentAsync(long correspondentId, CancellationToken cancellationToken)
    {
        var correspondent = await context.Correspondents
            .Include(x => x.SettlementCurrency)
            .SingleOrDefaultAsync(x => x.Id == correspondentId, cancellationToken)
            ?? throw new KeyNotFoundException("نمایندگی یافت نشد.");
        if (!correspondent.SettlementCurrencyId.HasValue || correspondent.SettlementCurrency is null)
            throw new InvalidOperationException("برای این نمایندگی ارز توافقی تعیین نشده است.");
        if (!correspondent.SettlementCurrency.IsActive)
            throw new InvalidOperationException("ارز توافقی این نمایندگی غیرفعال است.");
        return correspondent;
    }

    private async Task<long> GetCorrespondentAccountIdAsync(long correspondentId, CancellationToken cancellationToken) =>
        await context.Accounts
            .Where(x => x.CorrespondentId == correspondentId && !x.IsArchived)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken) is var accountId && accountId > 0
                ? accountId
                : throw new InvalidOperationException("حساب فعال نمایندگی یافت نشد.");

    private async Task<long> GetOrCreateClearingAccountIdAsync(CancellationToken cancellationToken)
    {
        var existingAccount = await context.Accounts
            .Where(x => x.AccountCode == ClearingAccountCode)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingAccount != null)
        {
            if (existingAccount.AccountType != "CurrencyConversionClearing")
                throw new InvalidOperationException("کد حساب واسط تبدیل ارز قبلاً برای حساب دیگری استفاده شده است.");
            return existingAccount.Id;
        }

        var account = new Account
        {
            AccountCode = ClearingAccountCode,
            AccountName = "حساب واسط تبدیل ارز نمایندگی‌ها",
            AccountType = "CurrencyConversionClearing",
            IsArchived = false,
            CreatedAt = DateTime.UtcNow
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return account.Id;
    }

    private IDisposable PinCurrentTenant()
    {
        var tenantId = context.CurrentTenantId;
        if (tenantId <= 0)
            throw new InvalidOperationException("صرافی جاری تشخیص داده نشد. لطفاً دوباره وارد سیستم شوید.");
        return context.UseTenantScope(tenantId);
    }
}
