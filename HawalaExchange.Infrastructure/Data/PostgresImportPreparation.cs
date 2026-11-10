using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresImportPreparation
{
    private static string Json(DataTable table)=>JsonSerializer.Serialize(table.Rows.Cast<DataRow>().Select(row=>table.Columns.Cast<DataColumn>().ToDictionary(c=>c.ColumnName,c=>row[c] is DBNull?null:row[c])));
    private static NpgsqlCommand Command(ApplicationDbContext context,string sql)
    {
        if(!context.Database.IsNpgsql() || context.Database.CurrentTransaction is null || context.CurrentTenantId<=0)throw new InvalidOperationException("Import preparation requires a tenant transaction.");
        var command=new NpgsqlCommand(sql,(NpgsqlConnection)context.Database.GetDbConnection(),(NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction()){CommandTimeout=120};
        command.Parameters.AddWithValue("tenant",context.CurrentTenantId);command.Parameters.AddWithValue("actor",context.RequireCurrentUserId());return command;
    }
    public static async Task<Dictionary<string,long>> CreateNamesAsync(ApplicationDbContext context,string kind,DataTable names,CancellationToken token=default)
    {
        var function=kind switch{"Locations"=>"bulk_import_locations","Correspondents"=>"bulk_import_correspondents",_=>throw new InvalidOperationException("Unknown import entity kind.")};
        await using var command=Command(context,$"SELECT * FROM hawala.{function}(@tenant,@actor,@names)");
        command.Parameters.AddWithValue("names",NpgsqlDbType.Jsonb,Json(names));
        try
        {
            await using var reader=await command.ExecuteReaderAsync(token);var result=new Dictionary<string,long>(StringComparer.Ordinal);
            while(await reader.ReadAsync(token))result.Add(reader.GetString(0),reader.GetInt64(1));return result;
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
    }
    public static async Task SaveAssignmentsAsync(ApplicationDbContext context,DataTable assignments,CancellationToken token=default)
    {
        await using var command=Command(context,"SELECT hawala.bulk_import_assignments(@tenant,@actor,@date,@assignments)");
        command.Parameters.AddWithValue("date",DateOnly.FromDateTime(DateTime.Today));command.Parameters.AddWithValue("assignments",NpgsqlDbType.Jsonb,Json(assignments));
        try{await command.ExecuteNonQueryAsync(token);}
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
    }
}
