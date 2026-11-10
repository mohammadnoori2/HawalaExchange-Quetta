using System.Collections.Concurrent;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresSql
{
    private static readonly ConcurrentDictionary<string,string> Cache=new(StringComparer.Ordinal);
    public static string Read(string name)=>Cache.GetOrAdd(name,key=>
    {
        if(key is not ("DashboardQuery" or "DailyOperationalReportQuery" or "TransactionReportQuery" or "CommissionReportQuery" or "DailyJournalQuery" or "SettlementHistoryQuery" or "AccountStatementQuery" or "PendingStatementQuery"))
            throw new InvalidOperationException("Unrecognized PostgreSQL query resource.");
        using var stream=typeof(PostgresSql).Assembly.GetManifestResourceStream($"HawalaExchange.Infrastructure.Sql.PostgreSql.{key}.sql")
            ??throw new InvalidOperationException("Missing PostgreSQL query resource.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd().TrimEnd().TrimEnd(';');
    });
}
