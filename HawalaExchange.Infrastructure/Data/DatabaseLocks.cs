using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HawalaExchange.Infrastructure.Data;

public static class DatabaseLocks
{
    public static async Task AcquireAsync(ApplicationDbContext context, string resource, int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required for an accounting lock.");
        if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (!context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource},@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout={timeoutMilliseconds};
                IF @result<0 THROW 51000,'The accounting operation is busy; retry shortly.',1;
                """, cancellationToken);
            return;
        }
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction();
        await using var command = new NpgsqlCommand("SELECT current_setting('lock_timeout')", connection, transaction);
        var previous = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        command.CommandText = "SELECT set_config('lock_timeout',@timeout,true)";
        // PostgreSQL's zero disables timeouts; a zero acquisition budget uses its try-lock below.
        command.Parameters.AddWithValue("timeout", Math.Max(timeoutMilliseconds, 1) + "ms");
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.Parameters.Clear();
        command.CommandText = timeoutMilliseconds == 0
            ? "SELECT pg_try_advisory_xact_lock(hashtextextended(@resource,0))"
            : "SELECT pg_advisory_xact_lock(hashtextextended(@resource,0))";
        command.Parameters.AddWithValue("resource", resource);
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is false) throw new InvalidOperationException("عملیات دیگری در جریان است؛ دوباره تلاش کنید.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new InvalidOperationException("عملیات دیگری در جریان است؛ دوباره تلاش کنید.", ex);
        }
        // Restore only after success: PostgreSQL requires rollback after a database error.
        command.Parameters.Clear();
        command.CommandText = "SELECT set_config('lock_timeout',@previous,true)";
        command.Parameters.AddWithValue("previous", previous);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
