using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

internal static class RelationalParameters
{
    public static void Add(DbCommand command,string name,SqlDbType type,object? value,int? size=null)
    {
        if(command is SqlCommand sql)
        {
            var parameter=size.HasValue?sql.Parameters.Add(name,type,size.Value):sql.Parameters.Add(name,type);
            parameter.Value=value??DBNull.Value;
            return;
        }
        if(command is not NpgsqlCommand pg)throw new NotSupportedException("Unsupported database command provider.");
        var nativeType=type switch
        {
            SqlDbType.BigInt=>NpgsqlDbType.Bigint,SqlDbType.Int=>NpgsqlDbType.Integer,SqlDbType.Bit=>NpgsqlDbType.Boolean,
            SqlDbType.Decimal=>NpgsqlDbType.Numeric,SqlDbType.Date=>NpgsqlDbType.Date,
            SqlDbType.DateTime2 or SqlDbType.DateTime=>NpgsqlDbType.Timestamp,
            SqlDbType.NVarChar or SqlDbType.VarChar=>NpgsqlDbType.Text,
            _=>throw new NotSupportedException($"Unsupported parameter type: {type}.")
        };
        if(value is DateTime date)
            value=type==SqlDbType.Date?(object)DateOnly.FromDateTime(date):DateTime.SpecifyKind(date,DateTimeKind.Unspecified);
        pg.Parameters.AddWithValue(name,nativeType,value??DBNull.Value);
    }
}
