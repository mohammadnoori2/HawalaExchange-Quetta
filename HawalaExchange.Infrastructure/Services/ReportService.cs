using System.Data;
using System.Data.Common;
using HawalaExchange.Application.DTOs;
using HawalaExchange.Application.Interfaces.Services;
using HawalaExchange.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HawalaExchange.Application.Services;

public sealed class ReportService(ApplicationDbContext context) : IReportService
{
    public async Task<DailyReportDto> GetDailyReportAsync(
        DateTime date,
        long branchId,
        long? currencyId = null)
    {
        var reports = await ReadDailyReportsAsync(date, date, NormalizeId(branchId), currencyId);
        return reports.Single();
    }

    public async Task<IEnumerable<DailyReportDto>> GetDailyReportRangeAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null,
        long? currencyId = null) =>
        await ReadDailyReportsAsync(fromDate, toDate, NormalizeId(branchId), currencyId);

    public async Task<IEnumerable<TransactionReportDto>> GetTransactionReportAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null,
        string? status = null,
        long? currencyId = null) =>
        await ReadTransactionReportsAsync(
            fromDate, toDate, NormalizeId(branchId), status, currencyId, null, null);

    public async Task<IEnumerable<CommissionReportDto>> GetCommissionReportAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null,
        long? currencyId = null) =>
        await ReadCommissionReportsAsync(fromDate, toDate, NormalizeId(branchId), currencyId);

    public async Task<IEnumerable<TrialBalanceDto>> GetTrialBalanceAsync(
        DateTime asOfDate,
        long? branchId = null)
    {
        var endExclusive = asOfDate.Date.AddDays(1).ToUniversalTime();
        return await context.LedgerEntries
            .AsNoTracking()
            .Where(x => x.CreatedAt < endExclusive)
            .GroupBy(x => new { x.AccountId, x.Account!.AccountCode, x.Account.AccountName })
            .Select(group => new TrialBalanceDto
            {
                AccountId = group.Key.AccountId,
                AccountCode = group.Key.AccountCode,
                AccountName = group.Key.AccountName,
                TotalDebit = group.Sum(x => x.TalabKar),
                TotalCredit = group.Sum(x => x.BadehKar),
                Balance = group.Sum(x => x.TalabKar - x.BadehKar),
                BalanceType = group.Sum(x => x.TalabKar - x.BadehKar) >= 0 ? "Debit" : "Credit"
            })
            .OrderBy(x => x.AccountCode)
            .ToListAsync();
    }

    public async Task<IEnumerable<TransactionReportDto>> GetCustomerTransactionReportAsync(
        long customerId,
        DateTime fromDate,
        DateTime toDate) =>
        await ReadTransactionReportsAsync(fromDate, toDate, null, null, null, customerId, null);

    public async Task<IEnumerable<TransactionReportDto>> GetCorrespondentTransactionReportAsync(
        long correspondentId,
        DateTime fromDate,
        DateTime toDate) =>
        await ReadTransactionReportsAsync(fromDate, toDate, null, null, null, null, correspondentId);

    public async Task<decimal> GetTotalCommissionAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null)
    {
        var rows = await ReadCommissionReportsAsync(fromDate, toDate, NormalizeId(branchId), null);
        return rows.Sum(x => x.TotalCommission);
    }

    public async Task<decimal> GetTotalExpensesAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null)
    {
        var rows = await ReadDailyReportsAsync(fromDate, toDate, NormalizeId(branchId), null);
        return rows.Sum(x => x.TotalExpenses);
    }

    public async Task<decimal> GetNetProfitAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId = null)
    {
        var rows = await ReadDailyReportsAsync(fromDate, toDate, NormalizeId(branchId), null);
        return rows.Sum(x => x.NetIncome);
    }

    private async Task<List<DailyReportDto>> ReadDailyReportsAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId,
        long? currencyId)
    {
        var range = CreateRange(fromDate, toDate);
        return await WithCommandAsync(
            "[dbo].[usp_GetDailyOperationalReport_v1]",
            command =>
            {
                AddCommonReportParameters(command, range, branchId, currencyId);
                RelationalParameters.Add(command,"@UtcOffsetMinutes",SqlDbType.Int,range.UtcOffsetMinutes);
            },
            async reader =>
            {
                var rows = new List<DailyReportDto>();
                while (await reader.ReadAsync())
                {
                    rows.Add(new DailyReportDto
                    {
                        Date = reader.GetDateTime(0),
                        BranchId = reader.GetInt64(1),
                        BranchName = reader.GetString(2),
                        CurrencyId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                        CurrencyCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                        TotalTransactions = reader.GetInt32(5),
                        TotalSendAmount = reader.GetDecimal(6),
                        TotalReceiveAmount = reader.GetDecimal(7),
                        TotalCommission = reader.GetDecimal(8),
                        TotalAgentCommission = reader.GetDecimal(9),
                        TotalExpenses = reader.GetDecimal(10),
                        NetIncome = reader.GetDecimal(11)
                    });
                }

                return rows;
            });
    }

    private async Task<List<TransactionReportDto>> ReadTransactionReportsAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId,
        string? status,
        long? currencyId,
        long? customerId,
        long? correspondentId)
    {
        var range = CreateRange(fromDate, toDate);
        var reports = await WithCommandAsync(
            "[dbo].[usp_GetTransactionReport_v1]",
            command =>
            {
                AddCommonReportParameters(command, range, branchId, currencyId);
                RelationalParameters.Add(command,"@Status",SqlDbType.NVarChar,status,20);
                RelationalParameters.Add(command,"@CustomerId",SqlDbType.BigInt,customerId);
                RelationalParameters.Add(command,"@CorrespondentId",SqlDbType.BigInt,correspondentId);
            },
            async reader =>
            {
                var rows = new List<TransactionReportDto>();
                while (await reader.ReadAsync())
                {
                    rows.Add(new TransactionReportDto
                    {
                        TransactionNo = reader.GetString(0),
                        TransactionType = reader.GetString(1),
                        CreatedAt = reader.GetDateTime(2),
                        CustomerName = reader.GetString(3),
                        SenderName = reader.GetString(4),
                        ReceiverName = reader.GetString(5),
                        FromCurrency = reader.GetString(6),
                        FromAmount = reader.GetDecimal(7),
                        ToCurrency = reader.GetString(8),
                        ToAmount = reader.GetDecimal(9),
                        Commission = reader.GetDecimal(10),
                        CommissionCurrency = reader.GetString(11),
                        Status = reader.GetString(12)
                    });
                }

                return rows;
            });
        var numbers = reports.Select(x => x.TransactionNo).Distinct().ToArray();
        if (numbers.Length > 0)
        {
            var references = await context.Transactions.AsNoTracking()
                .Where(x => numbers.Contains(x.TransactionNo) && x.CustomerId != null)
                .Select(x => new { x.TransactionNo, x.TransactionType, x.CustomerId }).ToListAsync();
            var customers = references.GroupBy(x => (x.TransactionNo, x.TransactionType))
                .ToDictionary(g => g.Key, g => g.Select(x => x.CustomerId).Distinct().Count() == 1 ? g.First().CustomerId : null);
            foreach (var row in reports)
                row.CustomerId = customers.GetValueOrDefault((row.TransactionNo, row.TransactionType));
        }
        return reports;
    }

    private async Task<List<CommissionReportDto>> ReadCommissionReportsAsync(
        DateTime fromDate,
        DateTime toDate,
        long? branchId,
        long? currencyId)
    {
        var range = CreateRange(fromDate, toDate);
        return await WithCommandAsync(
            "[dbo].[usp_GetCommissionReport_v1]",
            command =>
            {
                AddCommonReportParameters(command, range, branchId, currencyId);
                RelationalParameters.Add(command,"@UtcOffsetMinutes",SqlDbType.Int,range.UtcOffsetMinutes);
            },
            async reader =>
            {
                var rows = new List<CommissionReportDto>();
                while (await reader.ReadAsync())
                {
                    rows.Add(new CommissionReportDto
                    {
                        Date = reader.GetDateTime(0),
                        BranchId = reader.GetInt64(1),
                        BranchName = reader.GetString(2),
                        CurrencyId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                        CurrencyCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                        TotalCommission = reader.GetDecimal(5),
                        TotalAgentCommission = reader.GetDecimal(6),
                        NetCommission = reader.GetDecimal(7)
                    });
                }

                return rows;
            });
    }

    private async Task<T> WithCommandAsync<T>(
        string procedure,
        Action<DbCommand> configure,
        Func<DbDataReader, Task<T>> read)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            await using var command=connection.CreateCommand();
            command.Transaction=context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText=context.Database.IsNpgsql()?PostgresSql.Read(procedure switch
            {
                "[dbo].[usp_GetDailyOperationalReport_v1]"=>"DailyOperationalReportQuery",
                "[dbo].[usp_GetTransactionReport_v1]"=>"TransactionReportQuery",
                "[dbo].[usp_GetCommissionReport_v1]"=>"CommissionReportQuery",
                _=>throw new InvalidOperationException("Unknown PostgreSQL report.")
            }):procedure;
            command.CommandType=context.Database.IsNpgsql()?CommandType.Text:CommandType.StoredProcedure;
            command.CommandTimeout=60;
            configure(command);
            await using var reader = await command.ExecuteReaderAsync();
            return await read(reader);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private void AddCommonReportParameters(
        DbCommand command,
        ReportRange range,
        long? branchId,
        long? currencyId)
    {
        RelationalParameters.Add(command,"@TenantId",SqlDbType.BigInt,context.CurrentTenantId);
        RelationalParameters.Add(command,"@FromUtc",SqlDbType.DateTime2,range.FromUtc);
        RelationalParameters.Add(command,"@ToUtcExclusive",SqlDbType.DateTime2,range.ToUtcExclusive);
        RelationalParameters.Add(command,"@BranchId",SqlDbType.BigInt,branchId);
        RelationalParameters.Add(command,"@CurrencyId",SqlDbType.BigInt,currencyId);
    }

    private static ReportRange CreateRange(DateTime fromDate, DateTime toDate)
    {
        if (fromDate.Date > toDate.Date)
            throw new InvalidOperationException("تاریخ شروع نمی‌تواند بعد از تاریخ ختم باشد.");

        var localStart = fromDate.Date;
        var localEnd = toDate.Date.AddDays(1);
        var utcStart = localStart.ToUniversalTime();
        var utcEnd = localEnd.ToUniversalTime();
        var offset = checked((int)(localStart - utcStart).TotalMinutes);
        return new ReportRange(utcStart, utcEnd, offset);
    }

    private static long? NormalizeId(long? value) => value is > 0 ? value : null;
    private static object Db(object? value) => value ?? DBNull.Value;

    private sealed record ReportRange(
        DateTime FromUtc,
        DateTime ToUtcExclusive,
        int UtcOffsetMinutes);
}
