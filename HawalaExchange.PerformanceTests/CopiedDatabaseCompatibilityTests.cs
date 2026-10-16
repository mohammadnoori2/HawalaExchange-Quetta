using HawalaExchange.Infrastructure.Data;
using HawalaExchange.Infrastructure.Services;
using HawalaExchange.PerformanceTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HawalaExchange.PerformanceTests;

public sealed class CopiedDatabaseCompatibilityTests
{
    [Fact]
    public async Task Copied_database_upgrades_without_changing_existing_records_and_passes_consistency_check()
    {
        var name=Environment.GetEnvironmentVariable("HAWALA_VALIDATION_COPY_DATABASE");
        if(string.IsNullOrWhiteSpace(name))return; // Explicit opt-in; never infer a live database from application settings.
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^HawalaExchangeQuettaTestCopy_[0-9]{8}_[0-9]{6}_[a-f0-9]{8}$"))
            throw new InvalidOperationException("Unsafe validation database.");
        var connection=$"Server=.;Database={name};Integrated Security=True;Encrypt=False;TrustServerCertificate=True";
        await using var sql=new SqlConnection(connection);await sql.OpenAsync();
        var before=await Fingerprints(sql);
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db=new ApplicationDbContext(options,new TestCurrentTenant());
        await db.Database.MigrateAsync();
        var after=await Fingerprints(sql);
        foreach(var table in before.Where(x=>x.Key!="dbo.__EFMigrationsHistory"))Assert.Equal(table.Value,after[table.Key]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        foreach(var tenant in await db.Tenants.AsNoTracking().ToListAsync())
        {
            using var tenantScope=db.UseTenantScope(tenant.Id);
            var controls=await TenantOperationalAccess.ReadAsync(db,tenant.Id);
            Assert.False(controls.ReadOnly);Assert.Null(controls.MaxUsers);Assert.Empty(TenantOperationalAccess.Disabled(controls));
            await db.Users.AsNoTracking().ToListAsync();
            await db.Hawalas.AsNoTracking().OrderByDescending(x=>x.Id).Take(50).ToListAsync();
            await db.HawalaImportJobs.AsNoTracking().OrderByDescending(x=>x.Id).Take(50).ToListAsync();
        }
        await using var check=sql.CreateCommand();check.CommandTimeout=300;check.CommandText=$"DBCC CHECKDB ([{name}]) WITH NO_INFOMSGS,ALL_ERRORMSGS";
        await check.ExecuteNonQueryAsync();
    }
    private static async Task<Dictionary<string,(long Count,int? Checksum)>> Fingerprints(SqlConnection sql)
    {
        var tables=new List<(string Schema,string Name)>();
        await using(var command=sql.CreateCommand())
        {
            command.CommandText="SELECT s.name,t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name";
            await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())tables.Add((reader.GetString(0),reader.GetString(1)));
        }
        var result=new Dictionary<string,(long,int?)>();
        foreach(var table in tables)
        {
            await using var command=sql.CreateCommand();command.CommandTimeout=180;
            command.CommandText=$"SELECT COUNT_BIG(*),CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM [{table.Schema.Replace("]","]]")}].[{table.Name.Replace("]","]]")}]";
            await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();result.Add(table.Schema+"."+table.Name,(reader.GetInt64(0),reader.IsDBNull(1)?null:reader.GetInt32(1)));
        }
        return result;
    }
}
