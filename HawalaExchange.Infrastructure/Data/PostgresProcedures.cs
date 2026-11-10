using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

internal static class PostgresProcedures
{
    public static async Task<DbDataReader> AccountOperationsAsync(ApplicationDbContext context,Dictionary<string,object?> values,CancellationToken token)
    {
        values["TenantId"]=context.CurrentTenantId;
        var connection=(NpgsqlConnection)context.Database.GetDbConnection();var close=connection.State!=ConnectionState.Open;
        if(close)await connection.OpenAsync(token);
        try
        {
            await using var command=new NpgsqlCommand("SELECT hawala.account_operations_page(@request)::text",connection,
                context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction){CommandTimeout=60};
            command.Parameters.AddWithValue("request",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(values));
            return PostgresResultReader.Create((string)(await command.ExecuteScalarAsync(token))!);
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
        finally{if(close)await connection.CloseAsync();}
    }
    public static async Task<DbDataReader> SettlementAsync(ApplicationDbContext context,string mode,long correspondent,string? note,DataTable ids,DataTable rates,CancellationToken token)
    {
        if(context.Database.CurrentTransaction is null)throw new InvalidOperationException("Settlement requires the outer transaction.");
        object Rows(DataTable table)=>table.Rows.Cast<DataRow>().Select(row=>table.Columns.Cast<DataColumn>().ToDictionary(c=>c.ColumnName,c=>row[c] is DBNull?null:row[c])).ToArray();
        var values=new Dictionary<string,object?>{["SourceMode"]=mode,["TenantId"]=context.CurrentTenantId,["CurrentUserId"]=context.RequireCurrentUserId(),["CorrespondentId"]=correspondent,["Note"]=note,["HawalaIds"]=Rows(ids),["Rates"]=Rows(rates)};
        await using var command=new NpgsqlCommand("SELECT hawala.settlement(@request)::text",(NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction()){CommandTimeout=180};
        command.Parameters.AddWithValue("request",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(values));
        try{return PostgresResultReader.Create((string)(await command.ExecuteScalarAsync(token))!);}
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
    }
    public static async Task<DbDataReader> CommissionAsync(ApplicationDbContext context,Action<SqlParameterCollection> configure,CancellationToken token)
    {
        using var parameters=new SqlCommand();configure(parameters.Parameters);
        var values=parameters.Parameters.Cast<SqlParameter>().ToDictionary(p=>p.ParameterName.TrimStart('@'),p=>
            p.Value is DataTable table?(object)table.Rows.Cast<DataRow>().Select(row=>table.Columns.Cast<DataColumn>().ToDictionary(c=>c.ColumnName,c=>row[c] is DBNull?null:row[c])).ToArray():p.Value is DBNull?null:p.Value);
        values["TenantId"]=context.CurrentTenantId;values["CurrentUserId"]=context.RequireCurrentUserId();
        if((string?)values["Mode"]=="Post")await context.EnsureCurrentTenantWriteAccessAsync(token);
        var connection=(NpgsqlConnection)context.Database.GetDbConnection();var close=connection.State!=ConnectionState.Open;
        if(close)await connection.OpenAsync(token);
        try
        {
            await using var command=new NpgsqlCommand("SELECT hawala.periodic_commission(@request)::text",connection,
                context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction){CommandTimeout=180};
            command.Parameters.AddWithValue("request",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(values));
            return PostgresResultReader.Create((string)(await command.ExecuteScalarAsync(token))!);
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
        finally{if(close)await connection.CloseAsync();}
    }
    public static async Task<long> AedAsync(ApplicationDbContext context,string procedure,
        Action<SqlParameterCollection> configure,CancellationToken token)
    {
        var operation=procedure.Replace("[","").Replace("]","") switch
        {
            "dbo.usp_CreateAedDeal_v1"=>"Create", "dbo.usp_ConvertAedDeal_v1"=>"Convert",
            "dbo.usp_ReverseAedDealConversion_v1"=>"ReverseConversion", "dbo.usp_CancelAedDeal_v1"=>"Cancel",
            _=>throw new InvalidOperationException("Unknown AED operation.")
        };
        await context.EnsureCurrentTenantWriteAccessAsync(token);
        using var parameters=new SqlCommand();
        configure(parameters.Parameters);
        var values=parameters.Parameters.Cast<SqlParameter>().ToDictionary(p=>p.ParameterName.TrimStart('@'),p=>p.Value is DBNull?null:p.Value);
        values["Operation"]=operation;values["TenantId"]=context.CurrentTenantId;values["CurrentUserId"]=context.RequireCurrentUserId();
        var connection=(NpgsqlConnection)context.Database.GetDbConnection();
        var close=connection.State!=ConnectionState.Open;
        if(close)await connection.OpenAsync(token);
        try
        {
            await using var command=new NpgsqlCommand("SELECT hawala.process_aed(@request)",connection,
                context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction){CommandTimeout=60};
            command.Parameters.AddWithValue("request",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(values));
            return (long)(await command.ExecuteScalarAsync(token))!;
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException)
        {throw new InvalidOperationException(ex.MessageText,ex);}
        finally{if(close)await connection.CloseAsync();}
    }
}
