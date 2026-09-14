using System.Data.Common;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HawalaExchange.PerformanceTests.Infrastructure;

public sealed class CommandCounterInterceptor : DbCommandInterceptor
{
    private long count;
    private readonly ConcurrentQueue<CommandTiming> timings = new();

    public long Count => Interlocked.Read(ref count);
    public TimeSpan TotalDuration => TimeSpan.FromTicks(timings.Sum(x => x.Duration.Ticks));
    public CommandTiming? Slowest => timings.OrderByDescending(x => x.Duration).FirstOrDefault();

    public void Reset()
    {
        Interlocked.Exchange(ref count, 0);
        timings.Clear();
    }

    private void Increment() => Interlocked.Increment(ref count);
    private void Record(DbCommand command, CommandExecutedEventData eventData)
    {
        var summary = string.Join(' ', command.CommandText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        timings.Enqueue(new CommandTiming(
            eventData.Duration,
            summary.Length <= 180 ? summary : summary[..180]));
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }
}

public sealed record CommandTiming(TimeSpan Duration, string CommandSummary);
