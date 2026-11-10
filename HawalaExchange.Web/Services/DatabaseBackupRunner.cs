using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HawalaExchange.Web.Services;

public sealed class DatabaseBackupRunner(IConfiguration configuration, IOptions<DatabaseBackupOptions> options) : IDatabaseBackupRunner
{
    public async Task<string> CreateAsync(string slot, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        var provider = configuration["Database:Provider"] ?? "SqlServer";
        if (provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return await BackupPostgresAsync(slot, timeout.Token);
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            return await BackupSqlServerAsync(slot, timeout.Token);
        throw new InvalidOperationException("Unsupported database backup provider.");
    }

    public static ProcessStartInfo PostgresCommand(string executable, string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--format=custom");
        start.ArgumentList.Add("--no-password");
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGSSLMODE"] = connection.SslMode.ToString().ToLowerInvariant().Replace("verifyca", "verify-ca").Replace("verifyfull", "verify-full");
        if (!string.IsNullOrEmpty(connection.RootCertificate)) start.Environment["PGSSLROOTCERT"] = connection.RootCertificate;
        start.Environment["PGCONNECT_TIMEOUT"] = "30";
        return start;
    }

    private async Task<string> BackupPostgresAsync(string slot, CancellationToken cancellationToken)
    {
        var connection = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("Missing PostgreSQL connection string.");
        var target = Path.Combine(options.Value.Directory, $"hawala_{slot}.dump");
        var partial = target + ".partial";
        using var process = new Process { StartInfo = PostgresCommand(options.Value.PgDumpPath, connection) };
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(partial, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                process.Start();
                // Drain diagnostics without storing or logging credentials that a provider might echo.
                var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
                await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                await errors;
                if (process.ExitCode != 0 || output.Length == 0)
                    throw new InvalidOperationException($"pg_dump failed with exit code {process.ExitCode}.");
                await output.FlushAsync(cancellationToken);
            }
            File.Move(partial, target, true);
            return target;
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
            catch (InvalidOperationException) { /* Process did not start. */ }
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private async Task<string> BackupSqlServerAsync(string slot, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing SQL Server connection string."));
        await connection.OpenAsync(cancellationToken);
        var directory = options.Value.SqlServerDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            await using var location = connection.CreateCommand();
            location.CommandText = "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'))";
            directory = await location.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Configure DatabaseBackup:SqlServerDirectory on the database server.");
        // This path belongs to the database server, which may use a different OS from the web host.
        var separator = directory.Contains('\\') ? "\\" : "/";
        var target = directory.TrimEnd('/', '\\') + separator + SqlServerFileName(connection.Database, slot);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1800;
        // Reuse this slot's file after a crash instead of creating unlimited GUID files.
        // INIT replaces this slot's backup set rather than appending another set to it.
        command.CommandText = "BACKUP DATABASE @database TO DISK = @path WITH INIT, COPY_ONLY, CHECKSUM";
        command.Parameters.AddWithValue("@database", connection.Database);
        command.Parameters.AddWithValue("@path", target);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return target;
    }

    public static string SqlServerFileName(string database, string slot)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(database)))[..12].ToLowerInvariant();
        return $"hawala_{slot}_{identity}.bak";
    }

    public async Task VerifyAsync(string backupPath, CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration["Database:Provider"] ?? "SqlServer", "SqlServer", StringComparison.OrdinalIgnoreCase)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing SQL Server connection string."));
        await connection.OpenAsync(timeout.Token);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1800;
        command.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM";
        command.Parameters.AddWithValue("@path", backupPath);
        try { await command.ExecuteNonQueryAsync(timeout.Token); }
        catch (SqlException ex) when (ex.Number == 262)
        {
            // Leave the .created receipt intact. The worker can retry verification
            // without issuing BACKUP again or falsely marking this file verified.
            throw new InvalidOperationException("Backup was created, but SQL Server denied VERIFYONLY permission. Grant an appropriate verification permission or verify using an authorized maintenance account.", ex);
        }
    }
}
