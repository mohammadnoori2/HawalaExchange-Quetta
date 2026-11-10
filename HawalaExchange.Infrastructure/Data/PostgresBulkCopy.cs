using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresBulkCopy
{
    public static async Task WriteAsync(ApplicationDbContext context,string destination,DataTable table,
        CancellationToken token=default,Action<long>? progress=null)
    {
        destination=destination.Replace("[","").Replace("]","").Replace("dbo.","");
        if(destination is not("Hawalas" or "LedgerEntries" or "HawalaImportRows"))
            throw new InvalidOperationException("Unsupported bulk-copy destination.");
        if(!context.Database.IsNpgsql() || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("PostgreSQL bulk copy requires the caller's transaction.");
        if(context.CurrentTenantId<=0 || !table.Columns.Contains("TenantId") || table.Rows.Cast<DataRow>().Any(row=>Convert.ToInt64(row["TenantId"])!=context.CurrentTenantId))
            throw new InvalidOperationException("Bulk copy cannot cross tenant boundaries.");
        var connection=(NpgsqlConnection)context.Database.GetDbConnection();
        var types=new Dictionary<string,NpgsqlDbType>(StringComparer.Ordinal);
        await using(var command=new NpgsqlCommand("SELECT column_name,data_type FROM information_schema.columns WHERE table_schema='public' AND table_name=@table",connection))
        {
            command.Parameters.AddWithValue("table",destination);
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))types[reader.GetString(0)]=reader.GetString(1) switch
            {
                "bigint"=>NpgsqlDbType.Bigint,"integer"=>NpgsqlDbType.Integer,"smallint"=>NpgsqlDbType.Smallint,
                "numeric"=>NpgsqlDbType.Numeric,"boolean"=>NpgsqlDbType.Boolean,"bytea"=>NpgsqlDbType.Bytea,
                "uuid"=>NpgsqlDbType.Uuid,"date"=>NpgsqlDbType.Date,"timestamp without time zone"=>NpgsqlDbType.Timestamp,
                "timestamp with time zone"=>NpgsqlDbType.TimestampTz,"text" or "character varying"=>NpgsqlDbType.Text,
                _=>throw new NotSupportedException("Unsupported bulk-copy column type.")
            };
        }
        string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var columns=table.Columns.Cast<DataColumn>().ToArray();
        foreach(var column in columns)if(!types.ContainsKey(column.ColumnName))throw new InvalidOperationException("Bulk-copy column is absent from the destination.");
        await using var importer=await connection.BeginBinaryImportAsync($"COPY public.{Quote(destination)} ({string.Join(",",columns.Select(c=>Quote(c.ColumnName)))}) FROM STDIN(FORMAT BINARY)",token);
        importer.Timeout=TimeSpan.FromSeconds(120);
        long count=0;
        foreach(DataRow row in table.Rows)
        {
            await importer.StartRowAsync(token);
            foreach(var column in columns)
            {
                var value=row[column];var type=types[column.ColumnName];
                if(value is DBNull){await importer.WriteNullAsync(token);continue;}
                if(value is DateTime date)value=type==NpgsqlDbType.Date?(object)DateOnly.FromDateTime(date):DateTime.SpecifyKind(date,type==NpgsqlDbType.Timestamp?DateTimeKind.Unspecified:DateTimeKind.Utc);
                await importer.WriteAsync(value,type,token);
            }
            if(++count%250==0)progress?.Invoke(count);
        }
        await importer.CompleteAsync(token);
        progress?.Invoke(count);
    }
}
