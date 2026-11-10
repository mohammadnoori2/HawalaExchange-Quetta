using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresImportDeletion
{
    public sealed record PreviewDeletion(string FileName, int RowCount, int DeletedHawalaCount);

    private static NpgsqlCommand Command(ApplicationDbContext context, string sql)
    {
        if (!context.Database.IsNpgsql() || context.Database.CurrentTransaction is null || context.CurrentTenantId <= 0)
            throw new InvalidOperationException("Import deletion requires a PostgreSQL tenant transaction.");
        var command = new NpgsqlCommand(sql, (NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction()) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("tenant", context.CurrentTenantId);
        return command;
    }

    private static async Task ExecuteAsync(NpgsqlCommand command, CancellationToken token)
    {
        try { await command.ExecuteNonQueryAsync(token); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
        { throw new InvalidOperationException(ex.MessageText, ex); }
    }

    public static async Task ReverseLedgerAsync(ApplicationDbContext context, long[] ids, CancellationToken token = default)
    {
        await using var command = Command(context, "SELECT hawala.reverse_import_ledger(@tenant,@actor,@ids)");
        command.Parameters.AddWithValue("actor", context.RequireCurrentUserId());
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(ids));
        await ExecuteAsync(command, token);
    }

    public static async Task DeleteAccountingAsync(ApplicationDbContext context, long batch, long[] ids, CancellationToken token = default)
    {
        await using var command = Command(context, "SELECT hawala.delete_import_accounting(@tenant,@actor,@batch,@ids)");
        command.Parameters.AddWithValue("actor", context.RequireCurrentUserId());
        command.Parameters.AddWithValue("batch", batch);
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(ids));
        await ExecuteAsync(command, token);
    }

    public static async Task SaveDraftAsync(ApplicationDbContext context, long batch, string rowsJson, CancellationToken token = default)
    {
        await using var command = Command(context, "SELECT hawala.save_import_draft(@tenant,@batch,@rows)");
        command.Parameters.AddWithValue("batch", batch);
        command.Parameters.AddWithValue("rows", NpgsqlDbType.Jsonb, rowsJson);
        await ExecuteAsync(command, token);
    }

    public static async Task<PreviewDeletion> DeletePreviewAsync(ApplicationDbContext context, long batch, CancellationToken token = default)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        await context.EnsureCurrentTenantWriteAccessAsync(token);
        await using var command = Command(context, "SELECT hawala.delete_preview_import(@tenant,@batch)");
        command.Parameters.AddWithValue("batch", batch);
        PreviewDeletion result;
        try
        {
            var json = (string)(await command.ExecuteScalarAsync(token))!;
            result = JsonSerializer.Deserialize<PreviewDeletion>(json) ?? throw new InvalidOperationException("Missing deletion result.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
        { throw new InvalidOperationException(ex.MessageText, ex); }
        if (transaction is not null) await transaction.CommitAsync(token);
        return result;
    }
}
