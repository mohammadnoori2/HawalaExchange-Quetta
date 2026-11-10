using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresImportStaging
{
    public static Task ValidateAsync(ApplicationDbContext context,long batchId,CancellationToken token=default)=>
        Run(context,"SELECT hawala.validate_import(@tenant,@batch)",batchId,null,null,token);
    public static Task PrepareAsync(ApplicationDbContext context,long batchId,long ownLocation,DataTable mappings,CancellationToken token=default)=>
        Run(context,"SELECT hawala.prepare_import(@tenant,@batch,@own,@mappings)",batchId,ownLocation,mappings,token);
    public static Task FinalizeAsync(ApplicationDbContext context,long batchId,DataTable mappings,CancellationToken token=default)=>
        Run(context,"SELECT hawala.finalize_import(@tenant,@batch,@actor,@mappings)",batchId,null,mappings,token);
    private static async Task Run(ApplicationDbContext context,string sql,long batch,long? own,DataTable? mappings,CancellationToken token)
    {
        if(!context.Database.IsNpgsql() || context.Database.CurrentTransaction is null || context.CurrentTenantId<=0)
            throw new InvalidOperationException("Import staging requires a PostgreSQL tenant transaction.");
        await using var command=new NpgsqlCommand(sql,(NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction()){CommandTimeout=120};
        command.Parameters.AddWithValue("tenant",context.CurrentTenantId);command.Parameters.AddWithValue("batch",batch);
        if(own.HasValue)command.Parameters.AddWithValue("own",own.Value);
        if(mappings is not null)
        {
            var rows=mappings.Rows.Cast<DataRow>().Select(row=>mappings.Columns.Cast<DataColumn>().ToDictionary(c=>c.ColumnName,c=>row[c] is DBNull?null:row[c]));
            command.Parameters.AddWithValue("mappings",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(rows));
        }
        if(sql.Contains("@actor",StringComparison.Ordinal))command.Parameters.AddWithValue("actor",context.RequireCurrentUserId());
        try{await command.ExecuteNonQueryAsync(token);}
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
    }
}
