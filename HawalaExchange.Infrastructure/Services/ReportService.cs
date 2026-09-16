using System.Data;
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
                command.Parameters.Add("@UtcOffsetMinutes", SqlDbType.Int).Value = range.UtcOffsetMinutes;
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
        return await WithCommandAsync(
            "[dbo].[usp_GetTransactionReport_v1]",
            command =>
            {
                AddCommonReportParameters(command, range, branchId, currencyId);
                command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = Db(status);
                command.Parameters.Add("@CustomerId", SqlDbType.BigInt).Value = Db(customerId);
                command.Parameters.Add("@CorrespondentId", SqlDbType.BigInt).Value = Db(correspondentId);
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
                command.Parameters.Add("@UtcOffsetMinutes", SqlDbType.Int).Value = range.UtcOffsetMinutes;
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
        Action<SqlCommand> configure,
        Func<SqlDataReader, Task<T>> read)
    {
        var connection = (SqlConnection)context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction() as SqlTransaction;
            await using var command = new SqlCommand(procedure, connection, transaction)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 60
            };
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
        SqlCommand command,
        ReportRange range,
        long? branchId,
        long? currencyId)
    {
        command.Parameters.Add("@TenantId", SqlDbType.BigInt).Value = context.CurrentTenantId;
        command.Parameters.Add("@FromUtc", SqlDbType.DateTime2).Value = range.FromUtc;
        command.Parameters.Add("@ToUtcExclusive", SqlDbType.DateTime2).Value = range.ToUtcExclusive;
        command.Parameters.Add("@BranchId", SqlDbType.BigInt).Value = Db(branchId);
        command.Parameters.Add("@CurrencyId", SqlDbType.BigInt).Value = Db(currencyId);
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
