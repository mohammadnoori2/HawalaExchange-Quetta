using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace HawalaExchange.Infrastructure.Data;

public static class PostgresSchema
{
    public static string Fingerprint
    {
        get
        {
            var assembly = typeof(PostgresSchema).Assembly;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var name in assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("HawalaExchange.Infrastructure.Sql.PostgreSql.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(name));
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var buffer = new MemoryStream();stream.CopyTo(buffer);hash.AppendData(buffer.ToArray());
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    public static async Task MarkCandidateReadyAsync(ApplicationDbContext context, CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql() || !System.Text.RegularExpressions.Regex.IsMatch(
            new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Database ?? "",
            @"\Ahawala_pg_candidate_\d{8}_\d{6}_[a-f0-9]{12}\z"))
            throw new InvalidOperationException("Only an isolated PostgreSQL application candidate may be marked ready.");
        // Fingerprint consists exclusively of SHA-256 hex characters, never input SQL.
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE OR REPLACE FUNCTION hawala.application_compatibility_verified() RETURNS text LANGUAGE sql IMMUTABLE AS 'SELECT ''{Fingerprint}''::text'", token);
    }
    public static async Task InstallCoreAsync(ApplicationDbContext context, CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql()) throw new InvalidOperationException("PostgreSQL is required.");
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('HawalaSchema:Core',0))", token);
        var assembly = typeof(PostgresSchema).Assembly;
        await using var command = new NpgsqlCommand("", (NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction()) { CommandTimeout = 120 };
        foreach (var resource in new[] { "Core", "AedDeals", "Balances", "TextCollation", "ImportStaging",
            "PeriodicCommission", "Settlement", "SettlementHistory", "AccountOperations", "ImportPreparation", "ImportDeletion", "TextLengths" })
        {
            await using var stream = assembly.GetManifestResourceStream($"HawalaExchange.Infrastructure.Sql.PostgreSql.{resource}.sql")
                ?? throw new InvalidOperationException($"Missing PostgreSQL schema resource: {resource}.");
            using var reader = new StreamReader(stream);
            command.CommandText = await reader.ReadToEndAsync(token);
            await command.ExecuteNonQueryAsync(token);
        }
        if (transaction is not null) await transaction.CommitAsync(token);
    }

    public static async Task ValidateApplicationReadyAsync(ApplicationDbContext context, CancellationToken token = default)
    {
        // A data archive must never accidentally execute the SQL Server migration/seed/rebuild path.
        if (!context.Database.IsNpgsql()) throw new InvalidOperationException("PostgreSQL is required.");
        var ready = await context.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname='hawala' AND p.proname='application_compatibility_verified') AS "Value"
            """).SingleAsync(token);
        if (ready)
        {
            var installed = await context.Database.SqlQueryRaw<string>(
                "SELECT hawala.application_compatibility_verified() AS \"Value\"").SingleAsync(token);
            if (installed == Fingerprint) return;
        }
        throw new InvalidOperationException(
            "This PostgreSQL database is a staged migration. Financial procedure compatibility has not been verified; startup was stopped before migrations or seeding.");
    }
}
