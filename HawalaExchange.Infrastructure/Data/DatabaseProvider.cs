using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HawalaExchange.Infrastructure.Data;

public static class DatabaseProvider
{
    public static void Configure(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "SqlServer";
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing SQL Server connection string."));
            return;
        }
        if (!provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database:Provider must be SqlServer or PostgreSQL.");
        var connection = configuration.GetConnectionString("PostgreSQL");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("PostgreSQL requires its own ConnectionStrings:PostgreSQL setting.");
        var postgresConnection=new NpgsqlConnectionStringBuilder(connection){Timezone="UTC"};
        options.UseNpgsql(postgresConnection.ConnectionString, pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "public"));
    }
}
