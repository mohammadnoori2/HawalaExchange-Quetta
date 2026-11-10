using HawalaExchange.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Extensions.Configuration;

namespace HawalaExchange.PerformanceTests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hawala-backup-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(7, 29, null)]
    [InlineData(7, 30, "20261107_1200")]
    [InlineData(12, 29, "20261107_1200")]
    [InlineData(12, 30, "20261107_1700")]
    [InlineData(19, 29, "20261107_1700")]
    [InlineData(19, 30, null)]
    public void Schedule_uses_Kabul_time_independently_of_server_timezone(int hour, int minute, string? expected)
    {
        Assert.Equal(expected, DatabaseBackupSchedule.LatestDue(new DateTimeOffset(2026, 11, 7, hour, minute, 0, TimeSpan.Zero), "Asia/Kabul"));
    }

    [Fact]
    public async Task Successful_slot_is_persisted_across_worker_restarts_and_afternoon_runs_separately()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("backup.dump");
        await Worker(runner.Object).RunDueAsync(Noon, default);
        await Worker(runner.Object).RunDueAsync(Noon.AddMinutes(10), default);
        await Worker(runner.Object).RunDueAsync(Noon.AddHours(5), default);
        runner.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Failure_does_not_mark_slot_complete_and_is_retried()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.SetupSequence(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Controlled failure")).ReturnsAsync("backup.dump");
        var worker = Worker(runner.Object);
        await Assert.ThrowsAsync<IOException>(() => worker.RunDueAsync(Noon, default));
        await worker.RunDueAsync(Noon.AddMinutes(5), default);
        runner.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Disabled_worker_and_time_before_noon_do_not_create_backups()
    {
        var runner = new Mock<IDatabaseBackupRunner>(MockBehavior.Strict);
        await Worker(runner.Object, false).RunDueAsync(Noon, default);
        await Worker(runner.Object).RunDueAsync(Noon.AddMinutes(-1), default);
    }

    [Fact]
    public async Task Concurrent_workers_do_not_duplicate_backup()
    {
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource<string>();
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => { started.SetResult(); return finish.Task; });
        var first = Worker(runner.Object).RunDueAsync(Noon, default);
        await started.Task;
        await Worker(runner.Object).RunDueAsync(Noon, default);
        finish.SetResult("backup.dump");
        await first;
        runner.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Postgres_process_keeps_password_out_of_arguments_and_preserves_TLS_verification()
    {
        var command = DatabaseBackupRunner.PostgresCommand("pg_dump", "Host=localhost;Port=5433;Database=hawala;Username=backup;Password=private-password;SSL Mode=VerifyFull;Root Certificate=/private/ca.pem");
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.Equal(new[] { "--format=custom", "--no-password" }, command.ArgumentList);
        Assert.Equal("private-password", command.Environment["PGPASSWORD"]);
        Assert.Equal("hawala", command.Environment["PGDATABASE"]);
        Assert.Equal("5433", command.Environment["PGPORT"]);
        Assert.Equal("verify-full", command.Environment["PGSSLMODE"]);
        Assert.Equal("/private/ca.pem", command.Environment["PGSSLROOTCERT"]);
    }

    [Fact]
    public async Task Missing_pg_dump_does_not_leave_partial_or_completed_backup()
    {
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSQL",
            ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=unused;Username=unused"
        }).Build();
        var runner = new DatabaseBackupRunner(configuration, Options.Create(new DatabaseBackupOptions
        {
            Directory = directory, PgDumpPath = Path.Combine(directory, "missing-pg-dump")
        }));
        await Assert.ThrowsAnyAsync<Exception>(() => runner.CreateAsync("20261107_1200", default));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Unsupported_provider_is_rejected_without_running_a_process()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Database:Provider"] = "Unknown" }).Build();
        var runner = new DatabaseBackupRunner(configuration, Options.Create(new DatabaseBackupOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.CreateAsync("20261107_1200", default));
    }

    [Fact]
    public async Task Cancellation_releases_lock_and_does_not_mark_slot_complete()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.SetupSequence(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException()).ReturnsAsync("backup.dump");
        var worker = Worker(runner.Object);
        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.RunDueAsync(Noon, default));
        Assert.Empty(Directory.GetFiles(directory, "*.completed"));
        await worker.RunDueAsync(Noon, default);
        Assert.Single(Directory.GetFiles(directory, "*.completed"));
    }

    [Fact]
    public async Task Next_day_has_its_own_schedule_markers()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("backup.dump");
        var worker = Worker(runner.Object);
        await worker.RunDueAsync(Noon, default);
        await worker.RunDueAsync(Noon.AddDays(1), default);
        Assert.Equal(2, Directory.GetFiles(directory, "*.completed").Length);
    }

    private static readonly DateTimeOffset Noon = new(2026, 11, 7, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Verification_failure_retries_existing_backup_after_restart_without_creating_another_file()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("existing.bak");
        runner.SetupSequence(x => x.VerifyAsync("existing.bak", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("CREATE DATABASE permission denied"))
            .Returns(Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Worker(runner.Object).RunDueAsync(Noon, default));
        Assert.Empty(Directory.GetFiles(directory, "*.completed"));
        await Worker(runner.Object).RunDueAsync(Noon.AddMinutes(1), default);
        runner.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(x => x.VerifyAsync("existing.bak", It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Single(Directory.GetFiles(directory, "*.completed"));
    }

    [Fact]
    public async Task Persistent_verification_failure_creates_only_one_backup_per_scheduled_slot()
    {
        var runner = new Mock<IDatabaseBackupRunner>();
        runner.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("existing.bak");
        runner.Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Controlled verification failure"));
        foreach (var at in new[] { Noon, Noon.AddMinutes(1), Noon.AddHours(5), Noon.AddHours(5).AddMinutes(1) })
            await Assert.ThrowsAsync<InvalidOperationException>(() => Worker(runner.Object).RunDueAsync(at, default));
        runner.Verify(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void Sql_server_file_name_is_stable_for_a_slot_and_isolated_between_databases()
    {
        var first = DatabaseBackupRunner.SqlServerFileName("Quetta", "20261108_1200");
        Assert.Equal(first, DatabaseBackupRunner.SqlServerFileName("Quetta", "20261108_1200"));
        Assert.NotEqual(first, DatabaseBackupRunner.SqlServerFileName("Other", "20261108_1200"));
        Assert.NotEqual(first, DatabaseBackupRunner.SqlServerFileName("Quetta", "20261108_1700"));
        Assert.EndsWith(".bak", first);
        Assert.DoesNotContain("/", first);
    }

    private DatabaseBackupWorker Worker(IDatabaseBackupRunner runner, bool enabled = true) => new(
        Options.Create(new DatabaseBackupOptions { Enabled = enabled, Directory = directory }), runner,
        NullLogger<DatabaseBackupWorker>.Instance);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
