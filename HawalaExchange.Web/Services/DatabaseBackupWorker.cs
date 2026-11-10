using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HawalaExchange.Web.Services;

public sealed class DatabaseBackupOptions
{
    public bool Enabled { get; set; } = true;
    public string TimeZoneId { get; set; } = "Asia/Kabul";
    public string Directory { get; set; } = "App_Data/DatabaseBackups";
    public string PgDumpPath { get; set; } = "pg_dump";
    public string? SqlServerDirectory { get; set; }
}

public static class DatabaseBackupSchedule
{
    public static string? LatestDue(DateTimeOffset utcNow, string timeZoneId)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        var hour = local.Hour >= 17 ? 17 : local.Hour >= 12 ? 12 : 0;
        return hour == 0 ? null : local.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + $"_{hour:00}00";
    }
}

public interface IDatabaseBackupRunner
{
    Task<string> CreateAsync(string slot, CancellationToken cancellationToken);
    Task VerifyAsync(string backupPath, CancellationToken cancellationToken);
}

public sealed class DatabaseBackupWorker(IOptions<DatabaseBackupOptions> options,
    IDatabaseBackupRunner runner, ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        logger.LogInformation("Database backups scheduled for 12:00 and 17:00 in {TimeZone}.", options.Value.TimeZoneId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(DateTimeOffset.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // Provider exception messages can contain connection information. Log only the type.
            catch (Exception ex) { logger.LogError("Database backup cycle failed ({ErrorType}); retrying in one minute. Created backups are reused for verification.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        var slot = DatabaseBackupSchedule.LatestDue(now, options.Value.TimeZoneId);
        if (slot is null) return;
        var directory = options.Value.Directory;
        System.IO.Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        FileStream lease;
        try { lease = new FileStream(Path.Combine(directory, ".backup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; } // Another process using this directory is already backing up.
        await using (lease)
        {
            var marker = Path.Combine(directory, slot + ".completed");
            if (File.Exists(marker)) return;
            var created = Path.Combine(directory, slot + ".created");
            string result;
            if (File.Exists(created))
            {
                result = await File.ReadAllTextAsync(created, cancellationToken);
                if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("Empty backup creation receipt.");
            }
            else
            {
                result = await runner.CreateAsync(slot, cancellationToken);
                // Save creation separately: VERIFYONLY may fail even though BACKUP succeeded.
                await WriteReceiptAsync(created, result, cancellationToken);
                logger.LogInformation("Database backup for {Slot} created: {BackupPath}; verification pending.", slot, result);
            }
            await runner.VerifyAsync(result, cancellationToken);
            await WriteReceiptAsync(marker, result, cancellationToken);
            logger.LogInformation("Database backup for {Slot} completed: {BackupPath}", slot, result);
        }
    }

    private static async Task WriteReceiptAsync(string path, string value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, value, cancellationToken);
        File.Move(temporary, path, true);
    }
}
