using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace HawalaExchange.Infrastructure.Data;

internal static class PostgresSettlementHistory
{
    public static async Task CaptureAsync(ApplicationDbContext context,long conversion,DateTime? rateDate,bool confirm,CancellationToken token)
    {
        if(context.Database.CurrentTransaction is null)throw new InvalidOperationException("Settlement history requires the conversion transaction.");
        await using var command=new NpgsqlCommand("SELECT hawala.capture_settlement(@tenant,@conversion,@actor,@date,@offset,@confirm)",
            (NpgsqlConnection)context.Database.GetDbConnection(),(NpgsqlTransaction)context.Database.CurrentTransaction.GetDbTransaction());
        command.Parameters.AddWithValue("tenant",context.CurrentTenantId);command.Parameters.AddWithValue("conversion",conversion);command.Parameters.AddWithValue("actor",context.RequireCurrentUserId());
        command.Parameters.AddWithValue("date",NpgsqlDbType.Date,rateDate.HasValue?(object)DateOnly.FromDateTime(rateDate.Value):DBNull.Value);
        command.Parameters.AddWithValue("offset",(int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes);command.Parameters.AddWithValue("confirm",confirm);
        try{await command.ExecuteNonQueryAsync(token);}
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.RaiseException){throw new InvalidOperationException(ex.MessageText,ex);}
    }
}
